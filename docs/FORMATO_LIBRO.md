# Formato del libro CausaFix (esquema 1)

| Hoja | Visible | Contenido |
|---|---|---|
| `Instructions` | Sí | Modelo, fecha, versiones, alcance, leyenda, reglas, unidades por columna, notas por hoja. |
| Una por tabla | Sí | Datos editables o de solo lectura. |
| `CF_O1…n` | veryHidden | Valores originales por fila y columna (para comparar en tres estados). |
| `CF_Meta` | veryHidden | Esquema, herramienta, versiones, fecha, alcance, identidad del documento, hojas y columnas. |

## Hoja de datos

- **Fila 1 (oculta):** `A1 = CFSHEET|<clave de hoja>`; desde B, `CFCOL|<ParamKey>` por columna.
- **Columna A (oculta):** clave de fila = UniqueId (host) o `L|<UniqueId de la instancia del vínculo>|<UniqueId>` (vínculo).
- **Fila 2:** encabezados visibles: `Nombre [unidad] (T) (RO)`.
- **Desde la fila 3:** datos. Encabezados congelados, filtro automático en la fila 2.
- Celdas: texto con formato `@` (Excel no convierte lo escrito en fórmula o número), números como números en la unidad de la columna, Yes/No con lista, `N/A` cuando el parámetro no existe.

Las claves gobiernan el mapeo: el nombre de la pestaña, el orden de filas, el orden de columnas y el texto de los encabezados pueden cambiar.

## Hoja `CF_O<n>`

Fila 1: `CFSHADOW|<clave>`, `elementId`, `uniqueId`, `typeUniqueId`, `docKey`, `linked`, y `CFCOL|<ParamKey>` por columna.
Filas siguientes: clave de fila, ElementId **como texto** (64 bits sin pérdida), UniqueId, UniqueId del tipo, documento, vínculo (1/0) y el valor original codificado:

| Código | Significado |
|---|---|
| `na` | El parámetro no existe en el elemento |
| `e` | Vacío |
| `s:<texto>` | Texto |
| `i:<entero>` | Entero / Yes-No |
| `d:<double R>` | Doble en **unidades internas** de Revit, ida y vuelta exacta |
| `id:<entero>` | ElementId (referencias) |
| `w:<texto>` | Columnas de solo lectura: texto escrito en la celda (para detectar ediciones) |

## Hoja `CF_Meta`

Pares clave/valor (`schema`, `tool`, `addinVersion`, `revitVersion`, `exportedUtc`, `scope`, `doc.*`) y filas:

- `sheet | clave | nombre de pestaña | hoja de originales | título | origen | modo de fila | solo lectura`
- `column | clave de hoja | ParamKey | encabezado | tipo de valor | spec | unidad (ForgeTypeId) | etiqueta de unidad | solo lectura | motivo | tipo de dato`

Un libro con `schema` mayor que el soportado se rechaza con un mensaje para actualizar CausaFix.

## Exportación de presentación de schedules

Hoja visible con `A1 = CFSHEET|schp:<UniqueId>` y `B1 = CFPRES|…` (fila 1 oculta), seguida del texto de la tabla tal como la muestra Revit. No tiene hoja de originales: nunca se importa.
