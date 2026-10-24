# CausaFix

Suite de herramientas para Autodesk Revit 2024–2026. Primera herramienta: **Excel Link** — intercambio de parámetros con archivos .xlsx locales y edición en tabla, con revisión de tres estados antes de escribir en el modelo.

| Documento | Para qué |
|---|---|
| [docs/GUIA.md](docs/GUIA.md) | Compilar, instalar, usar y probar Excel Link (en español) |
| [docs/AGREGAR_MODULO.md](docs/AGREGAR_MODULO.md) | Añadir una herramienta a CausaFix |
| [docs/MATRIZ.md](docs/MATRIZ.md) | Funciones implementadas, verificadas, restringidas y pendientes |
| [docs/ARQUITECTURA.md](docs/ARQUITECTURA.md) | Proyectos, hilos, identidad de parámetros y documentos, decisiones |
| [docs/FORMATO_LIBRO.md](docs/FORMATO_LIBRO.md) | Esquema del libro .xlsx |
| [docs/CHECKPOINT.md](docs/CHECKPOINT.md) | Estado, verificación realizada y próximos pasos |
| [docs/GITHUB.md](docs/GITHUB.md) | Compilar en GitHub y descargar el instalador |

Inicio rápido (Windows, con Revit instalado):

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1      # pruebas + builds por año + paquete en dist\
dist\CausaFix-0.1.0\Install.cmd            # solo para el usuario actual
dist\CausaFix-0.1.0\Install-AllUsers.cmd   # para todos los usuarios (administrador)
```

`samples/` contiene un libro de ejemplo generado por CausaFix (Lighting Fixtures de prueba) para ver el formato en Excel.
