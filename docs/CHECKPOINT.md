# Checkpoint – estado para continuar

Fecha: 2026-10-24 · Versión 0.1.0 · Repositorio https://github.com/oxidodefosforo/causafix (rama `main`)

## Módulos

| Módulo | Botón | Proyectos |
|---|---|---|
| Excel Link | Data → Excel Link | `CausaFix.Core`, `CausaFix.ExcelLink`, `CausaFix.Revit`, `CausaFix.Addin/Modules/ExcelLink` |
| Project Setup | Documentation → Project Setup | `CausaFix.ProjectSetup`, `CausaFix.Revit/ProjectSetup`, `CausaFix.Addin/Modules/ProjectSetup` |

Un solo paquete e instalación por año (2024/2025/2026). Los ayudantes MVVM están en `CausaFix.Core/Mvvm` y los comparten todos los módulos.

## Excel Link: causa del bloqueo y corrección (commit 7766110)

Síntoma: revisión con cero cambios y filas *Blocked* con Original/Proposed "(empty)" y Current "–".

Causas confirmadas en el código (no ejecutado en Revit):
1. `ParamAccess.LockReason` y `ModelReader.ColumnLock` bloqueaban cuando `Parameter.UserModifiable` era false. Esa bandera describe la edición en la interfaz de Revit, no la API; muchos parámetros compartidos la tienen en false y se pueden escribir. Ahora solo bloquean `IsReadOnly`, grupos, worksharing y vínculos; los casos que CausaFix no soporta se marcan **Unsupported**, no como read-only.
2. La editabilidad de columna se decidía con una muestra de elementos; una columna entera quedaba marcada read-only en los metadatos del libro.
3. `WorkbookImporter` trataba esas columnas como read-only: generaba `Proposal.Invalid` con `RawValue.Empty` como original **antes** de consultar Revit, de ahí "(empty)" y "–". Ahora el importador nunca bloquea por metadatos; el estado real se lee en Revit por celda.

Además: esquema 2 con migración de libros de esquema 1, referencias ElementId editables con lista "Nombre [Id]", una escritura por destino de tipo, `TypeParameterConflict` y error de grupo sin escrituras parciales, "Multiple conflicting values" en Proposed, estados precisos con acción y diagnóstico por fila + CSV.

## Verificado aquí

| Verificación | Resultado |
|---|---|
| Pruebas automáticas (reglas + flujos con mocks) en .NET 8 y .NET Framework 4.8 | 81/81 |
| GitHub Actions (Windows): pruebas + compilación 2024/2025/2026 de todo, incluida la WPF, contra las referencias reales de la API | Verde |
| Libro .xlsx abierto y re-guardado por LibreOffice → reimportación con 0 cambios | OK |
| Plan de Project Setup 10 niveles × 4 conjuntos (2000 vistas y 500 sheets existentes) | ~10 ms (núcleo, sin Revit) |

## No verificado (requiere Revit)

1. Excel Link: un parámetro de instancia y uno de tipo cambiados desde Excel y leídos de nuevo en Revit (`GUIA.md §12`).
2. Project Setup: niveles → vistas/templates/nombres → sheets → colocación con error medido ≤ 0,1 mm (`PROJECT_SETUP.md §7`), reejecución sin duplicados, rollback.
3. Tiempos reales en Revit (10 000 × 20 en Excel Link; 10 × 4 en Project Setup).
4. Instaladores en Windows (Install / Install-AllUsers).

## Decisiones

- Project Setup ejecuta todo en **un callback** de ExternalEvent con un TransactionGroup: no hay transacciones entre callbacks; por eso no se puede cancelar a mitad (se cancela antes de empezar o durante los picks).
- Alineación con un bucle común (`AlignmentRunner` + `IViewportProbe`) probado con sondas simuladas y ejecutado en Revit con `RevitViewportProbe`.
- Split crops bloqueados; recortes no rectangulares admitidos (una transformación).
- Asociaciones fila ↔ vista/sheet con Extensible Storage (esquema `CausaFixProjectSetup`, GUID fijo en `SetupStorage.cs`).
- Las anclas y elecciones de elementos no viajan entre documentos en los perfiles.

## Próximo paso

1. Descargar el paquete de la última ejecución verde de Actions e instalar.
2. Prueba guiada de Excel Link y de Project Setup en copias de modelos; anotar resultados en la columna R de `MATRIZ.md`.
3. Pendientes: parámetros adicionales de sheet/title block, alineación del título, copiar recorte de una vista de referencia.
