# Cambios

## 1.1.0 (noviembre de 2026)

Esta versión salió de diagnosticar a mano un equipo que se trababa y perdía espacio, y de convertir cada cosa que hubo que buscar con comandos en una función de la app.

### Diagnóstico

- **Servicios rotos por dependencias**: detecta servicios deshabilitados de los que dependen otros en automático y cuenta sus errores por día (eventos 7001 y DCOM 10005). Botón para volver al valor de Windows. Caso real: `NcbService` deshabilitado hacía fallar cientos de veces por día a la plataforma de dispositivos conectados, que usan la barra de tareas y Alt+Tab.
- **Presión de memoria real**: además del % de RAM mide las lecturas de página por segundo (Windows usando el disco como memoria) y agrupa los procesos por app: «navegador: 26 procesos, 3,0 GB».
- **Apps que no arrancan a tiempo** (DCOM 10010) con el CLSID traducido a un nombre que se entiende: «Widgets», «Generador de miniaturas».
- **Perfiles temporales**: avisa cuando Windows inició sesión con un perfil temporal (eventos 1511, 1512 y 1552) y explica la relación con el Inicio rápido.

### Monitor de trabas (sección nueva)

- Muestra cada 2 segundos: RAM libre, lecturas de página, CPU, disco y cola, si la barra de tareas y la ventana activa responden (en ms), quién lee más del disco, quién usa más CPU, avisos del controlador de Google Drive y teclas trabadas.
- **«Se trabó recién»** y el atajo global **Ctrl+Alt+P** analizan el minuto y medio anterior y nombran la causa: falta de memoria, Explorador colgado sin falta de recursos, CPU o disco saturados, Drive sin responder o el sistema entero frenado.
- Guarda un CSV por día y puede arrancar solo al abrir PulsoPC.

### Mapa del disco (reemplaza el escaneo por carpeta)

- **Lectura directa de la MFT** de NTFS con administrador: unos 3 millones de archivos en unos 20 segundos. Maneja enlaces duros (aparecen en cada carpeta, el espacio se cuenta una vez), la MFT repartida en una lista de atributos y `$BadClus`. Si no hay administrador o la unidad no es NTFS, recorre las carpetas en paralelo.
- Árbol desplegable con % de la carpeta padre, tamaño, espacio real en disco, archivos, carpetas y fecha. Panel por tipo de archivo con colores; doble clic muestra los archivos más grandes de ese tipo.
- **«¿Qué es esto?»**: unas 50 reglas que explican qué es una carpeta o archivo y si se puede borrar, con el botón de la acción segura.
- **Sobrantes**: agrupa perfiles temporales, restos de Windows, cachés que se regeneran, volcados, logs grandes, modelos de IA repetidos e instaladores viejos, ordenados por veredicto.
- **Clic derecho**: abrir en el Explorador, mostrar en su carpeta, copiar ruta, ¿qué es esto?, enviar a la papelera.

### Informes para IA

- **Informe para IA** en Resumen: Markdown con instrucciones para el modelo, anonimizado por defecto (equipo, usuario, correos y ruta del perfil). Opcionalmente suma el monitor y el mapa.
- **Exportar informe de almacenamiento**: `.md`, `.json` y `.tsv`. Cada informe se compara con el anterior y muestra qué carpetas crecieron.

### Línea de comandos

- Nuevos modos: `--mapa`, `--mapa-lento`, `--informe-ia`, `--sobrantes` y `--exportar-disco`.
- `--autotest` llega a 125 comprobaciones, incluida la MFT con registros armados en memoria.

### Correcciones

- «Abrir ubicación» fallaba en la raíz de una unidad: `"C:\"` entre comillas dejaba la comilla escapada.

## 1.0.0 (octubre de 2026)

Primera versión.

- **Resumen** con métricas en vivo, puntaje de salud y hallazgos con botón hacia la solución.
- **Limpieza** de 28 categorías con vista previa del tamaño.
- **Espacio en disco**: archivos grandes, explorador de carpetas por tamaño y buscador de `node_modules`.
- **Programas de inicio**, **Procesos** agrupados por programa, **Optimizar** con 15 ajustes reversibles y **Reparar** con SFC, DISM, chkdsk y otras herramientas de Windows.
- Registro de cambios en `%LOCALAPPDATA%\PulsoPC\registro.txt` y estado reversible en `estado.ini`.
