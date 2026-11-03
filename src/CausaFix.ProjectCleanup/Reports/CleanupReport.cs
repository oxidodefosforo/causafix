using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CausaFix.ProjectCleanup.Execution;
using CausaFix.ProjectCleanup.Model;
using CausaFix.ProjectCleanup.Planning;

namespace CausaFix.ProjectCleanup.Reports
{
    /// <summary>Local CSV report of a cleanup (UTF-8 with BOM, opens in Excel). Same folder and style as the Excel Link reports.</summary>
    public static class CleanupReport
    {
        public static string Write(string folder, CleanupPlan plan, CleanupResult result)
        {
            Directory.CreateDirectory(folder);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string title = plan?.Catalog?.DocumentTitle ?? result?.DocumentTitle ?? "model";
            string safe = new string(title.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c).ToArray());
            string path = Path.Combine(folder, "CausaFixIt-ProjectCleanup-" + safe + "-" + stamp + ".csv");
            File.WriteAllText(path, Build(plan, result), new UTF8Encoding(true));
            return path;
        }

        public static string KindLabel(DocKind k)
        {
            switch (k)
            {
                case DocKind.Sheet: return "Plano";
                case DocKind.Schedule: return "Schedule";
                case DocKind.Legend: return "Leyenda";
                case DocKind.PanelSchedule: return "Panel schedule";
                case DocKind.CadLink: return "CAD vinculado";
                case DocKind.CadImport: return "CAD importado";
                case DocKind.RevitLink: return "Vínculo Revit";
                case DocKind.Image: return "Imagen";
                default: return "Vista";
            }
        }

        public static string OutcomeLabel(ItemOutcome o)
        {
            switch (o)
            {
                case ItemOutcome.Deleted: return "Eliminada";
                case ItemOutcome.DeletedWithParent: return "Eliminada con su primaria/padre";
                case ItemOutcome.Failed: return "Error";
                case ItemOutcome.RolledBack: return "Revertida (no eliminada)";
                case ItemOutcome.Preserved: return "Conservada";
                default: return "No intentada";
            }
        }

        public static string Build(CleanupPlan plan, CleanupResult r)
        {
            var sb = new StringBuilder();
            void Row(params string[] cols) => sb.AppendLine(string.Join(",", cols.Select(Csv)));
            Row("CausaFixIt Project Cleanup – informe");
            Row("Proyecto", r.DocumentTitle);
            Row("Se conservó", r.KeptView);
            Row("Revit", r.RevitVersion, "Add-in", r.AddinBuild);
            Row("Inicio (UTC)", r.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Row("Resultado", r.Committed ? "Aplicado (un solo Deshacer en Revit)" : "NO aplicado: no se eliminó nada", "Transacción", r.TransactionStatus, "Grupo", r.GroupStatus);
            if (!string.IsNullOrEmpty(r.Failure)) Row("Error", r.Failure);
            Row("Resumen", r.Summary);
            sb.AppendLine();
            Row("Eliminado", "Vistas", r.Deleted(DocKind.View).ToString(CultureInfo.InvariantCulture), "Planos", r.Deleted(DocKind.Sheet).ToString(CultureInfo.InvariantCulture),
                "Schedules", r.Deleted(DocKind.Schedule).ToString(CultureInfo.InvariantCulture), "Leyendas", r.Deleted(DocKind.Legend).ToString(CultureInfo.InvariantCulture),
                "Panel schedules", r.Deleted(DocKind.PanelSchedule).ToString(CultureInfo.InvariantCulture),
                "CAD", (r.Deleted(DocKind.CadLink) + r.Deleted(DocKind.CadImport)).ToString(CultureInfo.InvariantCulture),
                "Vínculos Revit", r.Deleted(DocKind.RevitLink).ToString(CultureInfo.InvariantCulture), "Imágenes", r.Deleted(DocKind.Image).ToString(CultureInfo.InvariantCulture));
            if (plan != null) Row("Opciones", "CAD " + (plan.Options.DeleteCad ? "sí" : "no"), "Vínculos Revit " + (plan.Options.DeleteRevitLinks ? "sí" : "no"), "Imágenes " + (plan.Options.DeleteImages ? "sí" : "no"));
            foreach (var d in r.Dependents.OrderByDescending(x => x.Value)) Row("Dependiente eliminado", d.Key, d.Value.ToString(CultureInfo.InvariantCulture));
            foreach (var c in r.ClosedViews) Row("Ventana cerrada", c);
            sb.AppendLine();
            Row("Resultado", "Clase", "Tipo", "Número", "Nombre", "Motivo / mensaje");
            foreach (var i in r.Items.OrderBy(i => i.Outcome == ItemOutcome.Preserved ? 0 : 1))
                Row(OutcomeLabel(i.Outcome), KindLabel(i.Kind), i.TypeLabel, i.SheetNumber, i.Name, i.Message);
            if (plan != null)
            {
                if (plan.Catalog.Excluded.Count > 0)
                {
                    sb.AppendLine();
                    Row("No es documentación eliminable (no se tocó)");
                    foreach (var x in plan.Catalog.Excluded) Row(x.TypeLabel, x.Name, x.Reason);
                }
                if (plan.Consequences.Count > 0 || plan.Notes.Count > 0)
                {
                    sb.AppendLine();
                    Row("Análisis previo");
                    foreach (var c in plan.Consequences) Row("Consecuencia", c);
                    foreach (var n in plan.Notes) Row("Nota", n);
                }
            }
            if (r.RevitMessages.Count > 0)
            {
                sb.AppendLine();
                Row("Mensajes de Revit");
                foreach (var m in r.RevitMessages) Row(m);
            }
            return sb.ToString();
        }

        private static string Csv(string s)
        {
            s = s ?? string.Empty;
            bool negativeNumber = s.Length > 1 && s[0] == '-' && (char.IsDigit(s[1]) || s[1] == '.');
            if (s.Length > 0 && (s[0] == '=' || s[0] == '+' || s[0] == '@' || (s[0] == '-' && !negativeNumber))) s = "'" + s;
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
