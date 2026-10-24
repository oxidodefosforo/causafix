namespace CausaFix.Core.Model
{
    /// <summary>Names of CausaFix context columns (ParamKey.Pseudo).</summary>
    public static class PseudoColumns
    {
        public const string ElementId = "ElementId";
        public const string Document = "Document";
        public const string Category = "Category";
        public const string Family = "Family";
        public const string Type = "Type";
        public const string Level = "Level";
        public const string InstanceCount = "InstanceCount";
        public const string Status = "Status";

        public static ColumnDef Column(string name, string header = null) => new ColumnDef
        {
            Key = ParamKey.Pseudo(name),
            Header = header ?? name,
            Kind = ValueKind.Context,
            ColumnReadOnly = true,
            ReadOnlyReason = "Context column.",
            DataTypeLabel = "Context"
        };
    }
}
