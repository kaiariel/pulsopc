# Lo que falta

Ideas pensadas y todavía sin hacer, con el problema real que resolverían. Si querés tomar alguna, abrí un issue antes para no duplicar trabajo.

## Diagnóstico

| Idea | Qué resolvería | Tamaño |
|---|---|---|
| **Extensiones del Explorador** | Listar las DLL de otros fabricantes cargadas en `explorer.exe` (íconos superpuestos, menú contextual, sincronización en la nube) con un interruptor para bloquear o desbloquear cada una en `Shell Extensions\Blocked`. Hoy eso lo hace a mano `herramientas/bloquear-adobe-explorador.ps1`, solo para Adobe. Son una causa frecuente de que la barra de tareas se cuelgue. | Mediano |
| **Salud de Google Drive y OneDrive** | Contar por día los avisos del controlador de Drive («no matching IRPs»: pedidos a la unidad que tardaron tanto que el programa se rindió), mostrar dónde está la caché y cuánto ocupa, y avisar si hay repositorios Git o `node_modules` dentro de la unidad sincronizada, que la hacen trabajar sin parar. | Chico |
| **Cachés de herramientas de IA y desarrollo en Limpieza** | Máquinas virtuales de apps de escritorio de IA, historiales de agentes de código, `.cache\huggingface`, discos de WSL, uv y bun. Cada una con la explicación de qué se pierde al borrarla. Hoy «¿Qué es esto?» y Sobrantes ya las reconocen, pero no están en Limpieza. | Chico |
| **Compactar discos virtuales** | Un botón para lo que hace `herramientas/compactar-docker.ps1`, extendido a los discos de WSL (`ext4.vhdx`). Estos discos crecen pero nunca devuelven el espacio solos. | Chico |

## Almacenamiento

| Idea | Qué resolvería | Tamaño |
|---|---|---|
| **Mapa de rectángulos** (*treemap squarified*) | Cada archivo como un rectángulo proporcional a su tamaño, coloreado por tipo, como WizTree o WinDirStat. Es lo que más rápido muestra «qué es ese bloque gigante». | Mediano |
| **Filtrar el árbol por tipo** | Hacer clic en una extensión del panel de tipos y ver solo dónde están esos archivos. | Chico |
| **Duplicados de verdad** | Hoy se marcan como «posible duplicado» los archivos grandes con igual nombre y tamaño. Falta confirmar: mismo tamaño, después hash parcial, después hash completo, y dejar elegir cuál conservar (el resto va a la papelera). | Mediano |
| **Archivos solo en la nube** | Mostrar lo que está en Drive u OneDrive sin ocupar disco, y lo que se podría liberar marcándolo «solo en línea». | Mediano |
| **Historial de crecimiento en la ventana** | El informe exportado ya compara con el anterior. Falta verlo en la app: «desde el martes, esta carpeta creció 30 GB». | Chico |

## General

- **Firma digital del ejecutable**, para que SmartScreen deje de advertir. Requiere un certificado de firma de código.
- **Interfaz en inglés.** Hoy todos los textos están en el código en español.
- **Más reglas para «¿Qué es esto?»**: cada regla nueva tiene que tener su prueba en `SelfTest*.cs`.
