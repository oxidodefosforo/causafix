# CausaFix · Excel Link — Guía

Versión 0.1.0 · Revit 2024, 2025 y 2026 · Windows x64

Excel Link es la primera herramienta de la suite CausaFix. Permite elegir categorías, elementos, schedules o elementos espaciales; elegir parámetros; revisar o editar los datos en una tabla; exportarlos a un **.xlsx local**; editarlos en Excel; e importarlos con una **revisión obligatoria** antes de escribir en el modelo.

Alcance de esta versión: archivos .xlsx locales y edición en tabla. Google Sheets/Morta, la creación de Rooms/Spaces y otras herramientas de la suite quedan para versiones posteriores. No es una réplica completa de SheetLink.

---

## 1. Requisitos

**Usuario final (solo usar el add-in)**
- Windows x64 y Revit 2024, 2025 o 2026.
- Nada más: no hace falta Excel instalado (CausaFix escribe y lee .xlsx sin Office), ni .NET SDK, ni herramientas de desarrollo. Revit 2024 ya incluye .NET Framework 4.8 y Revit 2025/2026 incluyen el runtime de .NET 8.

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

El script ejecuta primero las pruebas automáticas; si fallan, no genera el paquete. Los años sin RevitAPI.dll se omiten y se informan. Resultado: `dist\CausaFix-0.1.0\` y `dist\CausaFix-0.1.0.zip` con una carpeta por año, el manifiesto y los scripts de instalación. Las DLL de Autodesk nunca se copian al paquete.

Compilación manual de un año:

```powershell
dotnet build src\CausaFix.Addin\CausaFix.Addin.csproj -c Release -p:RevitVersion=2026
```

Solo pruebas: `dotnet run --project tests\CausaFix.Core.Tests -c Release -f net8.0`

## 3. Instalar y desinstalar

Cierra Revit y, en la carpeta del paquete (`dist\CausaFix-0.1.0\` o el zip descargado de GitHub y descomprimido):

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

Carpetas de usuario:
| Qué | Dónde |
|---|---|
| Perfiles | `%AppData%\CausaFix\ExcelLink\Profiles` |
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
2. **Export…** → elige el archivo. CausaFix lee el modelo por etapas (con progreso y **Cancel**) y escribe el libro primero en un archivo temporal; solo al terminar reemplaza el destino. Si el archivo está abierto en Excel, te lo indica.

El libro contiene:
- **Instructions** — leyenda, reglas de edición y unidades por columna.
- Una hoja por categoría / schedule. Fila 1 (oculta) y columna A (oculta) llevan las claves técnicas; la fila 2 son encabezados; los datos empiezan en la fila 3. Encabezados congelados y filtros activos.
- Hojas técnicas ocultas con los valores originales y los metadatos (versión de esquema, identidad del modelo, unidades).

Puedes ordenar filas, filtrar, mover columnas y **renombrar encabezados**: el mapeo usa las claves ocultas, no la posición ni el texto visible.

Unidades: cada número está en la unidad indicada en el encabezado (`Width [mm]`, `Voltage [V]`, `Apparent Load [VA]`), que es la unidad del proyecto en el momento de exportar y queda registrada en el archivo. Al importar se interpreta en esa unidad aunque el proyecto haya cambiado sus unidades después.

## 6. Editar en Excel — reglas

- **Celda vacía = sin cambios.** Para borrar un valor escribe `<<CLEAR>>`.
- **Sí/No:** `Yes` o `No` (hay lista desplegable).
- **Números:** escribe números normales. Si una celda está como texto y contiene `1,234` o `1.234`, CausaFix no adivina: lo marca como ambiguo. En **Settings** puedes fijar si la coma o el punto es el decimal para el texto.
- **Fórmulas:** se admite el resultado guardado por Excel (se muestra en la revisión). Una fórmula sin resultado guardado se bloquea: recalcula y guarda, o pega valores.
- **Filas borradas** no borran elementos; **filas nuevas** no crean elementos; **columnas nuevas** no crean parámetros. Todo se ignora y se informa.
- No borres la fila 1 ni la columna A ocultas, ni copies claves de una fila a otra. Si una clave se pierde, esa parte se bloquea y el mensaje explica cómo corregirlo (volver a exportar).
- Celdas rojas y de vínculos: los cambios se detectan y se informan, nunca se escriben.

## 7. Importar — siempre con revisión

**Import…** sigue este flujo obligatorio:

1. Abre el archivo y valida que es un libro CausaFix y su versión de esquema.
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

Antes de escribir, cada cambio se vuelve a validar (elemento existe, editable, y valor actual igual al que viste en la revisión). Toda la operación queda como **un solo Deshacer** en Revit. CausaFix **no guarda ni sincroniza** el modelo. Los avisos de Revit no se suprimen; los errores revierten la transacción.

El informe CSV (`%LocalAppData%\CausaFix\Reports`) lista cada cambio con valor anterior/nuevo y resultado. Si hubo rollback, los cambios aparecen como `RolledBack`, nunca como aplicados.

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

1. **Abrir:** pestaña CausaFix → Excel Link. La ventana muestra el modelo y la versión.
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

## 13. Problemas frecuentes

| Síntoma | Solución |
|---|---|
| No aparece la pestaña CausaFix | Revisa que exista `%AppData%\Autodesk\Revit\Addins\<año>\CausaFix.addin` y el log en `%LocalAppData%\CausaFix\Logs`. |
| "The file is open in Excel" | Cierra el archivo y exporta de nuevo. |
| "The active document is now…" | Vuelve al modelo original o pulsa Refresh para trabajar con el activo. |
| Importación bloqueada por modelo distinto | El libro es de otro modelo. Exporta uno nuevo desde este modelo. |
| Número "ambiguo" | Escribe el número sin separador de miles o fija el separador decimal en Settings. |
| Cambio "Blocked – Owned by …" | Pide al usuario que libere el elemento (worksharing). |

---

Cómo añadir una herramienta nueva a la suite: ver `docs/AGREGAR_MODULO.md`.
