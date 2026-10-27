# CausaFixIt

Suite de herramientas para Autodesk Revit 2024–2026:

- **Excel Link** — intercambio de parámetros con archivos .xlsx locales y edición en tabla, con revisión de tres estados antes de escribir en el modelo.
- **Project Setup** — crea plantas por nivel con templates y nombres, crea sheets y coloca las vistas para que una referencia del edificio caiga en el mismo punto de todos los planos.

| Documento | Para qué |
|---|---|
| [docs/GUIA.md](docs/GUIA.md) | Compilar, instalar, usar y probar Excel Link (en español) |
| [docs/PROJECT_SETUP.md](docs/PROJECT_SETUP.md) | Usar y probar Project Setup |
| [docs/AGREGAR_MODULO.md](docs/AGREGAR_MODULO.md) | Añadir una herramienta a CausaFixIt |
| [docs/MATRIZ.md](docs/MATRIZ.md) | Funciones implementadas, verificadas, restringidas y pendientes |
| [docs/ARQUITECTURA.md](docs/ARQUITECTURA.md) | Proyectos, hilos, identidad de parámetros y documentos, decisiones |
| [docs/FORMATO_LIBRO.md](docs/FORMATO_LIBRO.md) | Esquema del libro .xlsx |
| [docs/CHECKPOINT.md](docs/CHECKPOINT.md) | Estado, verificación realizada y próximos pasos |
| [docs/GITHUB.md](docs/GITHUB.md) | Compilar en GitHub y descargar el instalador |

Inicio rápido (Windows, con Revit instalado):

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1      # pruebas + builds por año + paquete en dist\
dist\CausaFixIt-0.2.0\Install.cmd            # solo para el usuario actual
dist\CausaFixIt-0.2.0\Install-AllUsers.cmd   # para todos los usuarios (administrador)
```

`samples/` contiene un libro de ejemplo generado por CausaFixIt (Lighting Fixtures de prueba) para ver el formato en Excel.
