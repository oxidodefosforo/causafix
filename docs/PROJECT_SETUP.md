# CausaFixIt · Project Setup — Guía

Segunda herramienta de la suite. Reemplaza el proceso de Dynamo para preparar documentación:

**Niveles → conjuntos de vistas → nombres → sheets y composiciones → numeración → colocación/alineación/títulos → revisión → ejecución.**

Se abre desde **CausaFixIt → Documentation → Project Setup**. Se instala con el mismo paquete que Excel Link (por usuario, sin administrador); no tiene instalación propia. La interfaz está en inglés.

## 1. Pasos de la ventana

Todo es un **borrador** hasta pulsar **Run Setup…**. Elegir opciones o editar celdas nunca modifica el modelo. Solo leen de Revit: **Refresh**, los botones **Pick…** y **Match…**, **Open view/sheet** y la lectura de valores al añadir un parámetro de View o Sheet a un nombre (solo lectura).

| Paso | Qué haces |
|---|---|
| **1 Levels** | Marcas los niveles del host y les das un código corto (GF, L01, RF…) y, si hace falta, una zona. El código es solo de CausaFixIt y **no renombra el Level**. |
| **2 View Sets** | Cada conjunto (Lighting, Power, RCP…) define familia, tipo de vista, template, escala y recorte. Además lleva códigos de disciplina, View Set, zona, **condición**, **prefijo/propósito** y **serie**. Los nombres **View Name** y **Title on Sheet** se componen con **Build Name** (§2). En la lista, un clic en el nombre, la familia o el espacio libre **selecciona** la regla; solo la casilla activa o desactiva (§1.1). La pestaña *Title on Sheet* incluye **Take title position from a placed view…** (§5.3). |
| **3 Sheets** | Tres pestañas. **Sheet rules**: por conjunto, el modo de sheet, el title block, el **perfil de numeración**, el tipo y la rotación del viewport, el grupo y el slot. **Sheet Name**: Build Name del nombre del sheet. **Views on Sheet**: composiciones con varias vistas por sheet (§4). |
| **4 Numbering** | Perfiles de numeración reutilizables, con segmentos opcionales, aprobación de renumeraciones y una vista previa de la renumeración (§3). |
| **5 Placement** | Grupos de alineación (ancla del modelo y slots del papel), tolerancia y área útil. Aquí está también **View Title Placement** (§5) y la lista de vistas existentes que usan *Place / Re-align Existing* y *Update Titles Only*. |
| **6 Review** | Plan **agrupado por sheet**: cada sheet muestra sus vistas, el papel de cada una (primaria o secundaria), el perfil y la secuencia, el título y la acción. Se pueden excluir filas, editar nombres o números, elegir la numeración de un sheet y el título de un viewport. Incluye el detalle de los componentes de cada nombre y número (origen y valor) y la previsualización del sheet. |

Modos (arriba a la derecha): **Full Setup**, **Views Only**, **Sheets Only**, **Place / Re-align Existing** y **Update Titles Only**.

### 1.1. Lista View Sets (rules)

- **Seleccionar:** clic en el nombre, la familia o el espacio libre de la fila. El panel derecho muestra esa regla y su estado Enabled no cambia. También con ↑ ↓.
- **Activar/desactivar:** solo con la casilla. Cada clic cambia el estado una vez y además selecciona esa fila. Una regla desactivada (texto atenuado) se selecciona y edita igual.
- Al cambiar de fila se confirma lo que estabas escribiendo en la regla anterior. Tras *Refresh* la selección se recupera por la identidad de la regla, no por su posición. La lista de composiciones (Views on Sheet) funciona igual.

## 2. Build Name: nombres sin escribir tokens

Cada destino tiene su propia regla: **View Name**, **Title on Sheet**, **Sheet Name** y **Sheet Number**, este último en el perfil de numeración. Cambiar una no afecta a las demás, salvo que marques **Use same rule as…**.

- **Add**: buscador agrupado por origen.
  - **CausaFixIt fields** (configuración): disciplina, nombre/código del View Set, código de nivel, zona, condición, prefijo/propósito y serie.
  - **Computed by the plan**: número de proyecto, nombre y elevación del nivel, nombre de template y de tipo, escala, secuencia del sheet, nombre de la composición y el **resultado** de otro nombre (p. ej. un Sheet Name construido con el View Name).
  - **Revit · Project Information / Level / View / Sheet / Title Block (type) / Title Block (instance)**: parámetros reales, incluidos los compartidos y los de proyecto de tu plantilla. Los homónimos se distinguen (GUID, built-in, de proyecto, instancia/tipo). Un parámetro de solo lectura sirve como fuente, porque leer no es escribir.
- Por componente puedes ajustar: **On**, **Separator**, **Required**, **Case**, **Pad**, **Fallback**, **Map values** (`Proposed=P; Demolished=D; Existing=E`), unidad y decimales de la elevación, y los botones ▲ ▼ ⧉ ✕. **Add fixed text** añade texto fijo.
- El **separador** de un componente solo se escribe si hay algo antes y el componente tiene valor. Un componente opcional vacío o desactivado desaparece junto con su separador, así que no se producen nombres como `E--L05--POWER`. Los guiones que forman parte de un valor o de un texto fijo se conservan.
- **Pad** solo rellena valores numéricos. Los códigos de texto como `007` o `L7` no cambian.
- **Requerido y vacío = error** en la fila, con el origen indicado; nunca un nombre incompleto. Esto incluye el parámetro de una vista que todavía no existe: sus valores solo se conocen después de crearla, y CausaFixIt no usa los del template como si fueran finales. Pon un *fallback*, márcalo opcional o usa un campo planificado.
- Las **dependencias** entre nombres se resuelven solas. Un ciclo, como un View Name que depende de su propio resultado, es un error comprensible.
- **Preview**: filas reales del plan (niveles × conjuntos) con el resultado y, en el *tooltip*, cada componente con su valor.
- **Advanced pattern**: edición manual con tokens como en la versión anterior (`{DisciplineCode}-{LevelCode}…`, `{Sequence:000}`, `:upper`/`:lower`). **Convert to builder** solo convierte si el significado no cambia.

## 3. Numeración (paso Numbering)

- Un **perfil** es una regla de Sheet Number con segmentos en el orden que quieras: prefijo/propósito (FCC u otro), disciplina, serie, zona, condición, código de nivel, secuencia, texto fijo y cualquier otro campo o parámetro del mismo buscador. Todos se pueden activar u omitir.
- El perfil **Standard** que trae la herramienta da, con serie 1 y secuencia 01: `E-101`, con zona `E-A-101`, con condición *Proposed* `E-A-P-101` y con prefijo `FCC-E-A-P-101`. Es un ejemplo; las abreviaturas son editables.
- **La condición es un código documental:** no cambia la Phase ni el Phase Filter de la vista. Solo los tomaría si añadieras ese parámetro explícitamente como fuente.
- **Secuencia:** inicio, incremento, orden (nivel → conjunto o conjunto → nivel) y **reinicio** por disciplina, serie, View Set, zona, condición, prefijo o nivel (cualquier combinación). Los perfiles con el mismo **Counter** comparten contador.
- **La unidad de numeración es el sheet**, no la vista. Un sheet con una planta y un RCP superpuesto tiene un solo número, y añadir un miembro no consume otro.
- **Estabilidad:** los números asignados se conservan al refrescar o al añadir niveles o miembros. Los números de sheets que salen de la selección quedan reservados.
- **Cambiar la regla o el prefijo no renumera:** el sheet existente conserva su número y aparece en **Pending renumbering** con el número actual y el propuesto. Solo cambia si lo apruebas (*Approve selected*) o si editas el número en Review.
- **Renumber:** *Preview renumbering* vuelve a asignar las secuencias en orden, para todo o solo para el perfil seleccionado, y muestra el número actual y el propuesto. *Apply selected* aplica las filas marcadas. Nada se escribe hasta Run Setup.
- **Identidad del sheet:** se guarda en el sheet con una clave estable (fila, composición + nivel, o sheet existente elegido), separada del número visible. Dos sheets distintos que produzcan el mismo número son un **conflicto**, nunca se fusionan. Compartir un sheet es siempre una composición explícita.
- Asignación: por View Set (Sheet rules), por composición o por sheet (Review). **Add / Duplicate / Remove / Import… / Export…**; para renombrar, edita el nombre.

## 4. Views on Sheet: varias vistas por sheet y superposición

En **Sheets → Views on Sheet** se definen las **composiciones**. Cada composición genera un sheet por nivel con sus miembros.

- Un miembro puede ser un **View Set**, aplicado en el nivel de ese sheet, o una **vista existente**, que solo se usa en su propio nivel y conserva su nombre, template y título. Cada miembro se puede activar o retirar, y uno se marca como **Primary**.
- El sheet tiene **un** nombre, número, title block y perfil de numeración. Por defecto los toma del View Set primario; también puedes definirlos en la composición, incluida una regla Sheet Name propia.
- **Separate**: cada miembro en su slot.
- **Overlay**: los miembros se superponen. Es el caso de *falso cielo raso encima de la planta*: el RCP se superpone a la planta, no se coloca más arriba en el papel.
  - Todos usan el mismo grupo y slot, y se alinean con **Model Anchor** (§5.1) para que la misma referencia del edificio caiga en el mismo punto del papel aunque sus recortes difieran.
  - Se exige la misma escala y la misma rotación del viewport. Planta y RCP no se rechazan por tener la vista en sentidos opuestos: se compara el resultado proyectado en el papel. Si hay segunda referencia, también se verifica.
  - El solape **intencional** dentro de una composición Overlay se permite. Dos vistas no relacionadas en el mismo slot siguen siendo un error.
- **Títulos:** *OnlyPrimary* (por defecto), *All* o *PerMember*. Para ocultar un título, elige **Hidden-title viewport type**, un tipo con *Show Title = No* de tu plantilla. CausaFixIt lo asigna, pero no edita tipos compartidos ni crea tipos derivados (ver §8). Longitud de línea 0 no es una forma de ocultar el título. Con varios títulos visibles, usa *Title dX/dY* por miembro para que no se solapen.
- **Reutilización:** se comprueba `CanAddViewToSheet`. Una planta o un RCP colocado en otro sheet no se mueve. Elige otra vista o marca en Review *Place as dependent copy*, que crea una dependiente con nombre propio (`<nombre> - <sheet>`). Quitar un miembro o una composición **no borra** vistas, sheets ni viewports.
- Los templates de cada miembro son los de su View Set; nunca se editan. La coincidencia geométrica no garantiza que fondos, rellenos o categorías visibles den una composición útil: revísalo en Revit. CausaFixIt no ofrece un orden delante/detrás de viewports, porque la API no lo expone para viewports.

## 5. Placement

### 5.1. Alineación (sin cambios respecto a la versión anterior)

**Model Anchor Alignment** (por defecto): elige *Pick grid intersection* o *Pick point* y define el slot con *Match reference viewport…* o con X/Y en el marco *Sheet* o *TitleBlock*. Al ejecutar, cada viewport sigue esta secuencia: posición provisional → regenerar → `View.GetModelToProjectionTransforms()` × `Viewport.GetProjectionToSheetTransform()` → `SetBoxCenter` → regenerar → **error medido** ≤ tolerancia. Una escala u orientación distinta se informa como *Anchor aligned; different scale/orientation*; en un Overlay es un fallo y se revierte todo. **Viewport Center Placement** es maquetación y no vale para Overlay. Los split crops están bloqueados y los viewports fijados se omiten salvo que lo permitas.

### 5.2. View Title Placement

Controla la posición del **título del viewport**, no del title block, de forma independiente del dibujo. El texto del título sigue siendo *Title on Sheet*.

- **Niveles:** *Profile default* → *Group* → *Composition* → **viewport** (en Review, *View title of this viewport*). Los niveles inferiores heredan salvo que marques **Override**.
- **Modos:**
  - **KeepExisting**: no se toca.
  - **RelativeToViewport**: la esquina elegida del título (BottomLeft, BottomCenter, BottomRight, TopLeft…) va a la misma esquina del recuadro del viewport más X/Y en mm. En un Overlay, el recuadro puede ser el del viewport o la **envolvente conjunta** (*OverlayEnvelope*).
  - **FixedSheetPosition**: la esquina del título va a un punto X/Y en mm del sheet o del title block del sheet.
  - **MatchReference**: en Revit eliges un viewport cuyo título está bien colocado y CausaFixIt captura su posición. Antes eliges *Capture as*: *RelativeOffset* (desplazamiento respecto a su dibujo) o *FixedPosition* (posición en el papel). Las dos no se mezclan.
- **Línea del título:** *Keep* o *Set* con longitud en mm; un valor negativo la dirige hacia la izquierda.
- **Cómo se aplica:**
  1. Después de fijar tipo, rotación y recorte, y de alinear el dibujo, se ajusta `Viewport.LabelLineLength`.
  2. Se mide el título con `GetLabelOutline` y se calcula `Viewport.LabelOffset`. Este es el vector desde la esquina inferior izquierda del viewport (con rotación None) hasta el inicio de la línea del título, así que se respeta el desfase propio de cada familia de título.
  3. Se regenera y se verifica.
  4. Los viewports rotados se resuelven en su propio marco.
  5. Se comprueba que el dibujo **no se ha movido**; si se moviera, se revierte todo.
  6. Si el título no queda dentro de tolerancia, se restaura como estaba y se informa *TitleNotPlaced*.
- **Update Titles Only** (modo): actualiza solo los títulos aprobados y el tipo de viewport de los títulos ocultos en viewports **existentes**. No crea sheets, no renombra vistas y no realinea dibujos. Respeta worksharing y viewports fijados, y cada viewport puede quedarse con *KeepExisting*.
- **MatchReference sin elección previa = FixedPosition.** Si no has elegido *Capture as*, una captura nueva usa la posición en el papel, que es lo que alinea los títulos entre planos. Se conserva la elección explícita, el tipo de una captura anterior (también la de perfiles antiguos) y el modo *RelativeToViewport* si era el que tenías. La captura guarda los dos resultados en campos distintos y solo aplica el elegido.
- **Preview:** gris = viewports y títulos existentes leídos del modelo; discontinuo naranja = posición **estimada** del título (tamaño nominal); continuo = **medido** en Revit tras ejecutar. Un título que saldría del área útil se avisa.

### 5.3. Tomar la posición del título desde una vista colocada

Accesible desde **View Sets → Title on Sheet → Take title position from a placed view…** y desde *Match reference title…* en View Title Placement. Ambos usan **la misma configuración** (§5.2).

1. **Apply to:** elige el nivel que vas a editar: *Profile default*, el *Group* del View Set o la *Composition* a la que pertenece. Por defecto aparece el que esas vistas usan ahora. El texto inferior indica **qué View Sets lo usan**, qué grupos o composiciones conservan su propia posición y cuántos overrides de viewport (Review) siguen mandando. Para una posición común, usa *Profile default*.
2. En Revit, **abre el plano** con el título bien colocado (por ejemplo, el de Lighting del Basement) y pulsa el botón. Selecciona su **viewport** (Esc cancela).
3. CausaFixIt mide el título con `GetLabelOutline` y guarda la esquina elegida (*Title corner*), la longitud y dirección de la línea, y la vista y el plano usados. **No copia el texto**: cada vista conserva su *Title on Sheet* (nivel incluido). Tampoco duplica la vista ni cambia su template o su nivel.
4. **Errores:** si el plano activo no es un sheet, si lo elegido no es un viewport o si su título está oculto, aparece el motivo y la opción de **reintentar**. Lo mismo con el marco *TitleBlock* cuando el plano de referencia no tiene exactamente un title block, porque CausaFixIt no adivina el marco. Cancelar conserva los valores anteriores. Un grupo o composición que heredaba recibe su propia posición **solo tras una captura válida**.
5. **Aplicar:** elegir la referencia solo cambia el borrador.
   - Vistas nuevas: **Run Setup**. El título se coloca después de alinear el dibujo, se verifica con la tolerancia y el dibujo no se mueve.
   - Vistas ya colocadas: modo **Update Titles Only**. Reutiliza los viewports existentes y no crea vistas, planos ni viewports.
   - En Planta + RCP se mantienen *OnlyPrimary/All/PerMember* y el título oculto sigue oculto.

*FixedPosition* deja la misma esquina en el mismo punto del papel, o del title block si eliges ese marco. Un título más largo crece desde esa esquina. *RelativeOffset* reproduce la relación entre título y viewport, así que, con crops de distinto tamaño, los títulos no coinciden en el papel.

## 6. Ejecución

- **Atómica:** un TransactionGroup (un Deshacer). Los pasos son: nombres temporales → vistas → sheets (primero la fila primaria de cada sheet) → viewports y tipos → alineación con verificación → títulos. Si algo falla se revierte todo. El resultado incluye *Titles* y *TitlePlaced* o *TitleNotPlaced*, con el error del título en mm.
- **Reejecutar sin cambios** crea cero vistas, sheets y viewports. Las asociaciones viven en los elementos (Extensible Storage `CausaFixProjectSetup`). Añadir un miembro o un nivel solo crea lo nuevo.
- No guarda ni sincroniza el modelo, no suprime avisos de Revit y no borra nada.

## 7. Perfiles y migración

- El formato de perfil pasa a ser el **2** e incluye reglas Build Name con sus parámetros, perfiles de numeración, composiciones, posiciones de título, secuencias, aprobaciones y overrides.
- **Un perfil anterior (formato 1) se migra al cargarlo**, sin perder niveles, templates, slots, anclas, secuencias ni overrides:
  - Cada patrón se convierte en una regla del constructor equivalente. Si no se puede convertir sin cambiar el significado, queda como **Advanced/Legacy** con una nota.
  - Cada patrón de Sheet Number se convierte en un perfil de numeración que mantiene el contador anterior (por conjunto, por nivel o global).
  - Los View Sets que compartían sheet por número pasan a una **composición explícita** (Separate). Se conserva su clave antigua, así que los sheets existentes se siguen reconociendo (sin duplicados) y su asociación se reescribe a la clave estable en el siguiente Run Setup. Los números no cambian.
  - Se avisa de lo migrado.
- En otro proyecto se informan las referencias que faltan y no se empareja nada por nombre:
  - niveles, tipos, templates y title blocks;
  - tipos de viewport;
  - vistas existentes usadas como miembros;
  - **parámetros de proyecto**: solo los compartidos (GUID) y los built-in conservan su identidad entre documentos.

  Las anclas, secuencias, aprobaciones y claves heredadas del otro documento se descartan.
- Los perfiles de numeración se exportan e importan por separado (`*.cfnumbering.json`) y entran con claves nuevas.

## 8. Limitaciones conocidas de esta versión

- **Tipo de viewport sin título:** no se crea uno derivado automáticamente. La API no documenta de forma verificable los valores de *Show Title*, y modificar un tipo compartido afectaría a otros planos. Usa un tipo de tu plantilla con *Show Title = No*.
- Orden delante/detrás de viewports superpuestos: no disponible (la API no lo expone para viewports).
- Valores de parámetros de **vistas o sheets que aún no existen**: no se conocen hasta crearlos. Se exige un fallback o que el componente sea opcional.
- La posición estimada del título en la previsualización usa un tamaño nominal; la real la mide Revit tras ejecutar.

## 9. Prueba guiada en Revit 2026

Usa una copia de un modelo con *Ground Floor* y *Level 01*, una retícula de ejes, templates de planta (Lighting) y de techo (RCP), un title block A1 y un tipo de viewport sin título.

1. **Nombres con parámetros (A):** en View Sets → View Name, añade *Level code*, un parámetro compartido propio (Revit · Level) y *View Set name*. Reordénalos con ▲▼. Comprueba que View Name, Title on Sheet y Sheet Name dan resultados distintos sin escribir tokens. Añade un Sheet Name que use *View Name (result)* y luego haz que View Name use *Sheet Name (result)*: debe salir *Circular reference*.
2. **Numeración (B):** en Numbering, perfil *Standard*. Prueba con y sin zona, con *Condition = Proposed* y con prefijo FCC: `E-101`, `E-A-101`, `E-A-P-101`, `FCC-E-A-P-101`, sin separadores sobrantes. Las fases de las vistas no cambian.
3. **Planta + RCP (C):** en Views on Sheet, composición *Overlay* con Lighting (primaria) y RCP, *OnlyPrimary* y el tipo sin título. Run Setup: un sheet por nivel y un solo número. El ancla A/1 de ambas vistas coincide (error ≤ 0,1 mm en Results) aunque los recortes difieran, y solo se ve el título de la planta. Añade después otro miembro: no se crea otro sheet ni cambia el número.
4. **Títulos (D):** *RelativeToViewport*, BottomLeft, Y = −8 mm. Run Setup y comprueba que el título queda 8 mm bajo el dibujo y que la columna *Title (mm)* es pequeña. Repite con un viewport rotado, con un title block desplazado (FixedSheetPosition en marco TitleBlock) y con *MatchReference*. Después usa **Update Titles Only** sobre un sheet existente: solo se mueven títulos.
   - **Referencia de título:** en View Sets → Title on Sheet, con *Apply to = Profile default*, pulsa *Take title position from a placed view…* y elige el viewport de Lighting del Basement. Genera tres niveles con crops distintos. En Results, *Title (mm)* debe salir ≤ tolerancia y la esquina del título coincidir en todos los planos (mídelo en Revit). Cada título debe mostrar su nivel y el dibujo no debe moverse. Cancela una selección: nada cambia. Prueba también con un plano activo que no sea sheet y con un viewport de título oculto (RCP).
   - **Lista View Sets:** clic en POWER, LIGHTING, CONTAINMENT y RCP sobre el texto. Cada clic cambia el editor sin tocar las casillas. Un clic en una casilla cambia solo esa regla.
5. **Persistencia (E):** guarda y vuelve a cargar el perfil (todas las reglas siguen). Run Setup otra vez debe dar cero elementos nuevos. Cambia el prefijo: aparece *Renumber pending* en el mismo sheet. Dos conjuntos con el mismo número dan un conflicto, no se fusionan. Carga un perfil de la versión anterior y comprueba que no aparecen duplicados.
6. Excel Link sigue funcionando igual (GUIA §12).

Anota los resultados en `docs/CHECKPOINT.md`.
