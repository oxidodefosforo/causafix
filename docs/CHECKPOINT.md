# Checkpoint – estado para continuar

Fecha: 2026-10-24 · Versión 0.1.0

## Qué hay

- Solución completa: `CausaFix.Core`, `CausaFix.ExcelLink`, `CausaFix.Revit`, `CausaFix.Addin`, pruebas, stubs de compilación, instalador, `build.ps1`, documentación.
- Excel Link implementado de extremo a extremo en código: pestañas, alcance, parámetros, Preview/Edit, exportación, importación con revisión de tres estados, aplicación atómica, informes, perfiles, schedules (datos y presentación), spatial, vínculos de solo lectura, Project Information e inventarios.

## Qué se verificó aquí (Linux, sin Windows ni Revit)

| Verificación | Resultado |
|---|---|
| `CausaFix.Core` y `CausaFix.ExcelLink` compilan para net48 y net8.0 | OK |
| 50 pruebas automáticas (reglas + flujos completos con un Revit en memoria), en .NET 8 y en .NET Framework 4.8 (Mono 6.8) | 50/50 |
| Libro generado abierto por LibreOffice Calc; su copia re-guardada se reimporta con **0 cambios** | OK |
| `CausaFix.Revit` compila para 2024 (net48) y 2026 (net8) contra stubs de la API escritos desde la documentación | OK (no es prueba en Revit) |
| XAML bien formado | OK (no compilado: requiere Windows) |

Rendimiento medido **solo en el núcleo** (sin Revit), 10 000 filas × 20 parámetros, en la máquina virtual de este entorno (Linux x64, .NET 8):
- construir + escribir .xlsx: ~2,2 s · leer: ~0,9 s · validar + comparar: ~0,5 s · archivo ~2,7 MB.
- En .NET Framework 4.8 (Mono) tiempos similares (~2,2 s / 1,0 s / 0,6 s).
La lectura de parámetros dentro de Revit **no está medida**: ejecutar la prueba guiada con 10 000 elementos y anotar equipo y tiempos aquí.

## Qué NO se pudo verificar (y cómo hacerlo)

1. **Compilar `CausaFix.Addin` (WPF) y `CausaFix.Revit` contra las DLL reales** → en Windows: `.\build.ps1`. Si aparece algún error de compilación, corregirlo es local (XAML/firmas).
2. **Recorrido real en Revit 2026** → `GUIA.md §12`. Marcar la columna R de `MATRIZ.md`.
3. **Builds 2024 y 2025** → `.\build.ps1 -Years 2024,2025` con esas versiones instaladas o `-RevitApiRoot`.
4. **Instalador** → ejecutar `Install.cmd` y comprobar el manifiesto en `%AppData%\Autodesk\Revit\Addins\<año>`.
5. **`Document.CreationGUID` en 2024** → se lee por reflexión; si no existe en 2024, la importación pide confirmación de identidad (comportamiento seguro, documentado).
6. **`-UseRevitNuGet`** (paquetes Nice3point de referencia) → opcional, no verificado aquí porque nuget.org no era accesible.

## Decisiones tomadas

- Lector/escritor .xlsx propio en lugar de ClosedXML (ver `ARQUITECTURA.md`).
- Parámetros de referencia (ElementId) de solo lectura en 0.1.0 (se muestran por nombre).
- Parámetros de familia no compartidos identificados por spec + nombre + ámbito (única identidad que expone Revit entre familias); duplicados en un elemento → bloqueados.
- Schedules: modo datos = una fila por elemento; campos calculados/relacionados excluidos; key/embebidos/filtrados por sheet solo en presentación; panel schedules fuera de alcance.
- Vínculos: la vista activa del host no filtra los vínculos (informado).
- Ventana de revisión no modal (los ExternalEvent necesitan el bucle de mensajes de Revit).

## Próximo paso sugerido

1. `.\build.ps1 -Years 2026` en una máquina con Revit 2026 → corregir lo que el compilador de WPF/Revit señale.
2. Prueba guiada (`GUIA.md §12`) en una copia de un modelo con Lighting Fixtures y Electrical Equipment.
3. Medir 10 000 elementos × 20 parámetros y anotarlo arriba.
4. Builds 2024/2025.
5. Siguientes funciones: edición de referencias (material/nivel) con selector por Id; panel schedules; creación de Rooms/Spaces.
