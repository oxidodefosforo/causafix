# Arquitectura y decisiones técnicas

## Proyectos

```
CausaFix.sln
├─ src/CausaFix.Core          net48 + net8.0   Reglas sin Revit: claves, valores, unidades, comparación,
│                                               lector/escritor .xlsx, esquema del libro, perfiles, informes.
├─ src/CausaFix.ExcelLink     net48 + net8.0   View models y casos de uso del módulo (sin WPF ni Revit).
├─ src/CausaFix.ProjectSetup  net48 + net8.0   Project Setup: configuración, nombres, plan, alineación, perfiles, view models.
├─ src/CausaFix.Revit         net48 | net8-win Adaptadores de Revit, ExternalEvent, sesión, aplicación.
├─ src/CausaFix.Addin         net48 | net8-win IExternalApplication, ribbon, ModuleRegistry, WPF.
├─ tests/CausaFix.Core.Tests  net8.0 + net48   Ejecutable de pruebas (sin dependencias).
└─ tools/RevitApiStubs        solo CI           Firmas de la API para compilar sin Revit. No se distribuye.
```

`Directory.Build.props` fija el runtime por año: `-p:RevitVersion=2024` → net48; `2025`/`2026` → net8.0-windows. RevitAPI/RevitAPIUI se referencian desde la instalación de Revit de cada año con `Private=false`.

## Por qué un lector/escritor .xlsx propio (y no ClosedXML)

- En este entorno nuget.org no era accesible: no podía fijar ni verificar versiones de ClosedXML y sus dependencias (DocumentFormat.OpenXml, SixLabors.Fonts, ExcelNumberFormat…).
- En Revit 2024 (.NET Framework, un solo AppDomain) las versiones de DocumentFormat.OpenXml de distintos add-ins chocan con frecuencia. El aislamiento de dependencias de Revit 2026 es opcional y no existe en 2024/2025.
- Lo que Excel Link necesita es acotado (cadenas compartidas, números, booleanos, estilos, paneles congelados, filtros, validación de lista, hojas ocultas, lectura de fórmulas con resultado guardado) y se cubre con `System.IO.Compression` + `System.Xml`, ambos en .NET Framework 4.8 y .NET 8.
- Se valida con pruebas y con LibreOffice Calc (apertura y re-guardado sin cambios). Limitación: no escribe gráficos, formato condicional ni protección de hoja (no se necesitan). Si se prefiere ClosedXML más adelante, el cambio queda acotado a `CausaFix.Core/Xlsx`.

## Modelo de hilos

- La ventana es no modal. Toda llamada a la API pasa por `RevitDispatcher` (`IExternalEventHandler` + cola). Estar en el hilo de la UI no es un contexto válido de API.
- El dispatcher devuelve `Task<T>` con DTOs; nunca salen `Document`, `Element` ni `Parameter`.
- `Task.Run` solo para leer/escribir archivos y procesar DTOs (exportar, leer el libro, parsear).
- Las lecturas grandes se dividen en lotes de 400 elementos: entre lotes la UI actualiza el progreso y atiende Cancelar. Las transacciones nunca cruzan lotes.
- `RevitSession` vuelve a comprobar en cada llamada que el documento activo es el de la ventana.
- Las conversiones de unidades para la UI usan un `AffineUnitConverter` capturado en contexto de API (todas las conversiones de Revit son afines), así la UI no llama a la API.
- La ventana de revisión es no modal: un diálogo modal bloquearía el bucle de mensajes que Revit usa para los ExternalEvent.

## Identidad de parámetros

`ParamKey` (texto estable, guardado en el libro y en perfiles):

| Origen | Clave | Ejemplo |
|---|---|---|
| Built-in | valor entero de `BuiltInParameter` (estable aunque el nombre del enum cambie, p. ej. en 2026) | `BIP:-1010106@I` |
| Compartido | GUID | `SP:9f1c…@T` |
| De proyecto no compartido | id del `ParameterElement` (propio del documento) | `PP:123456@I` |
| De familia no compartido | spec + nombre (Revit no ofrece otra identidad entre familias); si en un elemento hay dos iguales → ambiguo y bloqueado | `FP:autodesk.spec.aec:length-2.0.0:Width@I` |
| Contexto | nombre | `PS:Category` |

`@I` / `@T` = instancia / tipo. Nunca se usa `LookupParameter(nombre)`.

## Identidad del documento

Se guarda: título, ruta, nube (GUID de proyecto y modelo), central, detached, `Document.CreationGUID` (si la API lo ofrece) y UniqueId de Project Information.

| Situación | Resultado |
|---|---|
| Nube y mismo GUID de modelo | Verificado |
| CreationGUID distinto, o Project Information distinto | **Bloqueado** (otro modelo) |
| Mismo CreationGUID + Project Information y misma ubicación | Verificado |
| Mismo linaje, otra ubicación (Save As, copia, detached, movido) | Requiere confirmación explícita |
| Sin CreationGUID disponible | Requiere confirmación (Project Information puede repetirse en proyectos de la misma plantilla) |

Limitación: las copias de un mismo modelo comparten CreationGUID y UniqueIds; CausaFix sabe que son del mismo linaje pero no puede distinguirlas. Por eso pide confirmación cuando la ubicación difiere. Las filas se emparejan siempre por UniqueId, nunca por ElementId.

## Comparación de tres estados

Por celda: O = original exportado, P = propuesto, C = actual en Revit.

- P vacío → sin propuesta (vacío nunca se convierte en cero ni en borrado).
- P = O → sin cambio; se conserva C aunque haya cambiado.
- P ≠ O y C = O → Change.
- P ≠ O y C = P → Already in model.
- P ≠ O y C distinto de ambos → Conflict (no se escribe por defecto).
- Varias propuestas modificadas distintas para el mismo destino → Conflicting proposals.

Dobles: tolerancia relativa 1e-10 en unidades internas (muy por debajo de cualquier precisión visible), suficiente para absorber conversiones y los 15 dígitos de Excel. Textos: CRLF = LF.

## Aplicación

`TransactionGroup` → `Transaction` (→ `SubTransaction` por cambio en modo "solo válidos") → `Assimilate` (un Deshacer). Antes de cada escritura: el elemento existe, el parámetro se resuelve, sigue siendo editable y su valor actual es el visto en la revisión. Después de `Set` se relee el valor: si Revit guardó otro (redondeo/restricción), cuenta como fallo. Errores de Revit → rollback; avisos → se dejan a Revit. Nunca guarda ni sincroniza.
