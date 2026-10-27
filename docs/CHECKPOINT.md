# Checkpoint – estado para continuar

Fecha: 2026-10-27 · Versión 0.2.0 · Repositorio https://github.com/oxidodefosforo/causafix (rama `main`)

## Módulos

| Módulo | Botón | Proyectos |
|---|---|---|
| Excel Link | Data → Excel Link | `CausaFix.Core`, `CausaFix.ExcelLink`, `CausaFix.Revit`, `CausaFix.Addin/Modules/ExcelLink` |
| Project Setup | Documentation → Project Setup | `CausaFix.ProjectSetup`, `CausaFix.Revit/ProjectSetup`, `CausaFix.Addin/Modules/ProjectSetup` |

Un solo paquete e instalación por año (2024/2025/2026). Los ayudantes MVVM están en `CausaFix.Core/Mvvm` y los comparten todos los módulos.

## Excel Link: causa del bloqueo y corrección (commit 7766110)

Síntoma: revisión con cero cambios y filas *Blocked* con Original/Proposed "(empty)" y Current "–".

Causas confirmadas en el código (no ejecutado en Revit):
1. `ParamAccess.LockReason` y `ModelReader.ColumnLock` bloqueaban cuando `Parameter.UserModifiable` era false. Esa bandera describe la edición en la interfaz de Revit, no la API; muchos parámetros compartidos la tienen en false y se pueden escribir. Ahora solo bloquean `IsReadOnly`, grupos, worksharing y vínculos; los casos que CausaFixIt no soporta se marcan **Unsupported**, no como read-only.
2. La editabilidad de columna se decidía con una muestra de elementos; una columna entera quedaba marcada read-only en los metadatos del libro.
3. `WorkbookImporter` trataba esas columnas como read-only: generaba `Proposal.Invalid` con `RawValue.Empty` como original **antes** de consultar Revit, de ahí "(empty)" y "–". Ahora el importador nunca bloquea por metadatos; el estado real se lee en Revit por celda.

Además: esquema 2 con migración de libros de esquema 1, referencias ElementId editables con lista "Nombre [Id]", una escritura por destino de tipo, `TypeParameterConflict` y error de grupo sin escrituras parciales, "Multiple conflicting values" en Proposed, estados precisos con acción y diagnóstico por fila + CSV.

## Project Setup: ampliación (nombres, numeración, Views on Sheet, títulos)

Sobre el commit de referencia 65584df.
- `Naming/NameRule.cs` y `NameEngine.cs`: constructor de nombres con parámetros reales, separadores opcionales y ciclos.
- `Model/SheetModel.cs`: perfiles de numeración, composiciones y `TitlePlacement`.
- `Model/SetupMigration.cs`: formato 2 y migración.
- `Planning/PlanBuilder.cs`: identidad de sheet por composición o fila, secuencia por sheet, overlays y títulos.
- `Geometry/TitleGeometry.cs`: `TitleRunner`.
- `CausaFix.Revit/ProjectSetup`:
  - `ParamReader` y lectura de parámetros, valores y viewports;
  - paso de títulos en `SetupExecutor` con `LabelOffset`, `LabelLineLength` y `GetLabelOutline`;
  - verificación de overlay y `PickReferenceTitle`.
- Interfaz:
  - `NameBuilderControl` y paso **Numbering**;
  - pestañas **Sheet rules / Sheet Name / Views on Sheet**;
  - **View Title Placement**;
  - Review agrupada por sheet.

## Project Setup: selección de View Sets y referencia de título (commit 041f2ed y siguiente)

- **Causa de la selección (confirmada con WPF real en CI):** la plantilla de fila era un `CheckBox` cuyo contenido era el texto, estirado a todo el ancho. `ButtonBase` maneja `MouseLeftButtonDown`, así que un clic en el nombre, la familia o el espacio libre activaba o desactivaba la regla y `ListBoxItem` nunca la seleccionaba. Corrección: casilla separada del texto (View Sets y composiciones). El foco en la casilla selecciona su fila, de forma diferida para que la edición pendiente se confirme en la regla anterior.
- **Referencia de título:** *Title on Sheet → Take title position from a placed view…* con alcance (perfil/grupo/composición). Usa la misma `TitlePlacement` y el mismo `TitleRunner`. Por defecto la captura es **FixedPosition** si no hay elección previa (`CaptureKindExplicit`, captura anterior o modo Relative). Mensajes concretos con reintento; si se cancela, no cambia nada.
- `tests/CausaFix.Ui.Tests`: abre la ventana real y hace clic con `SendInput`. Primero reproduce el fallo con la plantilla antigua y después comprueba la nueva (texto, familia, espacio libre, casilla una vez, regla desactivada, edición pendiente, flechas, Refresh). Informe: artefacto `ui-check-report`.

## Excel Link: parámetros compartidos (DR_Tag-1 / EL_Circuit) y marca CausaFixIt (versión 0.2.0)

Base: commit 0c30203 (run 40151407427), que es el build que se estaba probando. Para comprobar qué DLL carga Revit, el log escribe `Excel Link: command start (CausaFixIt <versión> · commit <sha>, built …, loaded from <ruta>)`. Lo mismo aparece en About, `BUILD.txt` y los informes.

- **Causa confirmada en el código:**
  - `ScheduleReader.PlanData` asignaba ámbito de instancia a todos los campos `ScheduleFieldType.Instance`. La referencia de la API indica que *todos los compartidos usan ese tipo, sean de instancia o de tipo*, así que un compartido de tipo de una tabla se exportaba como columna de instancia y se resolvía y escribía en la instancia.
  - Además, ningún paso comprobaba el propietario real del `Parameter`.
- **Corrección:** `ParamOwner` establece el propietario a partir de `Parameter.Element`, de si el tipo resuelve la misma identidad y del binding del proyecto.
  - Lo usan la exportación (tablas y categorías), la lectura de filas, la revisión (`WrongScope` o la corrección explícita de libros antiguos) y Apply (`WrongTarget`).
- **Verificación por etapas:** `WriteVerifier` en Core, más una comprobación nueva tras el commit, antes de `Assimilate`. Devuelve los fallos `NotPersisted` y `SetFailed` con los valores leídos.
- **Diagnóstico ampliado:** en revisión, resultado e informe.
- **No confirmado (requiere tu modelo):**
  - Qué devuelve exactamente Revit para DR_Tag-1 en la instancia (proxy de solo lectura o null).
  - Si EL_Circuit es de instancia o de tipo.
  - Por qué EL_Circuit volvía vacío. Puede ser el mismo error de propietario, un actualizador de otro add-in o una regla de la familia. La nueva instrumentación lo dirá en la etapa en que falle.
- **Marca:**
  - Nombre visible CausaFixIt (pestaña, panel, ventanas, diálogos, Deshacer, selecciones, informes, paquete, instalador, documentación).
  - El logo aprobado está en `src/CausaFix.Addin/Assets` (16–256 px, transparente; solo se limpiaron 918 píxeles sueltos del archivo) y el original en `docs/brand`. Se usa en el botón About, los iconos y encabezados de ventana.
  - Se mantienen: `AddInId`, `VendorId`, `CausaFix.addin`, nombres de DLL y namespaces, `SchemaGuid` y esquema de Project Setup, marcadores del libro (`CF_*`) y rutas de AppData.

## Corrección: "Modification of the document is forbidden" en Match reference title

- Causa: `ProjectSetupService.PickReferenceTitle` medía con `RevitTitleProbe`, cuyos `ViewportBox()`/`LabelBox()` llaman a `Document.Regenerate()`. La captura corre en un ExternalEvent sin transacción abierta, y ExternalEvent no abre ninguna.
- Corrección: `ViewportTitleReader` (en `SheetGeometry.cs`) lee `GetBoxOutline`, `GetLabelOutline`, `LabelOffset` y `LabelLineLength` sin regenerar ni modificar el modelo, y es lo que usa la captura. `RevitTitleProbe` conserva sus `Regenerate()` para el paso de títulos de Run Setup / Update Titles Only, que ocurre dentro de su `Transaction`. Ahora exige `Document.IsModifiable` y da un error explícito si se usa fuera de una transacción.

## Verificado aquí

| Verificación | Resultado |
|---|---|
| Pruebas automáticas (reglas + flujos con mocks) en .NET 8 y .NET Framework 4.8 | 112/112 (7 nuevas: selección de reglas y referencia de título) |
| Interacción WPF real (ventana Project Setup, ratón/teclado reales, Windows CI) | Verde: lista View Sets |
| GitHub Actions (Windows): pruebas + compilación 2024/2025/2026 de todo, incluida la WPF, contra las referencias reales de la API | Verde |
| Libro .xlsx abierto y re-guardado por LibreOffice → reimportación con 0 cambios | OK |
| Plan de Project Setup 10 niveles × 4 conjuntos (2000 vistas y 500 sheets existentes) | ~10 ms (núcleo, sin Revit) |

## No verificado (requiere Revit)

1. Excel Link: un parámetro de instancia y uno de tipo cambiados desde Excel y leídos de nuevo en Revit (`GUIA.md §12`).
2. Project Setup: niveles → vistas/templates/nombres → sheets → colocación con error medido ≤ 0,1 mm, reejecución sin duplicados, rollback.
3. Project Setup (ampliación, `PROJECT_SETUP.md §9`): valores reales de parámetros compartidos/proyecto; superposición planta + RCP medida; títulos con `LabelOffset` (incluidos viewports rotados y title block desplazado) y Update Titles Only; migración de un perfil antiguo sobre un modelo con sheets compartidos.
4. Tiempos reales en Revit (10 000 × 20 en Excel Link; 10 × 4 en Project Setup).
5. Instaladores en Windows (Install / Install-AllUsers).
6. Referencia de título en Revit: captura de un viewport real, varios niveles con crops distintos alineados en papel (≤ tolerancia), Planta + RCP con título oculto, Update Titles Only (`PROJECT_SETUP.md §9`, punto 4).

## Decisiones

- Project Setup ejecuta todo en **un callback** de ExternalEvent con un TransactionGroup: no hay transacciones entre callbacks; por eso no se puede cancelar a mitad (se cancela antes de empezar o durante los picks).
- Alineación con un bucle común (`AlignmentRunner` + `IViewportProbe`) probado con sondas simuladas y ejecutado en Revit con `RevitViewportProbe`.
- Split crops bloqueados; recortes no rectangulares admitidos (una transformación).
- Asociaciones fila ↔ vista/sheet con Extensible Storage (esquema `CausaFixProjectSetup`, GUID fijo en `SetupStorage.cs`).
- Las anclas y elecciones de elementos no viajan entre documentos en los perfiles.
- La unidad de numeración es el sheet. Su identidad (`cmp|<composición>|<nivel>`, la clave de fila o `existing|<uid>`) es independiente del número visible. Las claves `sheet|NÚMERO` del formato 1 solo se usan para reconocer los sheets antiguos.
- Un número existente no cambia sin aprobación (`ApprovedSheetNumbers`) o sin edición en Review.
- Los títulos ocultos se resuelven con un tipo de viewport elegido por el usuario. No se crean tipos derivados, porque los valores de *Show Title* no se pueden verificar en la API documentada y la lectura usa `VIEWPORT_ATTR_SHOW_LABEL` solo si el enum existe en esa versión.
- El paso de títulos falla y revierte si mover el título moviera el dibujo. Un título fuera de tolerancia se restaura y se informa, sin revertir el resto.

## Próximo paso

1. Descargar el paquete de la última ejecución verde de Actions e instalar.
2. Prueba guiada de Excel Link y de Project Setup en copias de modelos; anotar resultados en la columna R de `MATRIZ.md`.
3. Pendientes: escribir parámetros adicionales de sheet/title block, copiar el recorte de una vista de referencia y crear un tipo de viewport derivado sin título (§8 de `PROJECT_SETUP.md`).
