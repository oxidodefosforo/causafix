# Cómo añadir una herramienta (módulo) a CausaFix

CausaFix separa el arranque/ribbon, los servicios compartidos y cada herramienta. Una herramienta nueva **no modifica el motor de Excel Link**: solo añade su carpeta y una línea de registro.

## Piezas de la solución

| Proyecto | Contiene | Depende de |
|---|---|---|
| `CausaFix.Core` | Modelos y reglas independientes de Revit (claves, valores, comparación, .xlsx, perfiles, informes, configuración) | — |
| `CausaFix.Revit` | Adaptadores de la API de Revit, `RevitDispatcher` (ExternalEvent), sesión de documento | Core, RevitAPI |
| `CausaFix.ExcelLink` | Lógica del módulo Excel Link (view models y casos de uso), sin WPF ni Revit | Core |
| `CausaFix.ProjectSetup` | Lógica de Project Setup: configuración, nombres, plan, alineación, perfiles, view models | Core |
| `CausaFix.Addin` | `App` (IExternalApplication), `ModuleRegistry`, servicios WPF compartidos, vistas de cada módulo | todos |

Servicios compartidos que un módulo puede usar (`CausaFix.Addin.Shared`): `Services.Paths`, `Services.Log`, `Services.LoadSettings()`, `Services.SetOwner(window, app)`, `WpfDialogs`, `Icons.Create(...)`, `Theme.xaml`, y `RevitDispatcher` + `RevitSession` (en `CausaFix.Revit`) para ventanas no modales.

## Ejemplo real: Project Setup

Project Setup se añadió así: proyecto de lógica `CausaFix.ProjectSetup` (sin WPF/Revit, con pruebas), adaptadores en `CausaFix.Revit/ProjectSetup`, vistas y `ProjectSetupModule` en `CausaFix.Addin/Modules/ProjectSetup`, y una línea en `ModuleRegistry.Modules`. Excel Link no se modificó.

## Pasos

1. **Carpeta del módulo:** `src/CausaFix.Addin/Modules/<Herramienta>/`.
2. **Comando:** una clase `IExternalCommand` con `[Transaction(TransactionMode.Manual)]`.
3. **Módulo:** una clase que implemente `ICausaFixModule` con su panel y botones (`CommandSpec`).
4. **Registro:** añade una línea en `ModuleRegistry.Modules` (`src/CausaFix.Addin/Bootstrap/ModuleRegistry.cs`).
5. **Lógica:** si es más que unas líneas, crea un proyecto `CausaFix.<Herramienta>` sin WPF/Revit (como `CausaFix.ExcelLink`) para poder probarlo, y adaptadores en `CausaFix.Revit` si necesita la API.
6. **Pruebas:** añade pruebas en `tests/` para las reglas independientes de Revit.
7. **Documenta** el módulo y actualiza `docs/MATRIZ.md`.

No hay que tocar `App.cs`, el instalador ni el manifiesto: todos los módulos viven en `CausaFix.Addin.dll`.

## Ejemplo mínimo (documentación; no está registrado en el producto)

```csharp
// src/CausaFix.Addin/Modules/ViewAudit/ViewAuditModule.cs
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CausaFix.Addin.Bootstrap;

namespace CausaFix.Addin.Modules.ViewAudit
{
    public sealed class ViewAuditModule : ICausaFixModule
    {
        public string Id => "CausaFix.ViewAudit";
        public string Panel => "Audit";                     // panel nuevo en la pestaña CausaFix
        public IEnumerable<CommandSpec> Commands => new[]
        {
            new CommandSpec
            {
                Name = "CausaFix.ViewAudit",
                Text = "View\nAudit",
                Tooltip = "Counts views without a sheet.",
                CommandType = typeof(ViewAuditCommand),
                AvailabilityType = typeof(ProjectDocumentAvailability),
                IconGlyph = "VA",
                IconColor = "#5B4B8A"
            }
        };
    }

    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ViewAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            Document doc = data.Application.ActiveUIDocument.Document;
            int count = 0;
            foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)))
            {
                if (v.IsTemplate || v is ViewSheet || v is ViewSchedule) continue;
                string sheet = v.get_Parameter(BuiltInParameter.VIEWER_SHEET_NUMBER)?.AsString();
                if (string.IsNullOrEmpty(sheet) || sheet == "---") count++;
            }
            TaskDialog.Show("View Audit", count + " view(s) are not placed on a sheet.");
            return Result.Succeeded;
        }
    }
}
```

Registro:

```csharp
public static readonly IReadOnlyList<ICausaFixModule> Modules = new ICausaFixModule[]
{
    new ExcelLinkModule(),
    new ProjectSetupModule(),
    new ViewAuditModule(),   // ← única línea añadida
    new SuiteModule()
};
```

## Ventanas no modales

Si la herramienta abre una ventana que sigue abierta mientras se trabaja en Revit:

- Crea el `RevitDispatcher` dentro de `Execute` del comando (ExternalEvent exige contexto de API).
- Haz **todas** las llamadas a la API con `dispatcher.InvokeAsync(app => …)` y devuelve DTOs. Estar en el hilo de la UI no basta.
- No pases `Document`, `Element`, `Parameter` ni colecciones diferidas a `Task.Run`.
- Usa `RevitSession.Require(app)` para comprobar que el documento activo sigue siendo el de la ventana.
- Divide trabajos largos en varias llamadas (etapas) para mostrar progreso y permitir cancelar; nunca dejes una transacción abierta entre llamadas.
- Abre las ventanas de confirmación que deban aplicar cambios como **no modales** (un diálogo modal bloquea el bucle de mensajes que Revit usa para los ExternalEvent).
- Libera el dispatcher (`Dispose`) al cerrar la ventana.
