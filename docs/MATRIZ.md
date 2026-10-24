# Matriz de funciones – CausaFix Excel Link 0.1.0

Estado: **Implementado** · **Restringido** (implementado con un límite deliberado) · **Pendiente** (no incluido en 0.1.0).

Verificación disponible en esta entrega:
- **T** = pruebas automáticas que pasan (50/50) en .NET 8 y .NET Framework 4.8 (Mono).
- **F** = flujo completo probado con la lógica real del módulo contra un "Revit" en memoria (`FakeGateway`).
- **L** = archivo validado con LibreOffice Calc (abre, convierte, y su copia re-guardada se reimporta con 0 cambios).
- **S** = el código de Revit compila para 2024 (net48) y 2025/2026 (net8) contra stubs escritos desde la documentación de la API. No es una prueba en Revit.
- **W** = interfaz WPF escrita, **no compilada** en este entorno (sin Windows); XAML validado solo como XML bien formado.
- **R** = verificado dentro de Revit → **pendiente para todas las filas**: aquí no había Revit. Rellenar con la prueba guiada (`GUIA.md §12`).

| Área | Función | Estado | Verificación | R |
|---|---|---|---|---|
| Arranque | Pestaña CausaFix, panel Data → Excel Link, Settings/About | Implementado | S (API de ribbon no incluida en stubs), W | ☐ |
| Arranque | Registro de módulos (`ModuleRegistry`), ejemplo documentado | Implementado | — | ☐ |
| Arranque | Disponibilidad solo con documento de proyecto | Implementado | — | ☐ |
| Compatibilidad | Builds separados 2024 (net48) / 2025-2026 (net8-windows) | Implementado | T, S | ☐ |
| Compatibilidad | API de 64 bits (ElementId.Value); CreationGUID por reflexión | Implementado | S | ☐ |
| Ventana | WPF no modal, redimensionable, propietario = Revit, escalado | Implementado | W | ☐ |
| Ventana | ExternalEvent + cola (`RevitDispatcher`), sesión de documento | Implementado | S | ☐ |
| Ventana | Cierre/cambio de documento detectado | Implementado | S, W | ☐ |
| Pestañas | Model / Annotation categories: tres paneles, búsqueda, marcar todo, ocultar no marcados | Implementado | F, W | ☐ |
| Pestañas | Filtros Instance/Type/Read-only, Common only, añadir/quitar/ordenar | Implementado | F, W | ☐ |
| Pestañas | Colores + etiquetas (I, T, RO, T·RO, N/A) | Implementado | L (Excel), W | ☐ |
| Pestañas | Elements: familias/tipos, filtro de texto, fila por instancia o por tipo | Implementado | S, W | ☐ |
| Pestañas | Schedules: multi-selección, campos, schedules de la sheet activa | Implementado | F, S, W | ☐ |
| Pestañas | Spatial: Rooms/Spaces/Areas, estado de colocación, actualizar existentes | Implementado | S | ☐ |
| Pestañas | Spatial: crear/colocar | Pendiente | — | — |
| Alcance | Whole Model / Active View / Current Selection, con explicación y bloqueo donde no aplica | Implementado | S | ☐ |
| Vínculos | Hojas de solo lectura por vínculo; vínculos descargados informados | Implementado | T (bloqueo), S | ☐ |
| Vínculos | Filtro por vista del host aplicado a vínculos | Restringido (no se aplica; se informa) | — | — |
| Parámetros | Identidad por BIP / GUID / ParameterElement / familia (spec+nombre+ámbito) | Implementado | T, S | ☐ |
| Parámetros | Homónimos distinguidos | Implementado | T, F | ☐ |
| Parámetros | Editabilidad por celda (IsReadOnly, UserModifiable, grupos, worksharing, enumerados) | Implementado | T (motor), S | ☐ |
| Parámetros | String / Integer / Double / Yes-No | Implementado | T | ☐ |
| Parámetros | Referencias (ElementId: material, nivel…) | Restringido: se muestran por nombre, solo lectura | T | ☐ |
| Unidades | Unidad de proyecto por columna, registrada en el libro; conversión UnitUtils | Implementado | T (con conversor simulado), S | ☐ |
| Unidades | Exportar→importar sin editar = 0 cambios (incl. 15 dígitos de Excel) | Implementado | T, L | ☐ |
| Unidades | Decimales regionales sin adivinar; ambigüedad = error | Implementado | T | ☐ |
| Excel | Libro .xlsx propio sin Office ni ClosedXML | Implementado | T, L | ☐ |
| Excel | Hoja Instructions, metadatos versionados, valores originales | Implementado | T, L | ☐ |
| Excel | Ids como texto (64 bits), nombres de hoja válidos/únicos | Implementado | T | ☐ |
| Excel | Encabezados congelados, filtros, colores, lista Yes/No | Implementado | L | ☐ |
| Excel | Ordenar filas / renombrar encabezados / mover columnas | Implementado | T | ☐ |
| Excel | Escritura atómica (temporal + reemplazo), archivo abierto/sin permisos | Implementado | T (Windows: bloqueo) | ☐ |
| Excel | Texto que empieza por "=" nunca se escribe como fórmula | Implementado | T, L | ☐ |
| Schedules | Data exchange (desglose por elemento, campos ligados a parámetros) | Implementado | S | ☐ |
| Schedules | Presentation export (solo lectura) | Implementado | T (no importable), S | ☐ |
| Schedules | Key / embebidos / filtrados por sheet / keynote → solo presentación | Restringido | S | ☐ |
| Schedules | Material takeoff: campos de material/cantidad | Restringido (excluidos en modo datos) | — | ☐ |
| Schedules | Panel schedules (PanelScheduleView) | Pendiente | — | — |
| Preview/Edit | Tabla virtualizada, búsqueda, filtros, ordenación | Implementado | F (lógica), W | ☐ |
| Preview/Edit | Copiar/pegar rangos, edición múltiple, Clear value, revertir | Implementado | F (lógica), W | ☐ |
| Preview/Edit | Apply Changes por el mismo motor que la importación | Implementado | F | ☐ |
| Preview/Edit | Seleccionar / zoom / aislar temporal / reset | Implementado | S, W | ☐ |
| Importación | Validar esquema y documento; confirmación de linaje | Implementado | T, F | ☐ |
| Importación | Comparación de tres estados; conflictos; consolidación por destino | Implementado | T, F | ☐ |
| Importación | Conflictos de tipo entre filas y hojas; instancias afectadas en todo el modelo | Implementado | T | ☐ |
| Importación | Filas borradas/nuevas, columnas nuevas, duplicados, IDs alterados, claves perdidas | Implementado | T | ☐ |
| Importación | Celda vacía ignorada; `<<CLEAR>>`; Clear value | Implementado | T, F | ☐ |
| Importación | Fórmulas: solo resultado guardado; sin resultado = bloqueo | Implementado | T | ☐ |
| Aplicación | Transacción atómica por defecto; "solo válidos" con SubTransaction | Implementado | F (semántica), S | ☐ |
| Aplicación | Revalidación justo antes de escribir; lectura de comprobación tras Set | Implementado | F, S | ☐ |
| Aplicación | Un solo Deshacer (TransactionGroup.Assimilate); sin guardar/sincronizar | Implementado | S | ☐ |
| Aplicación | Avisos de Revit no suprimidos; errores → rollback | Implementado | S | ☐ |
| Aplicación | Informe CSV; rollback nunca declarado como aplicado | Implementado | T, F | ☐ |
| Perfiles | JSON: crear, duplicar, renombrar, eliminar, importar, exportar | Implementado | T, F | ☐ |
| Perfiles | Referencias faltantes informadas, sin asociar por nombre | Implementado | T, F | ☐ |
| Consultas | Project Information (editable) | Implementado | S | ☐ |
| Consultas | Inventario de familias, Object Styles, Line Styles (solo lectura) | Implementado | S | ☐ |
| Rendimiento | Lectura por etapas con progreso y cancelación | Implementado | S, W | ☐ |
| Rendimiento | 10 000 × 20 medido en el núcleo (ver CHECKPOINT) | Medido solo sin Revit | T | ☐ |
| Instalación | Install/Uninstall por usuario, sin admin, sin duplicados | Implementado | no ejecutado (sin Windows) | ☐ |
| Instalación | build.ps1 → paquete por año | Implementado | no ejecutado (sin Windows/Revit) | ☐ |
| Integraciones | Google Sheets / Morta | Pendiente | — | — |
