# Carpeta `instalador\` — insumos de los ejecutables

Esta carpeta **no se entrega suelta**: son los **insumos** con los que `build\generar_exes.ps1`
arma los ejecutables autocontenidos.

- `bin\` — DLLs compiladas del addon (las empaqueta `BrosLMV-Instalador.exe`).
- `scripts\` — scripts `.csx` de ejemplo.
- `sql\` — `provision_empresa.sql` y `desprovision_empresa.sql`.
- `assets\` — logos + `BrosLMV.ico`.
- `Instalar.ps1` / `Desinstalar.ps1` — métodos **manuales de respaldo** (uso avanzado).

**Para instalar/desinstalar normalmente usa los ejecutables** de `dist\`:
`BrosLMV-Instalador-X.Y.Z.exe` y `BrosLMV-Desinstalador-X.Y.Z.exe` (el nombre lleva la
versión para no confundir cuál mandar).

Guía completa: [`..\docs\INSTALACION.md`](../docs/INSTALACION.md).
Compilar: [`..\docs\DESARROLLO.md`](../docs/DESARROLLO.md).
