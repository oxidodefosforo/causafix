# CausaFixIt · Excel Link — Guía

Versión 0.1.0 · Revit 2024, 2025 y 2026 · Windows x64

Excel Link es la primera herramienta de la suite CausaFixIt (la segunda, Project Setup, está en `PROJECT_SETUP.md`). Permite elegir categorías, elementos, schedules o elementos espaciales; elegir parámetros; revisar o editar los datos en una tabla; exportarlos a un **.xlsx local**; editarlos en Excel; e importarlos con una **revisión obligatoria** antes de escribir en el modelo.

Alcance de esta versión: archivos .xlsx locales y edición en tabla. Google Sheets/Morta, la creación de Rooms/Spaces y otras herramientas de la suite quedan para versiones posteriores. No es una réplica completa de SheetLink.

---

## 1. Requisitos

**Usuario final (solo usar el add-in)**
- Windows x64 y Revit 2024, 2025 o 2026.
- Nada más: no hace falta Excel instalado (CausaFixIt escribe y lee .xlsx sin Office), ni .NET SDK, ni herramientas de desarrollo. Revit 2024 ya incluye .NET Framework 4.8 y Revit 2025/2026 incluyen el runtime de .NET 8.

**Para compilar**
- .NET 8 SDK.
- .NET Framework 4.8 Developer Pack (solo para el build de Revit 2024).
- RevitAPI.dll y RevitAPIUI.dll de cada año: un Revit instalado en `C:\Program Files\Autodesk\Revit <año>`, o una carpeta con subcarpetas `2024\`, `2025\`, `2026\` (parámetro `-RevitApiRoot`).

## 2. Compilar

**Opción A – GitHub (recomendada, sin instalar nada):** ver `docs/GITHUB.md`. GitHub compila en Windows y te da el zip instalable.

**Opción B – en tu PC.** En PowerShell, en la carpeta de la solución:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1                 # 2024, 2025 y 2026 (los que encuentre)
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Years 2026     # solo 2026
powershell -ExecutionPolicy Bypass -File .\build.ps1 -RevitApiRoot D:\RevitApi
```

El script ejecuta primero las pruebas automáticas; si fallan, no genera el paquete. Los años sin RevitAPI.dll se omiten y se informan. Resultado: `dist\CausaFixIt-0.2.0\` y `dist\CausaFixIt-0.2.0.zip` con una carpeta por año, el manifiesto y los scripts de instalación. Las DLL de Autodesk nunca se copian al paquete.

Compilación manual de un año:

```powershell
dotnet build src\CausaFix.Addin\CausaFix.Addin.csproj -c Release -p:RevitVersion=2026
```

Solo pruebas: `dotnet run --project tests\CausaFix.Core.Tests -c Release -f net8.0`

## 3. Instalar y desinstalar

Cierra Revit y, en la carpeta del paquete (`dist\CausaFixIt-0.2.0\` o el zip descargado de GitHub y descomprimido):

| Quiero instalar… | Archivo | Permisos | Binarios | Manifiesto |
|---|---|---|---|---|
| Solo para mí | **Install.cmd** | Ninguno | `%AppData%\CausaFix\bin\<año>\` | `%AppData%\Autodesk\Revit\Addins\<año>\CausaFix.addin` |
| Para todos los usuarios del PC | **Install-AllUsers.cmd** | Administrador (lo pide) | `C:\Program Files\CausaFix\<año>\` | `C:\ProgramData\Autodesk\Revit\Addins\<año>\CausaFix.addin` |

Para desinstalar: **Uninstall.cmd** (solo para mí) o **Uninstall-AllUsers.cmd** (todos). Se conservan perfiles y configuración, salvo que uses `Uninstall.ps1 -RemoveUserData`.

Reglas:
- Se instala cada año presente en el paquete (2024, 2025, 2026). Para uno solo: `Install.ps1 -Years 2026` (o `-Scope Machine -Years 2026`).
- No se tocan otros add-ins ni sus manifiestos. Volver a ejecutar actualiza la instalación.
- **Nunca dos veces:** si instalas en un ámbito, el script borra tu manifiesto del otro ámbito. Si en el PC otro usuario lo había instalado solo para él y luego se instala para todos, ese usuario debe ejecutar `Uninstall.cmd` una vez (Revit avisaría de un add-in duplicado).
- Para actualizar a una versión nueva: cierra Revit y ejecuta el mismo instalador del paquete nuevo.
- **De CausaFix a CausaFixIt:** el cambio de nombre es solo visible. El manifiesto (`CausaFix.addin`), el `AddInId`, el `VendorId`, las DLL y las carpetas no cambian, así que `Install.cmd` actualiza la instalación anterior en su sitio: aparece **una sola** pestaña *CausaFixIt* y se conservan perfiles (incluida la configuración AZ), ajustes, informes y logs. Los libros exportados con CausaFix y las asociaciones de Project Setup (esquema `CausaFixProjectSetup`) se siguen leyendo. No hay migración de carpetas porque las rutas de datos no cambian.

Carpetas de usuario:
| Qué | Dónde |
|---|---|
| Perfiles | `%AppData%\CausaFix\ExcelLink\Profiles` y `%AppData%\CausaFix\ProjectSetup\Profiles` |
| Configuración | `%AppData%\CausaFix\settings.json` |
| Informes de cambios | `%LocalAppData%\CausaFix\Reports` |
| Logs | `%LocalAppData%\CausaFix\Logs` |

## 4. La ventana de Excel Link

Es una ventana no modal: puedes seguir trabajando en Revit. Está ligada al modelo activo al abrirla; si cambias de modelo, las acciones lo detectan y te piden **Refresh**. Si cierras el modelo, la ventana se cierra.

Pestañas: **Model Categories, Annotation Categories, Elements, Schedules, Spatial, Preview / Edit.**

### Alcance (Whole Model / Active View / Current Selection)
- **Whole Model:** todos los elementos de las categorías marcadas en el modelo anfitrión.
- **Active View:** los elementos que devuelve la vista gráfica activa (respeta su visibilidad y recorte). No se permite en schedules ni sheets: para eso está la pestaña Schedules. Los vínculos no se filtran por la vista.
- **Current Selection:** lo seleccionado en Revit al pulsar Refresh.

### Tres paneles
1. **Categorías** — búsqueda, marcar todo, ocultar no marcadas, número de elementos.
2. **Parámetros disponibles** — filtros Instance / Type / Read-only, "Common only" (solo los que tienen todos los elementos; si no, la unión), búsqueda.
3. **Parámetros seleccionados** — el orden de las columnas del Excel. ▶ añade, ◀ quita, doble clic también; ⤒ ▲ ▼ ⤓ ordenan.

Colores, siempre acompañados de una etiqueta de texto:
| Color | Etiqueta | Significado |
|---|---|---|
| Verde suave | `I` | Parámetro de instancia editable |
| Amarillo suave | `T` | Parámetro de tipo editable: cambia **todas** las instancias del tipo |
| Rojo suave | `RO` | Solo lectura (calculado, fórmula, restricción, referencia, no modificable) |
| Rojo + borde ámbar | `T · RO` | Tipo y solo lectura |
| Gris | `N/A` | El parámetro no existe en ese elemento (no es vacío ni cero) |

`RO*` = editable en unos elementos y bloqueado en otros (p. ej. por worksharing o grupos); se resuelve celda por celda.

Opciones: **Include linked files** (hojas de solo lectura por vínculo cargado) y **One row per type** (exporta tipos; solo parámetros de tipo).

Pestaña **Elements**: además de categorías, lista de familias y tipos (ninguno marcado = todos) y un filtro de texto (familia, tipo, Mark, nombre).

## 5. Exportar a Excel

1. Elige categorías y parámetros.
2. **Export…** → elige el archivo. CausaFixIt lee el modelo por etapas (con progreso y **Cancel**) y escribe el libro primero en un archivo temporal; solo al terminar reemplaza el destino. Si el archivo está abierto en Excel, te lo indica.

El libro contiene:
- **Instructions** — leyenda, reglas de edición y unidades por columna.
- Una hoja por categoría / schedule. Fila 1 (oculta) y columna A (oculta) llevan las claves técnicas; la fila 2 son encabezados; los datos empiezan en la fila 3. Encabezados congelados y filtros activos.
- Hojas técnicas ocultas con los valores originales y los metadatos (versión de esquema, identidad del modelo, unidades).

Puedes ordenar filas, filtrar, mover columnas y **renombrar encabezados**: el mapeo usa las claves ocultas, no la posición ni el texto visible.

Unidades: cada número está en la unidad indicada en el encabezado (`Width [mm]`, `Voltage [V]`, `Apparent Load [VA]`), que es la unidad del proyecto en el momento de exportar y queda registrada en el archivo. Al importar se interpreta en esa unidad aunque el proyecto haya cambiado sus unidades después.

## 6. Editar en Excel — reglas

- **Celda vacía = sin cambios.** Para borrar un valor escribe `<<CLEAR>>`.
- **Sí/No:** `Yes` o `No` (hay lista desplegable).
- **Números:** escribe números normales. Si una celda está como texto y contiene `1,234` o `1.234`, CausaFixIt no adivina: lo marca como ambiguo. En **Settings** puedes fijar si la coma o el punto es el decimal para el texto.
- **Fórmulas:** se admite el resultado guardado por Excel (se muestra en la revisión). Una fórmula sin resultado guardado se bloquea: recalcula y guarda, o pega valores.
- **Filas borradas** no borran elementos; **filas nuevas** no crean elementos; **columnas nuevas** no crean parámetros. Todo se ignora y se informa.
- No borres la fila 1 ni la columna A ocultas, ni copies claves de una fila a otra. Si una clave se pierde, esa parte se bloquea y el mensaje explica cómo corregirlo (volver a exportar).
- Celdas rojas y de vínculos: los cambios se detectan y se informan, nunca se escriben.

## 7. Importar — siempre con revisión

**Import…** sigue este flujo obligatorio:

1. Abre el archivo y valida que es un libro CausaFixIt y su versión de esquema.
2. **Verifica el modelo**: si el libro es de otro modelo, se bloquea. Si es del mismo linaje pero otra ubicación (Save As, copia, detached) o la identidad no se puede probar, te pide confirmación explícita. Nunca empareja por ElementId; usa UniqueId.
3. Resuelve elementos y parámetros por sus claves.
4. Compara **tres estados** por celda: valor exportado (original), valor de Excel (propuesto) y valor actual en Revit.
5. Muestra la **revisión**.
6. Solo al pulsar **Apply to Revit…** y confirmar, escribe.
7. Muestra el resultado y guarda un informe CSV.

En la revisión:
| Estado | Qué significa | ¿Se escribe? |
|---|---|---|
| Change | Cambiado en Excel; Revit sigue con el original | Sí (puedes excluirlo) |
| Conflict (changed in Revit) | Revit cambió ese dato después de exportar, distinto de tu propuesta | No, salvo que lo incluyas |
| Conflicting proposals | Varias filas/hojas proponen valores distintos para el mismo destino (típico en parámetros de tipo) | No, hasta que elijas uno |
| Already in model | Revit ya tiene el valor propuesto | No hace falta |
| Blocked | Solo lectura, valor inválido, elemento borrado, parámetro ausente, vínculo… | Nunca |

Si una celda no cambió en Excel, se conserva el valor actual de Revit aunque alguien lo haya cambiado después. Para parámetros de tipo se indica cuántas instancias del **modelo completo** se verán afectadas (también las que no estaban en la exportación).

**Modo de aplicación**
- **All or nothing** (por defecto): si falla un cambio aprobado, se revierte toda la operación.
- **Apply valid changes only**: aplica los válidos y reporta los omitidos.

Antes de escribir, cada cambio se vuelve a validar (elemento existe, editable, y valor actual igual al que viste en la revisión). Toda la operación queda como **un solo Deshacer** en Revit. CausaFixIt **no guarda ni sincroniza** el modelo. Los avisos de Revit no se suprimen; los errores revierten la transacción.

El informe CSV (`%LocalAppData%\CausaFix\Reports`) lista cada cambio con valor anterior/nuevo y resultado. Si hubo rollback, los cambios aparecen como `RolledBack`, nunca como aplicados.

**Propietario real y verificación por etapas**
- Antes de escribir, CausaFixIt comprueba qué elemento **posee** el parámetro: `Parameter.Element`, si el tipo expone la misma identidad (GUID, built-in o parámetro de proyecto) y el *binding* del proyecto (InstanceBinding/TypeBinding). Ser compartido no decide nada: un compartido puede ser de instancia o de tipo.
- Si el destino aprobado no es el propietario, el cambio se bloquea como **Wrong destination** (`WrongScope`). El ámbito nunca se cambia durante Apply.
- **Libros antiguos con el ámbito equivocado:** las tablas de planificación de Revit marcan todos los compartidos como campos de instancia, también los de tipo. Las versiones anteriores exportaban así esos parámetros de tipo como columnas de instancia. Al importar uno de esos libros, la revisión los muestra como **(T) · scope corrected**, con un aviso, y los compara y escribe sobre el tipo con sus reglas: una escritura por tipo y conflicto si dos filas del mismo tipo proponen valores distintos. Se reconocen por ese aviso y por la nota *Scope corrected* del diagnóstico. Lo recomendable es reexportar la tabla: el libro nuevo trae la columna como (T) y en amarillo. Si quieres conservar las propuestas, copia la columna editada del libro antiguo al nuevo; las filas se emparejan por la clave oculta, no por la posición.
- **Escritura:** se lee el valor, se llama a `Set` con la sobrecarga de su StorageType (texto: `Set(string)`/`AsString`) y se registran el retorno y cualquier excepción. Luego se lee de nuevo. Si no coincide, se regenera dentro de la transacción y se vuelve a obtener el elemento y el parámetro. Tras el commit se comprueba otra vez con una consulta nueva, **antes** de cerrar el grupo. Un valor que no quedó en el modelo es **NotPersisted**, nunca *Applied*. En *All or nothing* se revierte todo.
- **Tipos de fallo** en el resultado: ReadOnly, Locked, AmbiguousIdentity, WrongTarget, Missing, ChangedSinceReview, InvalidValue, SetFailed, NotPersisted, RolledBack. Al seleccionar una fila del resultado se ve el mensaje completo y la evidencia de cada etapa. Incluye documento, elemento y propietario, GUID, ámbito, StorageType, IsShared, IsReadOnly, UserModifiable, HasValue, transacción y los valores antes de Set, después de Set, tras regenerar y tras el commit. Lo mismo va en el informe (columna *Diagnostic (stages)*), junto con la versión de Revit, el build y commit del add-in y el estado de la transacción y del grupo.
- **Solo lectura:** si Revit dice `IsReadOnly = true` en el propietario real, se mantiene el bloqueo y se explica lo comprobado: la asociación a un parámetro global, si la hay, con su nombre. La API del proyecto no expone las fórmulas de la familia: si tampoco se puede editar en Propiedades, hay que revisarlo en la familia. CausaFixIt no fuerza nada, no quita fórmulas ni asociaciones y no edita familias.

## 8. Preview / Edit

**Preview / Edit** carga la selección en una tabla editable sin exportar: búsqueda, filtros (todas / editadas / inválidas), ordenación por columna, copiar/pegar rangos con Excel (Ctrl+C / Ctrl+V), **Set value…** para varias celdas, **Clear value**, **Revert**. Una celda vacía no borra: usa Clear value.

Editar solo prepara cambios. **Apply Changes…** usa el mismo motor de validación, comparación y aplicación que la importación. Los filtros nunca descartan cambios pendientes. Exportar desde Preview exporta los valores leídos de Revit (no los borradores) y te avisa.

Selección en Revit: **Select in Revit**, **Zoom to**, **Isolate (temporary)** (Ocultar/Aislar temporal en la vista activa, nunca permanente) y **Reset isolate**.

## 9. Schedules

- **Data exchange (re-importable):** una fila por elemento que lista la tabla, columnas ligadas a parámetros reales. En tablas agrupadas o no itemizadas, las filas se desglosan por elemento; los totales no se exportan en este modo.
- **Presentation (solo lectura):** la tabla como la muestra Revit (encabezados, agrupaciones, totales) como texto. No se puede importar.

Matriz de soporte:
| Tipo de tabla | Data exchange | Presentation |
|---|---|---|
| Schedule normal (itemizado o agrupado), sheet list, view list, note block | Sí | Sí |
| Material takeoff | Sí, campos de elemento; campos de material/cantidad excluidos | Sí |
| Key schedule | No | Sí |
| Con schedule embebido | No | Sí |
| Filtrado por sheet | No | Sí |
| Keynote legend | No | Sí |
| Revision schedule de titleblock | No se lista | No |
| Panel schedule eléctrico (PanelScheduleView) | No (otra clase; fuera de esta versión) | No |

**Ámbito de los campos:** Revit informa todos los parámetros compartidos de una tabla como campos de instancia (`ScheduleFieldType.Instance`), aunque sean de tipo. CausaFixIt lee el ámbito real en los elementos de la tabla: si el parámetro lo tiene el tipo, la columna sale como (T), en amarillo, y se escribe en el tipo. Si unas familias lo tienen de instancia y otras de tipo, salen dos columnas ("(instance)" y "(type)"). Las notas de la hoja indican qué campos se corrigieron.

Campos que no se exportan en modo datos: calculados, combinados, conteo/porcentaje y de elementos relacionados (Room, From/To Room, Space, Material, Project Info, vínculos). Se listan en las notas de la hoja.

**Check schedules on active sheet / active schedule** marca las tablas colocadas en la sheet activa o la tabla activa.

## 10. Spatial, vínculos, Project Information e inventarios

- **Spatial:** Rooms, Spaces y Areas con su estado (Placed / Not placed / Not enclosed or redundant). Se actualizan los existentes; no se crean ni colocan nuevos.
- **Vínculos:** hojas de solo lectura con documento e instancia del vínculo. Los vínculos descargados se omiten y se informan. Los parámetros de proyecto no compartidos aparecen como N/A dentro de los vínculos.
- **More ▾ → Project Information:** se abre en Preview/Edit y sus parámetros editables pueden aplicarse o exportarse/reimportarse.
- **More ▾ → inventario de familias, Object Styles, Line Styles:** libros de solo lectura.

## 11. Perfiles

Guardan pestaña, categorías, parámetros (por clave técnica), orden, filtros y opciones. Botones: Load, Save, More ▾ (Save as, Duplicate, Rename, Delete, Import, Export). Al usar un perfil en otro proyecto, las referencias que no existen se informan y se mantienen como "missing": **nunca se asocian por nombre**.

## 12. Prueba guiada en Revit 2026 (validación manual)

Usa un modelo de prueba (copia), con Lighting Fixtures y Electrical Equipment.

1. **Abrir:** pestaña CausaFixIt → Excel Link. La ventana muestra el modelo y la versión.
2. **Seleccionar:** Model Categories → marca Lighting Fixtures y Electrical Equipment. Añade Comments, Mark (instancia), Type Mark (tipo), tus parámetros compartidos de QA y, si existen, parámetros eléctricos editables en V y VA. Comprueba que los calculados por Revit (p. ej. cargas aparentes) salen en rojo `RO`.
3. **Exportar** a `prueba.xlsx` y ábrelo en Excel: colores, encabezados con unidades, `N/A` donde no aplica.
4. **Reimportar sin editar** → debe decir "No differences" (**cero cambios**).
5. **Editar en Excel:** ordena las filas, renombra el encabezado "Mark", cambia 2 Comments, un Mark, un Type Mark en dos filas del mismo tipo con **valores distintos**, un valor en V, escribe `1,234` como texto en un número, `<<CLEAR>>` en un Comments, deja vacía otra celda, cambia una celda roja, duplica una fila, añade una fila y una columna nuevas, y una fórmula `=A1*2` sin recalcular (guárdala desde otra herramienta o borra el resultado) si quieres probar ese caso.
6. **Mientras tanto, en Revit**, cambia a mano el Mark de uno de los elementos que editaste.
7. **Importar:** comprueba en la revisión: changes correctos sobre el elemento correcto; conflicto de tipo (elige un valor); conflicto por el Mark cambiado en Revit (no incluido por defecto); `1,234` bloqueado por ambiguo; celda roja bloqueada; vacío ignorado; `<<CLEAR>>` como borrado; duplicado, fila y columna nuevas en "Messages"; cuántas instancias afecta el Type Mark.
8. **Aplicar** (All or nothing). Verifica valores en Revit, un solo Deshacer y el informe CSV.
9. **Rollback:** repite con un elemento bloqueado por otro usuario (worksharing) o dentro de un grupo, en modo All or nothing: no debe aplicarse nada y el informe debe decir `RolledBack`.
10. **Otro proyecto:** importa el libro en otro modelo: debe bloquearse.
11. **Schedule agrupada:** exporta en Data exchange (una fila por elemento, sin totales) y en Presentation (totales visibles, no importable).
12. **Vínculos:** Include linked files → hojas de solo lectura; editar en ellas → bloqueado.
13. **Preview/Edit:** edita, pega un rango desde Excel, revierte una celda, aplica.

Anota los resultados en `docs/MATRIZ.md` (columna "Verificado en Revit").

### 12.1. Parámetros compartidos (DR_Tag-1, EL_Circuit u otros de tu plantilla)

Hazla en una copia del modelo, con la Electrical Fixture Schedule.

0. **DLL cargada:** abre Excel Link y busca en el log (`%LocalAppData%\CausaFix\Logs`) la línea `Excel Link: command start (CausaFixIt 0.2.0 · commit …, built …, loaded from …)`. Debe coincidir con `BUILD.txt` del paquete y con CausaFixIt → About.
1. **Exportar** la tabla en *Data exchange*. En las notas de la hoja aparece *Scope read from the elements…* si algún compartido de tipo se corrigió. Ese parámetro debe salir como `(T)` y en amarillo.
2. **Importar** el libro **antiguo** (el de la captura) sin cambiarlo. DR_Tag-1 debe salir como `(T) · scope corrected` con el aviso *Re-export*, no como *Read-only parameter*. Si sigue saliendo *Read-only parameter*, abre **Open diagnostic**: la fila indica `owner=…`, `IsReadOnly=…`, `binding=…` y *checked: …* (asociación a parámetro global o «revisar la familia»).
3. **EL_Circuit = b2** sobre un valor vacío y sobre uno existente: aplicar en *All or nothing*. El resultado debe ser *Applied*, o un fallo con su etapa, por ejemplo *Write not persisted: expected 'b2'; read (empty) after the transaction committed*. Selecciona la fila del resultado para ver el texto completo, y mándame **Open report** si no queda escrito.
4. **Tipo:** cambia DR_Tag-1 en una sola fila → se escribe una vez en el tipo, y todas sus instancias muestran el valor (incluso las no exportadas). Con dos filas del mismo tipo y valores distintos debe salir *Type conflict* y no escribirse nada.
5. **Reexportar** después de aplicar: los valores deben aparecer en las columnas correctas.

## 13. Problemas frecuentes

| Síntoma | Solución |
|---|---|
| Dos pestañas (CausaFix y CausaFixIt) | Hay otro manifiesto que carga `CausaFix.Addin.dll` (el instalador lo avisa). Ejecuta `Uninstall.cmd` del otro ámbito o borra ese manifiesto. |
| No aparece la pestaña CausaFixIt | Revisa que exista `%AppData%\Autodesk\Revit\Addins\<año>\CausaFix.addin` y el log en `%LocalAppData%\CausaFix\Logs`. |
| "The file is open in Excel" | Cierra el archivo y exporta de nuevo. |
| "The active document is now…" | Vuelve al modelo original o pulsa Refresh para trabajar con el activo. |
| Importación bloqueada por modelo distinto | El libro es de otro modelo. Exporta uno nuevo desde este modelo. |
| Número "ambiguo" | Escribe el número sin separador de miles o fija el separador decimal en Settings. |
| Cambio "Blocked – Owned by …" | Pide al usuario que libere el elemento (worksharing). |

---

Cómo añadir una herramienta nueva a la suite: ver `docs/AGREGAR_MODULO.md`.
