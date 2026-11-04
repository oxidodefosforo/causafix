# Keynote Manager (CausaFixIt 0.4.0)

Cuarta herramienta de CausaFixIt: **Documentation → Keynote Manager**. Es un editor completo del archivo de keynotes (notas clave) del modelo: crea grupos y subgrupos, añade notas, edita su texto, guarda el archivo y recarga Revit para que las etiquetas muestren el texto nuevo. Funcionalmente equivale al Keynote Manager de pyRevit, escrito en C#/WPF y sin depender de pyRevit.

> Nada se escribe en el archivo hasta **Guardar**, y nada cambia en el modelo hasta **Guardar y recargar** (o colocar una nota). La herramienta nunca guarda ni sincroniza el modelo.

## 1. Flujo básico

1. Abre un proyecto y pulsa **Keynote Manager**. La ventana lee la tabla de keynotes del modelo y abre su archivo configurado.
   - Si el modelo no tiene archivo, usa **Nuevo archivo…**. Se crea un TXT vacío en UTF-16 LE con BOM y la herramienta ofrece **vincularlo** al modelo.
   - **Abrir…** abre otro TXT. Si no es el del modelo, la herramienta ofrece vincularlo (LoadFrom).
2. **Nuevo grupo**: crea una nota de primer nivel (categoría). Ejemplo: clave `E`, texto `E — ELECTRICAL`.
3. Selecciona el grupo y pulsa **Nuevo subgrupo**. Ejemplo: clave `E-LTG`, texto `E-LTG — LIGHTING`, padre `E`.
4. Selecciona el subgrupo y pulsa **Nueva nota (Ctrl+N)**. Ejemplo: `E-LTG-001`, `PROVIDE LIGHT FITTING AS SHOWN.`
5. Pulsa **Guardar y recargar (Ctrl+S)**. Se escribe el archivo, se recarga la tabla en Revit y se verifica que Revit cargó exactamente lo guardado.
6. Para colocar una nota, selecciónala y pulsa **Nota de usuario** (o **De elemento** / **De material**), y haz clic en la vista.
7. Para editar texto, selecciona la nota y pulsa **Editar (F2)** o haz doble clic. Después pulsa **Guardar y recargar**: las etiquetas existentes muestran el texto nuevo.

En el formato nativo, grupos, subgrupos y notas son el mismo tipo de registro. Un grupo es un registro sin padre. Un subgrupo es un registro con padre que contiene otros registros.

## 2. Ventana

- **Estado del archivo**: ruta, número de registros, codificación, problemas y cambios sin guardar.
- **Estado en Revit**: ruta configurada, estado de carga, número de notas cargadas y si coinciden con el archivo. Son dos estados distintos: un archivo guardado no está en Revit hasta recargar.
- **Árbol**: filas con clave, texto, estado (Nueva / Modificada / Re-key) y uso en este proyecto.
  - Orden natural: `E-LTG-2` va antes de `E-LTG-10`.
  - Expandir y contraer se conserva al filtrar o editar.
  - Un clic en el texto o en el espacio libre selecciona la fila.
- **Filtros**:
  - búsqueda por clave o texto, con retardo de 250 ms;
  - uso: todas, con etiquetas, sin etiquetas, visibles en la vista activa, con cambios, con problemas;
  - origen: usuario, elemento, material;
  - «Solo esta rama»;
  - regex de inclusión y de exclusión, validadas (si una no es válida, se avisa y no se aplica).

  Los padres de una nota encontrada se muestran en gris como contexto.
- **Panel derecho**: uso de la nota seleccionada, vistas con sus etiquetas (doble clic para abrir la vista y seleccionarlas) y botones para colocar.

### Atajos

Los atajos no actúan mientras escribes en un cuadro de texto, salvo Ctrl+S, F5 y Ctrl+F.

| Tecla | Acción |
|---|---|
| F2, Intro, doble clic | Editar |
| Ctrl+N | Nueva nota |
| Ctrl+D | Duplicar |
| Ctrl+C / Ctrl+V | Copiar la rama / pegarla dentro de la fila seleccionada |
| Supr | Eliminar (con revisión) |
| Tab / Mayús+Tab | Bajar / subir de nivel |
| → / ← | Expandir / contraer |
| Ctrl+S | Guardar y recargar |
| F5 | Releer el archivo, la tabla y el uso |
| Ctrl+F | Buscar |

**Arrastrar** una fila sobre otra la mueve dentro de esa fila. Soltarla en el espacio libre la lleva al primer nivel. Mover nunca cambia claves.

## 3. Operaciones

- **Editar**: cambia el texto y el padre. La clave no cambia, así que las etiquetas existentes siguen apuntando a la misma nota.
- **Texto pegado de varias líneas** (Word, Excel, PDF): se convierte en una sola línea. Saltos de línea (incluidos VT, LS/PS y NEL) y tabuladores pasan a ser un espacio. El formulario muestra una vista previa de cómo quedará antes de aceptar.
- **Cambiar clave…** (re-key explícito):
  - Las hijas pasan a la clave nueva.
  - Al guardar y recargar, las etiquetas de usuario y los parámetros Keynote de tipos y materiales del proyecto se actualizan en el mismo paso.
  - Se bloquea, sin escribir nada, si la clave nueva ya se usa en el modelo, si un elemento está tomado por otro usuario o si un parámetro no es editable.
  - Otros proyectos que usen el mismo archivo no se pueden actualizar desde aquí y quedan con la clave antigua.
- **Eliminar**: muestra la rama completa y el uso de cada nota. Si alguna se usa, la confirmación es «Eliminar de todos modos»: Revit no borra las etiquetas, que quedan sin texto. Si el uso no se pudo leer, se avisa.
- **Duplicar**: propone una clave libre conservando los ceros (`E-LTG-009` → `E-LTG-010`; si la clave no termina en número, `-COPY`).
- **Copiar/pegar rama**: las claves que ya existen reciben una propuesta, editable en la revisión. Los padres internos de la rama siguen a las claves nuevas.
- **Transformaciones** (menú «Más»): MAYÚSCULAS, minúsculas, Tipo Título, Tipo oración y MAYÚSCULAS en toda la rama.
- **Plantillas de texto**: `RESERVED` y `DO NOT USE` vienen incluidas. Las tuyas se guardan con «Guardar texto como plantilla…» en `%AppData%\CausaFix\Keynotes\templates.json` y se insertan desde el formulario.
- **Revertir**: deshace el cambio pendiente de una nota. **Descartar todos los cambios** vuelve al archivo.
- **Importar…**: lee un TXT de Revit o de pyRevit y lo compara clave por clave con el borrador.
  - Las nuevas vienen marcadas. Las idénticas no hacen nada.
  - En los **conflictos** se conserva tu versión salvo que marques la importada.
  - Si la importación dejara la biblioteca inválida (por ejemplo, una nota sin su padre), no se aplica nada.
- **Exportar**: todas las notas, o solo las filtradas incluyendo sus padres. Se exporta en UTF-16 LE.

## 4. Guardar y recargar: qué hace exactamente

1. **Valida** el borrador: claves duplicadas o vacías, padre inexistente, padre de sí misma, ciclos, tabuladores o saltos de línea. Con errores no se escribe nada.
2. Si hay borrados o cambios de clave, muestra una **revisión** y, para los re-key, la comprobación de referencias del modelo.
3. **Escritura segura**:
   1. Vuelve a leer el archivo y compara su SHA-256 con el que se leyó al abrirlo.
   2. Si cambió (otro usuario, pyRevit o una sincronización), hace una **combinación a tres bandas**. Los cambios de cada lado que no chocan se conservan. Si la misma nota cambió distinto en los dos lados, eliges «Mío» o «Suyo» en cada una. Nunca gana en silencio el último en guardar.
   3. Copia el archivo anterior en `%LocalAppData%\CausaFix\Keynotes\Backups` (se guardan las 30 últimas copias por archivo).
   4. Escribe en un archivo temporal en la misma carpeta y lo sustituye (`File.Replace`). Si el recurso no lo admite, copia encima, porque la copia de seguridad ya existe.
   5. Relee el archivo y verifica el hash.
4. **Recarga en Revit** dentro de un `TransactionGroup` (un solo Deshacer): `KeynoteTable.Reload` con `KeyBasedTreeEntriesLoadResults`, más la actualización de referencias de los re-key.
   - El estado `Success` o `ResourceAlreadyCurrent` se considera correcto. Cualquier otro estado revierte el modelo.
   - Los avisos de lectura o sintaxis de Revit se muestran.
5. **Verifica** que las entradas cargadas en Revit coinciden con el archivo escrito.
6. Si la recarga falla, el archivo **sí quedó guardado**, y se ofrece **restaurar el archivo anterior**. Solo se restaura si el archivo sigue teniendo exactamente los bytes que escribió la herramienta. Tus cambios vuelven al borrador como pendientes. Nunca se revierte un archivo a ciegas.

**Guardar** sin recargar escribe el archivo pero deja Revit con la versión anterior. El estado en Revit lo indica. Un re-key siempre recarga, porque el archivo y el modelo deben cambiar juntos.

**Borrador local**: cada cambio se guarda en `%LocalAppData%\CausaFix\Keynotes\Drafts`. Si Revit o la ventana se cierran sin guardar, al volver a abrir el mismo archivo se ofrece recuperarlos. Si el archivo cambió mientras tanto, al guardar se combina.

**Cerrar con cambios**: Sí guarda (y recarga si es el archivo del modelo), No descarta y Cancelar sigue editando. Si se cierra el documento, la ventana se cierra sin preguntar y los cambios quedan en el borrador.

## 5. Colocar notas

- La clave debe estar en la tabla **recargada** en Revit. Si es nueva o tiene cambios sin recargar, se ofrece «Guardar y recargar» antes de colocar.
- Se comprueba que hay un tipo de etiqueta de nota clave cargado y que la vista activa admite etiquetas.
- Se usan los comandos nativos (`PostCommand`: User / Element / Material Keynote). Mientras dura la colocación, la herramienta:
  - registra las etiquetas nuevas con `DocumentChanged`;
  - cancela solo el diálogo de elección de clave de Revit (en usuario y elemento);
  - en el siguiente `Idling` escribe la clave en una transacción propia.

  Después libera todos los eventos.
- **Usuario** (por defecto): se escribe `KEY_VALUE` en cada etiqueta.
- **Elemento**: la clave pertenece al **tipo** del elemento etiquetado. Se asigna al tipo solo si su parámetro Keynote está vacío. Si el tipo ya tiene otra clave, no se cambia y se informa. Asignarla afecta a todas las instancias del tipo.
- **Material**: la API no expone qué material etiqueta una etiqueta de material. Por eso la clave se elige en el diálogo nativo de Revit y se respeta el material real.
- La colocación termina después de procesar las etiquetas, con Esc o tras 3 minutos sin actividad. Las etiquetas que coloques después de que termine piden la clave con el diálogo nativo.

## 6. Uso en el proyecto

- **Etiquetas**: categoría Keynote Tags, clave `KEY_VALUE` y origen `KEY_SOURCE_PARAM` (usuario, elemento o material), con la vista propietaria de cada una.
- **Asignaciones**: parámetro Keynote (`KEYNOTE_PARAM`) de tipos y materiales.
- Las etiquetas y las asignaciones se cuentan por separado. El recuento es **solo de este proyecto**: otros proyectos que compartan el archivo no son visibles.
- Si la consulta falla, el uso se muestra como **«?»** (desconocido), nunca como cero. El filtro «Sin etiquetas» no devuelve nada mientras el uso sea desconocido.

## 7. Formato y codificaciones

- **Registro**: una línea por registro, `Clave TAB Texto` (raíz) o `Clave TAB Texto TAB ClavePadre`.
  - Las claves se tratan como texto: `001` y `1` son claves distintas y los ceros se conservan.
  - Las líneas vacías se ignoran.
  - Las líneas que empiezan por `#` se conservan como comentarios al principio del archivo.
- **Líneas mal formadas** (sin tabulador o con más de dos): se informan con su número de línea y se conservan tal cual. Al guardar se pregunta si quitarlas.
- **Lectura**: UTF-16 LE/BE con BOM, UTF-16 LE sin BOM (detectado), UTF-8 con o sin BOM (UTF-8 estricto) y ANSI Windows-1252.
- **Escritura**:
  - Un archivo existente conserva su codificación y sus saltos de línea.
  - Si un archivo ANSI recibe caracteres que no caben en Windows-1252 (por ejemplo `Ω` o `≤`), se guarda como UTF-16 LE y se avisa, en lugar de escribir `?`.
  - Los archivos nuevos se escriben en **UTF-16 LE con BOM y CRLF**.
- **Caracteres admitidos**: Ø, ±, °, ², µ, ñ, tildes, comillas tipográficas y guiones se conservan.
- **pyRevit (DeffrelDB)**: se reconoce por los metadatos `# … @datastore(…) @db(…) @table(…)`. Se leen solo las tablas `categories` y `keynotes`; las de bloqueos y otras se ignoran, y `NULL` cuenta como padre vacío.
  - El archivo se abre en **solo lectura** y nunca se modifica.
  - **Guardar como…** crea una copia compatible en formato estándar, que se puede vincular.
  - El archivo `<nombre>.lock` de pyRevit nunca se toca. Si existe, otro usuario está editando y el archivo queda en solo lectura.

## 8. Red, ACC / Desktop Connector y colaboración

- **Rutas locales y de red (UNC)**: admitidas. La detección de cambios ajenos y la escritura segura son las mismas.
- **ACC / BIM 360 con Desktop Connector, OneDrive o SharePoint** (rutas `…\DC\ACCDocs\…` y similares): se avisa de que el archivo debe estar **descargado y sincronizado**.
  - Un marcador de posición sin descargar (atributos Offline / Recall) se descarga al leerlo; sin conexión, la lectura falla con un mensaje.
  - La sincronización puede tardar en subir el archivo guardado. Hasta entonces, otros usuarios ven la versión anterior.
  - Si la sincronización cambia el archivo mientras guardas, se detecta y se combina.
  - Si `File.Replace` no está soportado, se copia encima, con la copia de seguridad ya hecha.
- **Recursos de servidor** (tablas de keynotes servidas por un servicio externo de Autodesk, no por un archivo): la herramienta no las edita. Crea o abre un TXT local y vincúlalo.
- **Solo lectura**: atributo de solo lectura, archivo bloqueado o sin permiso, bloqueo de pyRevit o archivo DeffrelDB.
  - La edición se desactiva y se muestra el motivo. «Guardar como…» sigue disponible.
  - Si el documento es de solo lectura o la tabla de keynotes está tomada por otro usuario en un modelo compartido, no se recarga ni se vincula.
- **Trabajo compartido**: la recarga cambia la tabla de keynotes del modelo local. Para que otros usuarios la vean, tú sincronizas con el central cuando quieras; la herramienta no lo hace. Los re-key se bloquean si hay elementos tomados por otros usuarios.

## 9. Validación

### Probado automáticamente (CI y local)

Núcleo con Revit simulado: 30 pruebas, en .NET 8 y .NET Framework 4.8.

- Formato y codificaciones, ceros iniciales, Unicode, líneas mal formadas y DeffrelDB.
- Validación de duplicados, padres, ciclos y caracteres.
- Normalización de texto pegado y orden natural.
- Operaciones del borrador: re-key compuesto, indentar y desindentar, borrar rama, claves sugeridas.
- Importación sin sobrescritura silenciosa, pegado de ramas y combinación a tres bandas.
- Archivo: conflicto, copia de seguridad, restauración solo si no cambió, bloqueos y solo lectura, borradores y plantillas.
- Flujos de la ventana:
  - **E2E obligatorio**: biblioteca nueva → grupo `E` → subgrupo `E-LTG` → `E-LTG-001` → guardar y recargar → nota de usuario colocada → texto cambiado a «PROVIDE LED LIGHT FITTING AS SHOWN.» → recarga → la etiqueta muestra el texto nuevo → al reabrir persiste;
  - conflicto con otro usuario;
  - re-key con referencias del modelo;
  - recarga fallida con restauración;
  - uso desconocido;
  - borrado de una nota en uso;
  - filtros, copiar, pegar y exportar;
  - colocar una clave no recargada;
  - recuperación del borrador y cierre;
  - pyRevit → copia estándar.

Comprobación WPF real en CI (`tests/CausaFix.Ui.Tests`, ratón y teclado reales):

- árbol visible;
- clic en el texto y en el espacio libre;
- expandir y contraer;
- F2;
- Supr en el buscador no borra;
- búsqueda con retardo;
- cierre con cambios.

Compilación 2024/2025/2026 contra la API real en GitHub Actions.

### Requiere validación manual en Revit

Anota el resultado en `MATRIZ.md`.

1. **E2E en un modelo de prueba**:
   1. Nuevo archivo, vincular y crear `E — ELECTRICAL` / `E-LTG — LIGHTING` / `E-LTG-001`.
   2. Guardar y recargar.
   3. Colocar una nota de usuario en una planta y en una **leyenda**.
   4. Editar el texto a «PROVIDE LED LIGHT FITTING AS SHOWN.» y guardar y recargar: la etiqueta de la planta y la de la leyenda muestran el texto nuevo.
   5. Cerrar y reabrir el modelo: persiste.
2. Colocación de nota de **elemento**: con el tipo sin clave, la clave se asigna al tipo. Con el tipo con otra clave, no cambia y se informa. Comprobar que el diálogo de Revit se cancela; si aparece, anotar el `DialogId` del log.
3. Colocación de nota de **material** con el diálogo nativo.
4. Re-key de una clave con etiquetas de usuario y un tipo asignado: todo actualizado en un solo Deshacer.
5. Recarga con un archivo inválido (por ejemplo, una línea mal formada): avisos de Revit mostrados y modelo sin cambios.
6. Archivo en una carpeta de red y en ACC con Desktop Connector: lectura, guardado, conflicto (editar a la vez en otro equipo) y archivo sin descargar.
7. Modelo compartido: tabla tomada por otro usuario → solo lectura; sincronizar manualmente después.
8. Archivo de pyRevit (DeffrelDB) con bloqueo activo y sin él: solo lectura, «Guardar como…» compatible y bloqueo intacto.
9. Rendimiento con una biblioteca de 5 000 notas y un modelo con miles de etiquetas: apertura, filtro y uso.

## 10. Archivos de la herramienta

| Qué | Dónde |
|---|---|
| Lógica (formato, modelo, edición, combinación, archivos, ventana) | `src/CausaFix.Keynotes` |
| Adaptador Revit (tabla, recarga, uso, re-key, colocación) | `src/CausaFix.Revit/Keynotes/KeynoteService.cs` |
| Ventana y diálogos | `src/CausaFix.Addin/Modules/Keynotes` |
| Pruebas | `tests/CausaFix.Core.Tests/KeynoteTests.cs`, `KeynoteFakes.cs`; `tests/CausaFix.Ui.Tests` |
| Copias de seguridad / borradores | `%LocalAppData%\CausaFix\Keynotes\Backups`, `…\Drafts` |
| Plantillas de texto | `%AppData%\CausaFix\Keynotes\templates.json` |
| Log | `%LocalAppData%\CausaFix\Logs` (líneas `Keynotes:`) |
