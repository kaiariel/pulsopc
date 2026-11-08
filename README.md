# PulsoPC

**Diagnóstico, limpieza y mapa del disco para Windows 10 y 11, en un solo `.exe` de 436 KB.** Sin instalación, sin conexión a Internet, sin telemetría y sin Electron. Código abierto y de dominio público.

PulsoPC nació para responder dos preguntas concretas sobre una PC que se trababa hasta tener que apretar Ctrl+Alt+Supr y que perdía decenas de gigas de disco sin explicación:

1. **¿Por qué se traba?** No «tenés poca RAM» en general, sino qué proceso, qué servicio o qué extensión del Explorador lo provocó y a qué hora.
2. **¿Qué está ocupando el disco y qué se puede borrar sin romper nada?** Como WizTree, pero diciendo qué es cada cosa.

[**Descargar la última versión**](https://github.com/kaiariel/pulsopc/releases/latest) · [Qué cambió](CHANGELOG.md) · [Cómo está hecho](docs/ARQUITECTURA.md) · [Lo que falta](docs/ROADMAP.md)

> La interfaz está en español.

---

## Empezar

1. Bajá `PulsoPC.exe` desde [Releases](https://github.com/kaiariel/pulsopc/releases/latest).
2. Guardalo en un **disco local** (por ejemplo `C:\Herramientas\PulsoPC\`). Un programa que pide administrador puede fallar al arrancar desde una unidad de red o desde Google Drive o OneDrive.
3. Abrilo. Windows pide permiso de administrador: hace falta para leer la tabla del disco, limpiar carpetas del sistema, tocar servicios y correr SFC o DISM.
4. Empezá por **Resumen**. Mide el equipo en vivo, te dice qué lo frena y cada hallazgo trae un botón que te lleva a la solución.

**Requisitos:** Windows 10 22H2 o Windows 11. Los dos ya traen .NET Framework 4.8, así que no hay que instalar nada más.

### Si Windows SmartScreen o el antivirus lo frena

El ejecutable no está firmado digitalmente (un certificado de firma cuesta dinero cada año) y pide administrador. Esa combinación hace que SmartScreen muestre «Windows protegió su PC». Para abrirlo: **Más información → Ejecutar de todas formas**.

Si preferís no confiar en un binario ajeno, compilalo vos mismo con el código de este repositorio (ver [Compilar](#compilar)). Cada versión publica también el hash SHA-256 del `.exe` para que puedas comprobar que es el mismo archivo:

```powershell
Get-FileHash .\PulsoPC.exe -Algorithm SHA256
```

---

## Qué hace

| Sección | Para qué sirve |
|---|---|
| **Resumen** | CPU, memoria, disco y espacio en vivo, con un puntaje de salud. Detecta poca RAM, disco mecánico, poco espacio, salud SMART, disco o CPU saturados, demasiados programas al inicio, días sin reiniciar con Inicio rápido, plan de energía, reinicio pendiente, dos antivirus a la vez y errores de disco, hardware o apagados en el visor de eventos. También encuentra cosas que otras herramientas no miran: **servicios deshabilitados que rompen a otros** (con botón Arreglar), **Windows usando el disco como memoria** (lecturas de página por segundo), **apps repartidas en muchos procesos** («navegador: 26 procesos, 3,0 GB»), **apps de Windows que no arrancan a tiempo** traducidas a un nombre que se entiende («Widgets», «Generador de miniaturas») y **perfiles temporales** al iniciar sesión. |
| **Limpieza** | 28 categorías con casillas: temporales, Windows Update, Delivery Optimization, papelera, miniaturas, informes de error, volcados, registros, cachés de Chrome, Edge, Brave, Vivaldi, Opera y Firefox, Teams, Discord, Slack, VS Code y Cursor, Adobe, shaders, npm, Yarn, pnpm, pip, NuGet, Gradle y Playwright. Muestra cuánto se libera antes de borrar. |
| **Espacio en disco** | **Mapa del disco**: lee directamente la tabla maestra de archivos (MFT) de NTFS, como WizTree. Unos 3 millones de archivos en unos 20 segundos. Árbol desplegable con % de la carpeta padre, tamaño, espacio real en disco, archivos, carpetas y fecha, más un panel por tipo de archivo con colores. **«¿Qué es esto?»**: al elegir una carpeta o archivo, una franja explica qué es y si se puede borrar (unas 50 reglas: `pagefile.sys`, `hiberfil.sys`, WinSxS, puntos de restauración, perfiles temporales, cachés de npm, uv y pnpm, modelos de IA, discos de Docker y WSL...) con el botón para resolverlo. **Sobrantes**: junta en grupos los perfiles temporales, restos de Windows, cachés que se regeneran, volcados, logs grandes, modelos repetidos e instaladores viejos, con su veredicto (se puede borrar, con cuidado, revisalo vos). **Clic derecho**: abrir en el Explorador, mostrar en su carpeta, copiar ruta, ¿qué es esto?, enviar a la papelera. Además: archivos grandes, explorador de carpetas por tamaño y buscador de `node_modules` de proyectos que ya no usás. |
| **Programas de inicio** | Lo que arranca con Windows, para habilitar o deshabilitar igual que el Administrador de tareas. No desinstala nada. |
| **Procesos** | Procesos agrupados por programa con CPU, memoria y disco en vivo. Finalizar, bajar prioridad y abrir ubicación. |
| **Monitor de trabas** | Cada 2 segundos registra RAM libre, lecturas de página, CPU, disco, si la barra de tareas y la ventana activa responden, quién lee más del disco, quién usa más CPU y si hay teclas trabadas. Cuando algo se cuelga, **«Se trabó recién»** (o **Ctrl+Alt+P** desde cualquier ventana) analiza el minuto y medio anterior y dice qué pasó: falta de memoria, Explorador colgado, CPU o disco saturados, Google Drive sin responder o el sistema entero frenado. Usa menos del 1 % de CPU. |
| **Optimizar** | 15 ajustes reversibles con un clic (incluye desactivar el Inicio rápido y la hibernación), punto de restauración opcional y liberador de RAM. |
| **Reparar** | SFC, DISM, chkdsk, TRIM y desfragmentación, restablecer red, Windows Update y Tienda, caché de iconos, índice de búsqueda, reiniciar el Explorador y prueba de RAM. Con consola en vivo y botón Cancelar. |

### Informes para IA, sin clave de API

PulsoPC no se conecta a ninguna IA ni manda nada por su cuenta. En cambio arma informes pensados para pegarle a Claude, ChatGPT o la que uses:

- **Informe para IA** (en Resumen): un Markdown con el diagnóstico y las instrucciones para que la IA lo analice. Opcionalmente suma el monitor de trabas y el mapa del disco. **Anonimizado por defecto**: saca el nombre del equipo, el usuario, los correos y la ruta del perfil.
- **Exportar informe de almacenamiento** (en el Mapa del disco): deja en `%LOCALAPPDATA%\PulsoPC\informes\` un `.md` legible, un `.json` con todos los datos y un `.tsv` con la foto de las carpetas. El próximo informe compara contra esa foto y muestra **qué creció desde la última vez**, que suele ser la pregunta real cuando el disco se llena solo.

Se copia al portapapeles, se guarda como archivo o se abre el chat directamente. Vos decidís qué se comparte.

---

## Seguridad: lo que hace y lo que no

- **Nada se borra sin que lo marques y confirmes.** Antes de limpiar se muestra el tamaño y la lista.
- Lo que borrás desde el Mapa y Sobrantes **va a la papelera**. La excepción son los `node_modules`, que se eliminan directo porque se regeneran con `npm install`.
- Los temporales modificados en las últimas 24 horas no se tocan.
- No sigue enlaces simbólicos ni uniones de carpetas, y no cuenta ni borra archivos que están solo en la nube (Google Drive, OneDrive).
- **Carpetas protegidas**: no se puede borrar desde la app Windows, Program Files, ProgramData, tu perfil, Documentos, Escritorio, Imágenes, Videos, Música, Descargas ni la raíz de una unidad.
- Cada ajuste de Optimizar guarda el valor anterior en `%LOCALAPPDATA%\PulsoPC\estado.ini` y se revierte con un clic hasta dejar el registro como estaba.
- Todo lo que cambia queda anotado en `%LOCALAPPDATA%\PulsoPC\registro.txt` (enlace «Ver registro de cambios» en el menú).
- **No limpia el registro de Windows**: no mejora el rendimiento y es arriesgado.
- **No se conecta a Internet**, no descarga nada, no tiene telemetría ni actualizaciones automáticas.
- La lectura de la MFT es **solo lectura**: abre el volumen sin permiso de escritura.

---

## Línea de comandos

Además de la ventana, el mismo `.exe` tiene modos sin interfaz, útiles para automatizar o para pasarle la salida a una IA:

```text
PulsoPC.exe --informe salida.txt              Diagnóstico completo en texto
PulsoPC.exe --informe-ia salida.md [E:\]      Informe para IA anonimizado, con el mapa de una unidad si se indica
PulsoPC.exe --mapa C:\ salida.txt             Mapa del disco (MFT si hay administrador, si no recorre carpetas)
PulsoPC.exe --mapa-lento C:\ salida.txt       Lo mismo forzando el recorrido de carpetas, para comparar
PulsoPC.exe --exportar-disco C:\ [carpeta]    Informe de almacenamiento: .md, .json y .tsv, con lo que creció
PulsoPC.exe --sobrantes salida.txt            Sobrantes de la unidad de Windows, agrupados
PulsoPC.exe --autotest resultado.txt          125 comprobaciones del motor (no tocan tus archivos)
PulsoPC.exe --captura carpeta [secciones]     Captura de pantalla de cada sección ("mapa=E:" escanea esa unidad antes)
```

---

## Herramientas sueltas

En [`herramientas/`](herramientas) hay scripts de PowerShell que salieron del mismo diagnóstico y sirven por separado:

| Script | Qué hace |
|---|---|
| `monitor-trabas.ps1` | La versión original del monitor de trabas, para equipos donde no quieras abrir PulsoPC. Guarda un CSV por día en `%LOCALAPPDATA%\PulsoPC\`. |
| `bloquear-adobe-explorador.ps1` | Bloquea en el Explorador la extensión de sincronización de Adobe Creative Cloud (`CoreSync_x64.dll`), causa conocida de cuelgues de la barra de tareas. Usa la lista oficial `Shell Extensions\Blocked` y se revierte con `-Revertir`. No desinstala nada. |
| `compactar-docker.ps1` | Achica `docker_data.vhdx`, el disco virtual de Docker Desktop, que no devuelve solo el espacio que liberás con `docker prune`. No borra imágenes, contenedores ni volúmenes. Docker cerrado y como administrador. |
| `probar-mft.ps1` | Compara el mapa leído de la MFT con el recorrido de carpetas y mide los tiempos. Tiene que estar al lado de `PulsoPC.exe`. |

Para ejecutar cualquiera: `powershell -ExecutionPolicy Bypass -File nombre.ps1`.

---

## Compilar

No hace falta Visual Studio completo. Alcanza con **Visual Studio Build Tools 2022** (trae el compilador Roslyn `csc.exe`); el script también lo encuentra en las ediciones Community, Professional y Enterprise.

```powershell
git clone https://github.com/kaiariel/pulsopc.git
cd pulsopc
.\build.ps1                                     # PulsoPC.exe en esta carpeta, pide administrador al abrir
.\build.ps1 -Test -Out C:\ruta\PulsoPC_test.exe # versión de prueba, sin aviso de administrador
```

Sin NuGet, sin paquetes, sin archivo de proyecto: `build.ps1` le pasa a `csc` todos los `.cs` de `src\` y las referencias de .NET Framework que vienen con Windows. Si `assets\pulso.ico` no existe, `make-icon.ps1` lo dibuja.

Para comprobar que todo sigue funcionando después de un cambio:

```powershell
.\PulsoPC_test.exe --autotest resultado.txt   # termina en "TODAS LAS PRUEBAS PASARON"
```

### Estructura

```text
src/             Motor: diagnóstico, limpieza, ajustes, lector de la MFT, monitor, base de conocimiento
src/Pages/       Una clase por sección de la ventana (WinForms, sin diseñador)
src/SelfTest*.cs Las pruebas automáticas de --autotest
herramientas/    Scripts de PowerShell independientes
assets/          Ícono
app.manifest     Nivel de permisos (build.ps1 reemplaza @@LEVEL@@) y compatibilidad con DPI
build.ps1        Compilación
make-icon.ps1    Genera el ícono
```

Cómo funcionan por dentro la lectura de la MFT, el monitor y la base de conocimiento está en [docs/ARQUITECTURA.md](docs/ARQUITECTURA.md).

---

## Contribuir

Los reportes de errores y las mejoras son bienvenidos. Antes de abrir un pull request:

- Mantené la filosofía: un solo `.exe`, sin dependencias externas, sin Internet y nada destructivo sin confirmación.
- Si agregás una regla a «¿Qué es esto?» o a Sobrantes, sumá su prueba en `SelfTest*.cs`.
- Corré `--autotest` y confirmá que todo pasa.

Lo que está pensado y todavía no se hizo está en [docs/ROADMAP.md](docs/ROADMAP.md).

## Licencia

Dominio público, bajo [The Unlicense](LICENSE). Podés copiarlo, modificarlo, venderlo o usarlo en lo que quieras, sin pedir permiso y sin dar crédito.

El software se entrega **tal cual, sin garantías**. PulsoPC está hecho para ser prudente, pero toca configuración de Windows y borra archivos cuando se lo pedís: revisá lo que marcás antes de confirmar.
