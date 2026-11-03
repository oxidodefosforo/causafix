# CausaFixIt · Project Cleanup — Guía

**Eliminar documentación conservando la vista activa.** Es la tercera herramienta de CausaFixIt (pestaña CausaFixIt → panel *Documentation*, después de Project Setup).

## 1. Uso

1. Abre en Revit la vista que quieres conservar y pulsa **Project Cleanup**.
2. La ventana analiza el documento activo y muestra:
   - **Se conservará: [vista activa] — [tipo]**;
   - cuántas vistas, planos, schedules, leyendas y panel schedules se eliminarán;
   - la lista con nombre, tipo, número de plano y acción;
   - lo que se conserva, con su motivo;
   - las consecuencias previstas.
3. Opcional: marca **Borrar también** para incluir **CAD (vinculados e importados)**, **vínculos de Revit** o **imágenes**. Al lado de cada opción aparece cuántos hay en el modelo. Cada casilla reconstruye la revisión al momento; vienen desmarcadas.
4. Revisa el alcance. **Actualizar análisis** vuelve a leer el documento sin cambiar nada. **Cancelar** cierra sin cambiar nada.
5. **Eliminar documentación (N)** es la confirmación final: elimina exactamente lo mostrado, en una sola operación, sin más diálogos.
6. El resultado muestra lo eliminado por clase, los dependientes que Revit eliminó con cada elemento, lo conservado y los errores. **Abrir informe** abre el CSV en `%LocalAppData%\CausaFix\Reports` (`CausaFixIt-ProjectCleanup-…csv`).

Si cambias de vista o de documento antes de confirmar, no se elimina nada. La ventana reconstruye el análisis para la vista activa nueva y pide confirmar de nuevo. Pulsar otra vez el botón de la cinta también actualiza el análisis.

## 2. Qué se elimina y qué no

La clasificación sale de la clase y del `ViewType` de Revit, no de nombres ni del Project Browser.

| Se elimina | Se conserva siempre |
|---|---|
| Plantas, RCP, plantas estructurales y de áreas, secciones, alzados, detalles, callouts, vistas 3D (también las de trabajo), drafting views, recorridos, renders, tablas gráficas de pilares | La vista activa |
| Planos (`ViewSheet`), también vacíos y marcadores de posición | Plantillas de vista (view templates) y plantillas de panel schedule |
| Schedules (`ViewSchedule`): material takeoffs, listas de planos y vistas, bloques de notas, tablas de claves, leyendas de notas clave | Tablas de revisiones del cajetín, Project/System Browser, vistas internas y de informe (se listan como «Sistema») |
| Leyendas | Niveles, ejes, planos de referencia, elementos del modelo, habitaciones/espacios, familias y tipos, materiales, paneles y circuitos. Además, CAD, vínculos e imágenes mientras su opción esté desmarcada |
| Panel schedules (`PanelScheduleView`), como documentación | — |
| **Opción CAD:** CAD vinculados e importados (`CADLinkType` con todas sus `ImportInstance`) | El archivo DWG/DGN/… de origen |
| **Opción vínculos de Revit:** `RevitLinkType` con sus instancias y sus vínculos anidados | El archivo .rvt; los vínculos anidados no se quitan solos, solo con su vínculo principal |
| **Opción imágenes:** `ImageType` con todas sus `ImageInstance` (en vistas y planos) | — |

Pueden desaparecer, como consecuencia de cada borrado: viewports y colocaciones de schedules, anotaciones y detalles propios de esas vistas, cajetines de los planos, marcadores de sección, alzado y llamada, y las **áreas** de los planos de áreas (Revit las elimina con su plano). La revisión las estima antes de ejecutar y el resultado cuenta las reales.

Un CAD o una imagen con una instancia colocada en la vista activa, o en una vista que se conserva, se mantiene; la revisión lo indica. Al quitar un vínculo de Revit se pierde lo que dependa de él: elementos de Copy/Monitor, cotas y anotaciones que lo referencian.

No se ejecuta Purge Unused. El modelo no se guarda, no se sincroniza y no se cierra. Solo se trabaja en el documento activo; nunca se modifica el contenido de los vínculos.

## 3. Protecciones

- **Vista activa independiente** (planta, sección, 3D, drafting, leyenda, schedule): se conserva sin cambiar su identidad, nombre ni configuración, y se elimina el resto. No se crean vistas temporales ni se cambia de vista.
- **Vista activa dependiente:** se conserva su **primaria** (`GetPrimaryViewId`), porque borrar la primaria borraría también la activa. No se convierte en independiente. Para dejar una sola vista de usuario, ejecuta la herramienta desde una vista independiente, por ejemplo una 3D.
- **Plano activo:** se conservan el plano, las vistas y leyendas colocadas (`GetAllPlacedViews`), sus schedules (`ScheduleSheetInstance.ScheduleId`, todos los segmentos) y sus panel schedules (`PanelScheduleSheetInstance.ScheduleId`), además de las primarias de las vistas dependientes colocadas. El resto se elimina. Un schedule colocado también en otros planos se conserva; esos planos se eliminan y con ellos solo desaparece su colocación.
- **Worksharing y limitaciones de Revit:**
  - Se conservan, con su motivo, los elementos de otros usuarios o desactualizados y los que Revit no permite eliminar (`DocumentValidation.CanDeleteElement`).
  - Si se conserva una vista, se conserva también su primaria.
  - Si se conserva un plano que no se puede editar, se conserva lo colocado en él.
  - No se fuerza la propiedad de ningún elemento.
- **Documento de solo lectura:** no se elimina nada.

## 4. Ejecución

- Antes de modificar nada, Revit vuelve a leer el documento y compara la **firma** de la revisión (documento, vista activa y la acción de cada elemento). Si no coincide, no se ejecuta.
- Se cierran las ventanas abiertas de las vistas que se van a eliminar; la activa sigue abierta.
- Un `TransactionGroup` con una `Transaction`. Cada elemento se borra con `Document.Delete`, en este orden: primero los planos, luego las dependientes antes que sus primarias, y después vistas, leyendas, schedules y panel schedules. Los IDs se materializan antes de modificar y nunca se borra dos veces un ID que ya desapareció en cascada.
- Se revisan los IDs que devuelve cada `Delete`. Si alguno es un elemento protegido o un elemento del modelo fuera del alcance (nivel, eje, elemento del modelo, familia, tipo, material, vínculo o circuito), se **revierte todo**. Solo dejan de estar protegidos los CAD, vínculos e imágenes aprobados en la revisión, con sus instancias y vínculos anidados. Las opciones forman parte de la firma de la revisión.
- Tras el commit, y antes de cerrar el grupo, se comprueba que existen la vista activa y todos los protegidos.
- Los errores de Revit no se suprimen: un error revierte la operación y el resultado dice «No se aplicó la limpieza», con el elemento y el mensaje.
- Si todo va bien, queda **un solo paso de Deshacer**.

## 5. Prueba guiada en Revit (en una copia del modelo)

1. **3D independiente:** abre una vista 3D y ejecuta. Solo queda esa 3D. Niveles, ejes y modelo siguen intactos. Comprueba los números del resultado.
2. **Planta independiente:** como el caso 1, desde una planta.
3. **Dependiente:** desde una dependiente, la revisión muestra su primaria como conservada con el motivo. Al ejecutar, la activa sigue abierta.
4. **Plano con Lighting + RCP, leyenda y schedule:** desde el plano, todo su contenido aparece «Conservar (protección)». Tras ejecutar, el plano está completo y el resto de la documentación desaparece.
5. **Schedule o leyenda activos:** se conserva esa vista.
6. **Schedule en varios planos, schedule dividido en segmentos y panel schedule:** la definición y las colocaciones se cuentan por separado. Los paneles y circuitos siguen en el modelo.
7. **Primarias y dependientes:** ningún error de ID inválido; las dependientes salen como «Eliminada con su primaria».
8. **Cancelar:** no hay cambios en el modelo y el Deshacer no tiene ninguna entrada nueva.
9. **Error:** con un elemento de otro usuario (worksharing) aparece como conservado. Si un impedimento aparece al ejecutar, la operación se revierte y el mensaje lo explica.
10. **Deshacer:** un solo paso devuelve toda la documentación eliminada.
11. **Solo la vista activa:** el análisis dice «No hay documentación que eliminar» y el botón queda desactivado.
12. **Plantillas e internas:** siguen todas. Filtra con «Sistema (no se tocan)».
13. **CAD, vínculos e imágenes:** con cada opción marcada por separado se eliminan solo esos elementos. Con las opciones desmarcadas no se tocan (aparecen en Notas). Un CAD importado en la vista activa se conserva. Al quitar un vínculo con anidados desaparecen también los anidados, y los archivos externos siguen en disco.
14. **Actualización:** tras instalar sobre la versión anterior, Excel Link y Project Setup funcionan igual y Project Cleanup aparece a continuación.
