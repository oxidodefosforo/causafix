# Matriz de funciones – CausaFixIt 0.1.0 (Excel Link + Project Setup)

Estado: **Implementado** · **Restringido** (implementado con un límite deliberado) · **Pendiente** (no incluido en 0.1.0).

Verificación disponible en esta entrega:
- **T** = pruebas automáticas que pasan (81/81) en .NET 8 y .NET Framework 4.8 (Mono y GitHub Actions/Windows).
- **F** = flujo completo con la lógica real del módulo contra un "Revit" en memoria (mock: no prueba la API).
- **L** = archivo validado con LibreOffice Calc (abre, y su copia re-guardada se reimporta con 0 cambios).
- **C** = compila en Windows (GitHub Actions) para Revit 2024, 2025 y 2026 contra las referencias reales de la API (paquetes Nice3point.Revit.Api), incluida la interfaz WPF.
- **R** = verificado dentro de Revit → **pendiente para todas las filas**: aquí no hay Revit. Rellenar con las pruebas guiadas (`GUIA.md §12`, `PROJECT_SETUP.md §7`).

## Excel Link

| Área | Función | Estado | Verificación | R |
|---|---|---|---|---|
| Arranque | Pestaña CausaFixIt, panel Data → Excel Link, Settings/About | Implementado | C | ☐ |
| Arranque | Registro de módulos (`ModuleRegistry`), ejemplo documentado | Implementado | — | ☐ |
| Arranque | Disponibilidad solo con documento de proyecto | Implementado | — | ☐ |
| Compatibilidad | Builds separados 2024 (net48) / 2025-2026 (net8-windows) | Implementado | T, C | ☐ |
| Compatibilidad | API de 64 bits (ElementId.Value); CreationGUID por reflexión | Implementado | C | ☐ |
| Ventana | WPF no modal, redimensionable, propietario = Revit, escalado | Implementado | C | ☐ |
| Ventana | ExternalEvent + cola (`RevitDispatcher`), sesión de documento | Implementado | C | ☐ |
| Ventana | Cierre/cambio de documento detectado | Implementado | S, C | ☐ |
| Pestañas | Model / Annotation categories: tres paneles, búsqueda, marcar todo, ocultar no marcados | Implementado | F, C | ☐ |
| Pestañas | Filtros Instance/Type/Read-only, Common only, añadir/quitar/ordenar | Implementado | F, C | ☐ |
| Pestañas | Colores + etiquetas (I, T, RO, T·RO, N/A) | Implementado | L (Excel), C | ☐ |
| Pestañas | Elements: familias/tipos, filtro de texto, fila por instancia o por tipo | Implementado | S, C | ☐ |
| Pestañas | Schedules: multi-selección, campos, schedules de la sheet activa | Implementado | F, S, C | ☐ |
| Pestañas | Spatial: Rooms/Spaces/Areas, estado de colocación, actualizar existentes | Implementado | C | ☐ |
| Pestañas | Spatial: crear/colocar | Pendiente | — | — |
| Alcance | Whole Model / Active View / Current Selection, con explicación y bloqueo donde no aplica | Implementado | C | ☐ |
| Vínculos | Hojas de solo lectura por vínculo; vínculos descargados informados | Implementado | T (bloqueo), C | ☐ |
| Vínculos | Filtro por vista del host aplicado a vínculos | Restringido (no se aplica; se informa) | — | — |
| Parámetros | Identidad por BIP / GUID / ParameterElement / familia (spec+nombre+ámbito) | Implementado | T, C | ☐ |
| Parámetros | Homónimos distinguidos | Implementado | T, F | ☐ |
| Parámetros | Editabilidad por celda según la capacidad real (IsReadOnly, grupos, worksharing). **UserModifiable ya no bloquea** (corrección 7766110) | Implementado | T, C | ☐ |
| Parámetros | String / Integer / Double / Yes-No | Implementado | T | ☐ |
| Parámetros | Referencias (ElementId: material, nivel…) editables con lista "Nombre [Id]"; nunca por nombre solo; cambio de tipo/familia = Unsupported | Implementado | T, C | ☐ |
| Unidades | Unidad de proyecto por columna, registrada en el libro; conversión UnitUtils | Implementado | T (con conversor simulado), C | ☐ |
| Unidades | Exportar→importar sin editar = 0 cambios (incl. 15 dígitos de Excel) | Implementado | T, L | ☐ |
| Unidades | Decimales regionales sin adivinar; ambigüedad = error | Implementado | T | ☐ |
| Excel | Libro .xlsx propio sin Office ni ClosedXML | Implementado | T, L | ☐ |
| Excel | Hoja Instructions, metadatos versionados, valores originales | Implementado | T, L | ☐ |
| Excel | Ids como texto (64 bits), nombres de hoja válidos/únicos | Implementado | T | ☐ |
| Excel | Encabezados congelados, filtros, colores, lista Yes/No | Implementado | L | ☐ |
| Excel | Ordenar filas / renombrar encabezados / mover columnas | Implementado | T | ☐ |
| Excel | Escritura atómica (temporal + reemplazo), archivo abierto/sin permisos | Implementado | T (Windows: bloqueo) | ☐ |
| Excel | Texto que empieza por "=" nunca se escribe como fórmula | Implementado | T, L | ☐ |
| Schedules | Data exchange (desglose por elemento, campos ligados a parámetros) | Implementado | C | ☐ |
| Schedules | Presentation export (solo lectura) | Implementado | T (no importable), C | ☐ |
| Schedules | Key / embebidos / filtrados por sheet / keynote → solo presentación | Restringido | C | ☐ |
| Schedules | Material takeoff: campos de material/cantidad | Restringido (excluidos en modo datos) | — | ☐ |
| Schedules | Panel schedules (PanelScheduleView) | Pendiente | — | — |
| Preview/Edit | Tabla virtualizada, búsqueda, filtros, ordenación | Implementado | F (lógica), C | ☐ |
| Preview/Edit | Copiar/pegar rangos, edición múltiple, Clear value, revertir | Implementado | F (lógica), C | ☐ |
| Preview/Edit | Apply Changes por el mismo motor que la importación | Implementado | F | ☐ |
| Preview/Edit | Seleccionar / zoom / aislar temporal / reset | Implementado | S, C | ☐ |
| Importación | Validar esquema y documento; confirmación de linaje | Implementado | T, F | ☐ |
| Importación | Comparación de tres estados; conflictos; consolidación por destino | Implementado | T, F | ☐ |
| Importación | Conflictos de tipo entre filas y hojas; instancias afectadas en todo el modelo | Implementado | T | ☐ |
| Importación | Filas borradas/nuevas, columnas nuevas, duplicados, IDs alterados, claves perdidas | Implementado | T | ☐ |
| Importación | Celda vacía ignorada; `<<CLEAR>>`; Clear value | Implementado | T, F | ☐ |
| Importación | Fórmulas: solo resultado guardado; sin resultado = bloqueo | Implementado | T | ☐ |
| Aplicación | Transacción atómica por defecto; "solo válidos" con SubTransaction | Implementado | F (semántica), C | ☐ |
| Aplicación | Revalidación justo antes de escribir; lectura de comprobación tras Set | Implementado | F, C | ☐ |
| Aplicación | Un solo Deshacer (TransactionGroup.Assimilate); sin guardar/sincronizar | Implementado | C | ☐ |
| Aplicación | Avisos de Revit no suprimidos; errores → rollback | Implementado | C | ☐ |
| Aplicación | Informe CSV; rollback nunca declarado como aplicado | Implementado | T, F | ☐ |
| Perfiles | JSON: crear, duplicar, renombrar, eliminar, importar, exportar | Implementado | T, F | ☐ |
| Perfiles | Referencias faltantes informadas, sin asociar por nombre | Implementado | T, F | ☐ |
| Consultas | Project Information (editable) | Implementado | C | ☐ |
| Consultas | Inventario de familias, Object Styles, Line Styles (solo lectura) | Implementado | C | ☐ |
| Rendimiento | Lectura por etapas con progreso y cancelación | Implementado | S, C | ☐ |
| Rendimiento | 10 000 × 20 medido en el núcleo (ver CHECKPOINT) | Medido solo sin Revit | T | ☐ |
| Instalación | Install/Uninstall por usuario, sin admin, sin duplicados | Implementado | no ejecutado (sin Windows) | ☐ |
| Instalación | build.ps1 → paquete por año | Implementado | no ejecutado (sin Windows/Revit) | ☐ |
| Integraciones | Google Sheets / Morta | Pendiente | — | — |
| Corrección | Columnas nunca bloqueadas por una muestra; el importador consulta el estado real de Revit antes de bloquear (causa del "(empty)" / "–") | Implementado | T, C | ☐ |
| Corrección | Esquema 2; libros de esquema 1 migrados (originales recuperados del texto) | Implementado | T | ☐ |
| Corrección | Una escritura por destino de tipo; TypeParameterConflict y error de grupo sin escritura parcial; "Multiple conflicting values" | Implementado | T | ☐ |
| Corrección | Estados precisos con acción y diagnóstico por fila + CSV de diagnóstico | Implementado | T | ☐ |

## Project Setup

| Área | Función | Estado | Verificación | R |
|---|---|---|---|---|
| Arranque | Botón Documentation → Project Setup, registrado como módulo (sin tocar Excel Link) | Implementado | C | ☐ |
| Ventana | WPF no modal: Levels, View Sets, Sheets, Numbering, Placement, Review; ExternalEvent; cierre de documento | Implementado | C | ☐ |
| Modos | Full Setup, Views Only, Sheets Only, Place / Re-align Existing, Update Titles Only | Implementado | T, C | ☐ |
| Niveles | Niveles del host con código propio, orden, vistas existentes por nivel; niveles faltantes informados | Implementado | T, F | ☐ |
| Niveles | Crear/copiar niveles o usar niveles de vínculos | Pendiente (fuera de alcance) | — | — |
| Vistas | Matriz niveles × reglas, subconjuntos, exclusión de filas, overrides por fila | Implementado | T | ☐ |
| Vistas | ViewPlan.Create en el nivel pedido: Floor Plan, RCP, Structural Plan si existe | Implementado | C | ☐ |
| Vistas | Template con IsValidViewTemplate; escala/scope box controlados por template → valor efectivo y aviso | Implementado | T (plan), C | ☐ |
| Vistas | Scope box / sin recorte / mantener recorte | Implementado | C | ☐ |
| Vistas | Copiar recorte simple de una vista de referencia | Pendiente | — | — |
| Vistas | Áreas, secciones, alzados, 3D | Pendiente (fuera de alcance) | — | — |
| Nombres | Build Name por destino (View Name, Title on Sheet, Sheet Name, Sheet Number); componentes con orden, Enabled, Required, separador, mayúsculas, relleno, mapeos, fallback; previsualización con filas reales | Implementado | T, F, C | ☐ |
| Nombres | Fuentes: campos CausaFixIt, valores del plan y parámetros reales (Project Information, Level, View, Sheet, Title Block tipo/instancia) por GUID / built-in / ParameterElement; homónimos distinguidos; solo lectura válido como fuente | Implementado | T, C | ☐ |
| Nombres | Separadores omitidos con componentes opcionales vacíos; ceros iniciales de códigos conservados; dependencias entre nombres y ciclos detectados | Implementado | T | ☐ |
| Nombres | Valores de vistas/sheets que aún no existen: desconocidos (fallback u opcional), sin usar valores del template | Implementado | T | ☐ |
| Nombres | Advanced pattern (tokens) y migración fiel de patrones antiguos; los no convertibles quedan Legacy/Advanced con nota | Implementado | T | ☐ |
| Nombres | Valores de parámetros de View/Sheet leídos bajo demanda (solo lectura) | Implementado | F (mock), C | ☐ |
| Numeración | Perfiles reutilizables con segmentos opcionales en cualquier orden; Standard: E-101 · E-A-101 · E-A-P-101 · FCC-E-A-P-101 | Implementado | T, F | ☐ |
| Numeración | Secuencia por sheet (no por vista), inicio/incremento/orden, reinicio por campos, contador compartido; números reservados al deseleccionar | Implementado | T | ☐ |
| Numeración | Cambio de regla/prefijo → renumeración pendiente (actual → propuesto) hasta aprobar; renumeración explícita con previsualización | Implementado | T, F | ☐ |
| Numeración | Identidad estable de sheet separada del número; mismo número en sheets distintos = conflicto (sin fusión) | Implementado | T | ☐ |
| Numeración | Condición documental separada de Phase / Phase Filter | Implementado | T | ☐ |
| Numeración | Perfiles: añadir, duplicar, renombrar, eliminar, importar, exportar | Implementado | T, F | ☐ |
| Nombres | Colisiones con existentes y entre filas; unicidad según Revit; intercambios con nombres temporales | Implementado | T, C | ☐ |
| Sheets | Un sheet por vista, sheet existente, ninguno; title block por regla/composición/fila; placeholders comprobados | Implementado | T, C | ☐ |
| Sheets | Views on Sheet: composiciones con miembros (View Set o vista existente), principal, Separate u Overlay | Implementado | T, F, C | ☐ |
| Sheets | Overlay: misma referencia del edificio en el mismo punto (Model Anchor), escala/rotación iguales, segunda referencia; solape intencional permitido y accidental detectado | Implementado | T (lógica y matemática), C | ☐ |
| Sheets | Títulos de overlay: Only primary / All / Per member mediante un tipo de viewport elegido; offsets por miembro | Implementado | T, C | ☐ |
| Sheets | Crear automáticamente un tipo de viewport derivado sin título | Pendiente (valores de Show Title no verificables en la API documentada) | — | — |
| Sheets | Orden delante/detrás de viewports superpuestos | Fuera de alcance (no expuesto por la API para viewports) | — | — |
| Sheets | Parámetros adicionales de sheet/title block mapeados por el usuario | Pendiente | — | — |
| Viewports | CanAddViewToSheet, reutilizar viewport existente, tipo, rotación; vista en otro sheet → dependiente explícita | Implementado | T, C | ☐ |
| Títulos | View Title Placement: KeepExisting, MatchReference (relativo o fijo), RelativeToViewport (esquina + mm, viewport o envolvente del overlay), FixedSheetPosition (sheet o title block); línea con longitud/dirección | Implementado | T (geometría con sonda simulada), C | ☐ |
| Títulos | Aplicación tras alinear con LabelLineLength → GetLabelOutline → LabelOffset → regenerar → verificar; viewports rotados; dibujo verificado sin moverse; restaurar si no hay tolerancia | Implementado | T, C | ☐ |
| Títulos | Niveles: perfil → grupo → composición → viewport (override en Review) | Implementado | T, F | ☐ |
| Títulos | Update Titles Only sobre viewports existentes (sin crear, renombrar ni realinear) | Implementado | T, C | ☐ |
| Alineación | Ancla por intersección de ejes / punto; segunda referencia; validación de documento | Implementado | T (lógica), C | ☐ |
| Alineación | Match Reference Viewport y X/Y manual; marco Sheet o TitleBlock con transformaciones reales | Implementado | T (matemática), C | ☐ |
| Alineación | GetModelToProjectionTransforms × GetProjectionToSheetTransform, SetBoxCenter, regenerar, error medido ≤ tolerancia | Implementado | T (bucle con sonda simulada), C | ☐ |
| Alineación | Escala/orientación diferente → "Anchor aligned; different scale/orientation" | Implementado | T | ☐ |
| Alineación | Split crop bloqueado; recorte no rectangular admitido (una región) | Implementado / Restringido | C | ☐ |
| Alineación | Viewport Center Placement (maquetación) | Implementado | T | ☐ |
| Alineación | Seguimiento automático permanente | Pendiente (fuera de alcance) | — | — |
| Ejecución | TransactionGroup atómico, un Deshacer, sin huérfanos, resultado honesto con rollback | Implementado | T (mock), C | ☐ |
| Ejecución | Reejecución sin cambios = 0 vistas/sheets/viewports y sin mover (Extensible Storage) | Implementado | T, F, C | ☐ |
| Ejecución | Viewports fijados (omitir o realinear restaurando el pin), worksharing | Implementado | C | ☐ |
| Ejecución | Cancelación durante la transacción | Restringido: solo antes de ejecutar o durante los picks | — | — |
| Review | Agrupada por sheet con miembros y papel; componentes de nombres/números con origen; numeración y título por sheet/viewport; previsualización (existente gris, estimado discontinuo, medido continuo; overlays y títulos) | Implementado | F, C | ☐ |
| Perfiles | Guardar, duplicar, renombrar, eliminar, importar, exportar; portabilidad sin emparejar por nombre; anclas no se copian | Implementado | T, F | ☐ |
| Perfiles | Formato 2 con migración del 1 (patrones, secuencias, sheets compartidos → composición con clave antigua, sin duplicados ni renumeración) | Implementado | T | ☐ |
| Perfiles | Portabilidad: parámetros de proyecto, tipos de viewport y vistas miembro de otro documento se informan para remapear | Implementado | T | ☐ |
| Rendimiento | Plan de 10 niveles × 4 conjuntos con 2000 vistas/500 sheets: ~10 ms (núcleo) | Medido sin Revit | T | ☐ |
| Rendimiento | Run Setup de 10 × 4 en Revit | Pendiente de medir en Revit | — | — |
