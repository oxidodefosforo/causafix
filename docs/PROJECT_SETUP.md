# CausaFix · Project Setup — Guía

Segunda herramienta de la suite. Reemplaza el proceso de Dynamo para preparar documentación:

**Niveles → conjuntos de vistas → templates → nombres → sheets → colocación/alineación → revisión → ejecución.**

Se abre desde la pestaña **CausaFix → Documentation → Project Setup**. Se instala con el mismo paquete que Excel Link; no hay instalación aparte.

## 1. Pasos de la ventana

Todo es un **borrador** hasta pulsar **Run Setup…**. Elegir opciones o editar celdas nunca modifica el modelo; solo leen de Revit **Refresh**, los botones **Pick…** y **Open view/sheet**.

| Paso | Qué haces |
|---|---|
| **1 Levels** | Marcas los niveles del modelo anfitrión y les das un código corto (GF, L01, RF…). El código es solo de CausaFix: **no renombra el Level**. Puedes ordenarlos por elevación o a mano. Se muestran las plantas que ya existen en cada nivel. Los niveles de vínculos no sirven para crear vistas; si un perfil pide un nivel que no existe, se avisa y no se crea ni se copia. |
| **2 View Sets** | Cada conjunto (Lighting, Power…) es una regla: familia (Floor Plan, Reflected Ceiling Plan, Structural Plan si existe), tipo de vista, template, escala, recorte/scope box, patrón de **View Name** y de **Title on Sheet**, y opcionalmente un subconjunto de niveles. Abajo se ve un ejemplo del nombre resultante. |
| **3 Sheets** | Por conjunto: un sheet por vista (por defecto), sheet compartido (las filas con el mismo número van al mismo sheet, cada una en su slot), sheet existente o ninguno; title block; patrones de **Sheet Number** y **Sheet Name**; secuencia (inicio, paso, por conjunto/nivel/global); tipo y rotación del viewport; grupo de alineación y slot. |
| **4 Placement** | Grupos de alineación (edificio, zona, sector): método, **ancla del modelo** y **slots** del papel. También la tolerancia (0,1 mm por defecto), el área útil del papel y si se permite realinear viewports fijados. En modo *Place / Re-align Existing* aquí eliges vistas existentes y su sheet. |
| **5 Review** | Tabla final: qué se creará, reutilizará, actualizará o quedará sin cambios, con estado y motivo. Puedes excluir filas y editar View Name, Title on Sheet, Sheet Number y Sheet Name por fila (se guardan como cambios de esa fila). A la derecha, la previsualización del sheet. |

Modos (arriba a la derecha): **Full Setup**, **Views Only**, **Sheets Only**, **Place / Re-align Existing**. Todos usan el mismo motor.

## 2. Nombres y números

Tokens: `{ProjectNumber}` `{DisciplineCode}` `{ViewSetCode}` `{ViewSetName}` `{LevelName}` `{LevelCode}` `{Zone}` `{Sequence}`.

- `{Sequence:000}` rellena con ceros (007); `{LevelName:upper}` / `:lower` cambian mayúsculas; además cada patrón tiene una opción de mayúsculas/minúsculas.
- `{{` y `}}` escriben llaves literales; el resto del texto (prefijos, sufijos, separadores) se escribe tal cual.
- Un token sin valor (p. ej. `{Zone}` vacío o un nivel sin código), un token desconocido o un carácter que Revit no admite (`\ : { } [ ] | ; < > ? `` ~`) **es un error visible** en Review, nunca un nombre incompleto.

Ejemplos de patrones: View Name `{DisciplineCode}-{LevelCode}-{ViewSetName}` · Title on Sheet `{LevelName} - {ViewSetName}` · Sheet Number `{DisciplineCode}-1{Sequence:00}` · Sheet Name `{LevelName:upper} - {ViewSetName}`.

**Secuencias estables:** cada fila guarda el número que recibió. Si añades niveles, las filas existentes conservan su número y las nuevas toman el siguiente libre. Para renumerar todo usa **Regenerate numbering…** (acción explícita).

**Unicidad:** View Name es único por tipo de vista (dos plantas no pueden llamarse igual; una planta y un RCP sí). Sheet Number es único en todo el documento, incluidos los placeholders. Sheet Name y Title on Sheet pueden repetirse. Las colisiones con elementos existentes o entre filas se muestran en Review para corregir, editar o añadir sufijo; CausaFix no cambia nombres por su cuenta. Los **intercambios** de nombre/número entre vistas o sheets existentes se resuelven con nombres temporales dentro de la misma transacción.

## 3. Templates y escala

El template se asigna manteniendo su vínculo y se comprueba con `View.IsValidViewTemplate` sobre la vista real al ejecutar. Si el template controla la escala, el scope box u otro valor pedido, Review muestra el **valor efectivo** y el aviso; el template nunca se modifica ni se desvincula. Un template de otra familia (p. ej. RCP para una planta) es un error.

## 4. Alineación (lo central)

**Model Anchor Alignment (por defecto):** una referencia del edificio cae en el mismo punto del papel en todos los sheets del grupo, aunque los recortes y las anotaciones sean distintos.

1. **Pick grid intersection** (dos ejes rectos del host) o **Pick point** (en una planta con plano de trabajo). Se guarda en coordenadas internas del modelo; no se mezclan origen interno, Project Base Point, Survey Point ni coordenadas compartidas. El punto XY se proyecta sobre el nivel de cada planta.
2. Define dónde cae en el papel (por slot):
   - **Match reference viewport…**: abre en Revit el sheet de referencia y elige su viewport bien colocado; CausaFix calcula dónde cae el ancla y lo usa como objetivo.
   - **Manual**: X/Y en mm.
   - **Frame**: `Sheet` (coordenadas del sheet) o `TitleBlock` (relativas a la instancia del title block de cada sheet; requiere un único title block por sheet).
3. Al ejecutar, por cada viewport: se crea en una posición provisional → se regenera → se calcula dónde cae el ancla con `View.GetModelToProjectionTransforms()` × `Viewport.GetProjectionToSheetTransform()` → se mueve el viewport esa diferencia con `SetBoxCenter` (nunca se mueve el edificio) → se regenera y se **mide el error final**.
4. Solo se declara **Aligned** si el error ≤ tolerancia. Si la escala u orientación difieren del primer viewport del grupo (o falla la segunda referencia, si la elegiste con **Pick 2nd reference**), se informa **Anchor aligned; different scale/orientation** en lugar de afirmar una superposición completa.

**Viewport Center Placement:** coloca el centro del recuadro del viewport en el slot. Es maquetación; no garantiza que el edificio coincida entre sheets.

Casos bloqueados o avisados: **split crop** (varias regiones con transformaciones distintas) → bloqueado con mensaje; recorte no rectangular → admitido (una sola región); viewports **fijados** → se omiten salvo que permitas realinearlos (se restaura el pin); vista **ya colocada en otro sheet** → error salvo que marques *Place as dependent copy* (crea una dependiente explícitamente); nunca se mueven vistas entre sheets en silencio.

Cambiar recortes o templates después puede requerir volver a alinear: usa el modo **Place / Re-align Existing** o vuelve a ejecutar el setup (solo mueve lo que esté fuera de tolerancia). No hay seguimiento automático permanente.

## 5. Ejecución

- **Atómica:** un único TransactionGroup (un solo Deshacer). Si falla una operación o una verificación de alineación de las filas aprobadas, se revierte todo: no quedan vistas ni sheets huérfanos. El resultado lista Created, Reused, Updated, Aligned, Anchor only, Unchanged, Skipped, Failed y Rolled back; nada revertido se presenta como hecho.
- El plan se revalida dentro de Revit (niveles, tipos, templates, números, ocupación de worksharing). Si el documento activo cambió, la ejecución se bloquea.
- **Reejecutar sin cambios crea cero vistas, cero sheets y cero viewports** y no mueve lo que ya está alineado: cada vista/sheet generado guarda su clave de fila con **Extensible Storage** (esquema `CausaFixProjectSetup`), así CausaFix lo reconoce aunque lo renombres. Los nombres solo sirven de pista.
- No guarda ni sincroniza el modelo; no suprime avisos de Revit; no borra vistas, sheets ni anotaciones.
- Cancelar es posible mientras se espera un *pick* o antes de ejecutar. Durante Run Setup Revit está ocupado con la transacción y no se puede interrumpir a medias (por diseño, para que el resultado sea todo o nada).

## 6. Perfiles

Guardan niveles/códigos, reglas, patrones, secuencias, title blocks, slots, alineación, tolerancia y opciones en JSON (`%AppData%\CausaFix\ProjectSetup\Profiles`). Al cargar un perfil en otro proyecto, las referencias que no existen (niveles, tipos de vista, templates, title blocks, scope boxes) se informan y quedan vacías para que las elijas; **nunca se emparejan por nombre**. Las anclas de otro documento se descartan y hay que volver a elegirlas.

## 7. Prueba guiada en Revit 2026 (ejemplo de aceptación)

Usa una copia de un modelo con los niveles *Ground Floor* y *Level 01*, una retícula de ejes, dos templates de planta (Lighting y Power) y un title block A1 cargado.

1. **Levels:** marca los dos niveles; códigos GF y L01.
2. **View Sets:** deja *Lighting* y *Power* (o créalos), tipo Floor Plan, sus templates, View Name `E-{LevelCode}-{ViewSetName:upper}`.
3. **Sheets:** title block A1; Lighting `E-1{Sequence:00}`, Power `E-2{Sequence:00}`, nombre `{LevelName:upper} - {ViewSetName:upper}`.
4. **Placement:** *Pick grid intersection* (p. ej. A/1). Ejecuta una vez solo con una fila, coloca ese viewport donde quieras, y luego en el slot usa *Match reference viewport…* sobre él. (O escribe X/Y.)
5. **Review:** deben aparecer E-GF-LIGHTING/E-101, E-L01-LIGHTING/E-102, E-GF-POWER/E-201, E-L01-POWER/E-202. **Run Setup**.
6. Comprueba: la intersección A/1 cae en el mismo punto del papel en los cuatro sheets (error en la tabla de resultados ≤ 0,1 mm), también si Lighting y Power tienen recortes o anotaciones distintos; con la misma escala, una segunda intersección también coincide (*Pick 2nd reference*).
7. **Run Setup otra vez:** cero elementos nuevos, nada se mueve.
8. Cambia la escala de una vista a mano y realinea: debe salir *Anchor aligned; different scale/orientation*.
9. Mueve el title block en un sheet y usa slots con Frame = TitleBlock: el ancla sigue al title block.
10. Fuerza un fallo (p. ej. un sheet ocupado por otro usuario): nada se conserva y el resultado dice *Rolled back*.
11. Lote de 10 niveles × 4 conjuntos: anota el tiempo de Run Setup en `docs/CHECKPOINT.md`.
