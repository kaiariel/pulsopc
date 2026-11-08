# Cómo está hecho PulsoPC

PulsoPC es una app de WinForms en C# sobre .NET Framework 4.8, compilada con `csc` sin proyecto ni paquetes. La decisión de fondo es que funcione en cualquier Windows 10 u 11 recién instalado copiando un solo archivo: .NET Framework 4.8 ya viene con el sistema, y todo lo que no está en la biblioteca estándar se resuelve con llamadas directas a la API de Windows (`DllImport`).

Son unas 8.300 líneas en 72 archivos. Ningún archivo pasa de unas 400 líneas: las clases grandes se reparten en varios archivos con `partial`.

## Capas

```text
Program.cs / Cli.cs        Entrada: ventana o modo de línea de comandos
MainForm.cs + Pages/       Interfaz: una clase por sección, sin diseñador visual (UI.cs tiene los controles comunes)
Diagnostics, DiagRules*,   Motor de diagnóstico: lee contadores, WMI, eventos y registro, y devuelve hallazgos
SysHealth, Events,
Hardware
Cleaner*, CleanCatalog*    Limpieza: catálogo de categorías + borrado con reglas de seguridad
Tweaks*                    Ajustes reversibles con estado guardado
DiskIndex, MftReader       Índice del disco en memoria
Known, Leftovers           Base de conocimiento: qué es cada cosa y qué sobra
HangMonitor                Monitor de trabas
AiReport, StorageExport    Informes para IA
SelfTest*                  Pruebas automáticas
Util, Native               Utilidades, rutas protegidas, apertura del Explorador, P/Invoke
```

La interfaz nunca hace trabajo pesado en su hilo: los escaneos y diagnósticos corren en hilos de fondo y la página se refresca con un temporizador o con `BeginInvoke`.

## Lector de la MFT (`MftReader.cs`)

Es lo que hace que el mapa del disco tarde segundos en vez de minutos. En vez de pedirle a Windows carpeta por carpeta, lee la *Master File Table* de NTFS directamente del volumen, en bloques de 16 MB secuenciales.

1. Abre `\\.\C:` con `GENERIC_READ` y comparte lectura y escritura (no bloquea a nadie y no puede escribir). Requiere administrador.
2. `FSCTL_GET_NTFS_VOLUME_DATA` da la geometría: tamaño de cluster, tamaño de registro (1024 bytes casi siempre) y dónde empieza la MFT.
3. El **registro 0** es la propia MFT. Su atributo `$DATA` dice en qué tramos (*runs*) del disco está guardada, porque la MFT también se fragmenta. Cuando un volumen tiene millones de archivos, esos tramos ya no entran en el registro 0 y quedan repartidos en otros registros a través de un `$ATTRIBUTE_LIST`. `MftRuns` sigue esa lista, lee cada registro de extensión y junta los tramos en orden.
4. Cada registro pasa por el **fixup** (*update sequence array*): NTFS reemplaza los dos últimos bytes de cada sector de 512 bytes por un número de control, y hay que restaurarlos antes de leer el registro. Si el control no coincide, el registro se descarta.
5. `ParseRecord` saca de cada registro en uso:
   - `$FILE_NAME`: nombre y número de registro de la carpeta padre. Se prefiere el nombre Win32 al nombre corto 8.3. Un archivo con **enlaces duros** tiene varios `$FILE_NAME` con padres distintos: se guardan todos.
   - `$DATA` sin nombre: tamaño lógico y espacio asignado. Para archivos residentes (los muy chicos, guardados dentro del propio registro) el espacio asignado es 0.
   - `$STANDARD_INFORMATION`: fecha de modificación y atributos.
6. `Build` arma el árbol uniendo cada registro con su padre, y suma tamaños hacia arriba. Los enlaces duros aparecen en cada carpeta donde están (como en el Explorador), pero su espacio en disco se cuenta una sola vez en el total, que es lo que realmente ocupa.

Casos que costaron un error real y tienen su prueba en `SelfTest`:

- **`$BadClus`** (registro 8) declara un `$DATA` disperso del tamaño de toda la unidad. Si se suma, el disco aparece con el doble de lo que tiene. Se fuerza su espacio asignado a 0.
- **Enlaces duros**: pnpm y uv los usan masivamente. Si se toma un solo padre, carpetas enteras quedan vacías y faltan cientos de miles de archivos.
- **MFT en una lista de atributos**: con unos 4 millones de archivos los tramos de la MFT ya no entran en el registro 0.

Si algo falla (unidad que no es NTFS, sin administrador, geometría rara), `DiskIndex` vuelve al plan B.

## Recorrido de carpetas (`DiskIndex.cs`)

El plan B, y el único para ReFS o sin administrador. Recorre el árbol con varios hilos que toman carpetas de una cola compartida, usando `FindFirstFileExW` con `FindExInfoBasic` y `FIND_FIRST_EX_LARGE_FETCH` (sin nombres cortos y con buffers grandes), sobre rutas `\\?\` para pasar el límite de 260 caracteres. Cada hilo junta sus estadísticas por separado y se unen al final, sin bloqueos durante el recorrido.

No entra en enlaces simbólicos ni uniones de carpetas (`FILE_ATTRIBUTE_REPARSE_POINT`) y no cuenta archivos que están solo en la nube (`RECALL_ON_DATA_ACCESS`, `OFFLINE`), porque leerlos los descargaría.

Los dos caminos producen el mismo `DiskIndex`: un árbol de `DNode` (nombre, padre, hijos, tamaño, asignado, archivos, carpetas, fecha) más estadísticas por extensión y los archivos más grandes. Todas las vistas leen de ahí.

## Base de conocimiento (`Known.cs`, `Leftovers.cs`)

`Known.Explain(ruta, esCarpeta)` es una lista ordenada de reglas escritas a mano. Cada una devuelve un título, un veredicto (`Si`, `Cuidado`, `No`, `Revisar`), qué es, cómo resolverlo y, cuando corresponde, a qué sección de PulsoPC ir, qué comando copiar o si se puede mandar a la papelera. Si ninguna regla aplica devuelve `null`: la app prefiere no decir nada antes que inventar.

`Leftovers.Find(indice)` recorre el árbol ya armado (no vuelve a tocar el disco), le pregunta a `Known` por cada carpeta candidata y junta los resultados en grupos: «Perfil temporal de usuario (5 carpetas, 3,4 MB)». También marca posibles duplicados entre los archivos grandes: mismo nombre y mismo tamaño, de 200 MB o más, fuera de Windows, de los programas instalados y de `node_modules`.

## Monitor de trabas (`HangMonitor.cs`)

Un hilo de fondo que cada 2 segundos toma una muestra:

- Memoria libre y comprometida, lecturas de página (`Memory\Pages Input/sec`), CPU, disco y cola (`PhysicalDisk\Current Disk Queue Length`).
- Si la **barra de tareas** responde: busca la ventana `Shell_TrayWnd` y le manda un mensaje vacío con `SendMessageTimeout` (2 s de tope), además de `IsHungAppWindow`. Lo mismo con la ventana activa.
- Qué proceso leyó más del disco y cuál usó más CPU desde la muestra anterior.
- Teclas que Windows cree apretadas (`GetAsyncKeyState`): una tecla trabada bloquea clics y atajos.
- El hueco entre muestras: si el propio monitor tardó 6 segundos en despertar, el sistema entero estaba frenado.

Guarda las últimas 2 horas en memoria y todo en un CSV diario. `Analyze` toma la ventana de tiempo anterior a la marca y aplica reglas en orden de gravedad: falta de memoria (menos de 500 MB libres o más de 20.000 lecturas de página por segundo), barra de tareas o ventana colgada sin falta de recursos, CPU o disco saturados, sistema frenado. Si no ve nada raro lo dice, en vez de inventar una causa.

El atajo Ctrl+Alt+P es un `RegisterHotKey` global, así funciona aunque PulsoPC esté minimizado.

## Informes para IA (`AiReport.cs`, `StorageExport.cs`)

Los informes son texto plano pensado para un modelo de lenguaje: arriba van las instrucciones (qué analizar, qué no recomendar, cómo responder), abajo los datos en tablas Markdown.

`AiReport.Anonymize` reemplaza el nombre del equipo, el nombre de usuario y la ruta del perfil por valores genéricos, y cualquier correo por `[correo]`.

`StorageExport` escribe tres archivos con la misma marca de tiempo:

- `.md`: resumen legible con las carpetas más grandes, los tipos de archivo, los sobrantes con su veredicto y lo que creció.
- `.json`: los mismos datos completos, para que una IA con acceso a archivos (o un script) los procese.
- `.tsv`: una «instantánea» con el tamaño de cada carpeta de cierto peso. El próximo informe de la misma unidad busca la instantánea anterior y calcula las diferencias.

## Seguridad al borrar (`Util.cs`, `Cleaner*.cs`)

Toda operación de borrado pasa primero por `Safe.CanDelete`, una lista de rutas protegidas (Windows, Program Files, ProgramData, el perfil y sus carpetas conocidas, la raíz de cada unidad) que se compara con la ruta ya normalizada. Desde el Mapa y Sobrantes se borra con `FileSystem.DeleteDirectory` / `DeleteFile` de `Microsoft.VisualBasic` y `RecycleOption.SendToRecycleBin`, o sea a la papelera del Explorador. Si la papelera no acepta el elemento (pasa con algunos árboles muy grandes) se muestra el error de Windows: nunca se cae a un borrado permanente sin preguntar.

## Pruebas (`SelfTest*.cs`)

`--autotest` corre 125 comprobaciones sin tocar archivos del usuario:

- Limpieza y reglas de seguridad sobre carpetas temporales creadas para la prueba.
- Parser de la MFT con registros armados byte a byte en memoria: fixup, runlists, enlaces duros, `$BadClus`, lista de atributos.
- Base de conocimiento y Sobrantes sobre un árbol inventado con rutas reales del equipo.
- Monitor: muestreo real de unos segundos y análisis de casos armados (falta de memoria, Explorador colgado, sin anomalías).
- Anonimizado de informes, formato del JSON y comparación entre instantáneas.

Las pruebas que necesitan administrador y una unidad real están en `herramientas/probar-mft.ps1`.
