// COMPILE-CHECK STUBS – see RevitApiStubs.csproj. Signatures follow the Revit 2024–2026 API documentation.
using System;
using System.Collections;
using System.Collections.Generic;

#pragma warning disable CS1591

namespace Autodesk.Revit.Exceptions
{
    public class ApplicationException : System.Exception { }
    public class InvalidOperationException : ApplicationException { }
    public class ArgumentException : ApplicationException { }
}

namespace Autodesk.Revit.ApplicationServices
{
    public class Application
    {
        public string VersionNumber => throw new NotImplementedException();
    }
}

namespace Autodesk.Revit.DB
{
    internal static class X { public static Exception N() => new NotImplementedException("Revit API stub"); }

    public class APIObject : IDisposable
    {
        public virtual bool IsReadOnly => throw X.N();
        public void Dispose() { }
    }

    public class ElementId : IComparable<ElementId>
    {
        public ElementId(long id) { }
        public ElementId(BuiltInCategory c) { }
        public ElementId(BuiltInParameter p) { }
        public long Value => throw X.N();
        public static ElementId InvalidElementId => throw X.N();
        public int CompareTo(ElementId other) => throw X.N();
        public static bool operator ==(ElementId a, ElementId b) => throw X.N();
        public static bool operator !=(ElementId a, ElementId b) => throw X.N();
    }

    public class ForgeTypeId
    {
        public ForgeTypeId() { }
        public ForgeTypeId(string typeId) { }
        public string TypeId => throw X.N();
        public bool Empty() => throw X.N();
        public override bool Equals(object obj) => throw X.N();
        public override int GetHashCode() => throw X.N();
    }

    public static class SpecTypeId
    {
        public static class Boolean { public static ForgeTypeId YesNo => throw X.N(); }
        public static class Int { public static ForgeTypeId Integer => throw X.N(); }
    }

    public enum StorageType { None, Integer, Double, String, ElementId }
    public enum CategoryType { Invalid, Model, Annotation, Internal, AnalyticalModel }
    public enum GraphicsStyleType { Projection, Cut }
    public enum BuiltInCategory { INVALID = -1, OST_Rooms = -2000160, OST_MEPSpaces = -2003600, OST_Areas = -2003200, OST_Sheets = -2003100, OST_Views = -2000279, OST_Lines = -2000051, OST_ProjectInformation = -2003101 }
    public enum BuiltInParameter { INVALID = -1, ALL_MODEL_MARK = -1001203, ELEM_TYPE_PARAM = -1002052, ELEM_FAMILY_AND_TYPE_PARAM = -1002051, ELEM_FAMILY_PARAM = -1002053, SYMBOL_ID_PARAM = -1002050 }
    public enum ViewType { Undefined, FloorPlan, CeilingPlan, Elevation, ThreeD, Schedule, DrawingSheet, ProjectBrowser, Report, DraftingView, Legend, SystemBrowser, EngineeringPlan, AreaPlan, Section, Detail, CostReport, LoadsReport, PresureLossReport, ColumnSchedule, PanelSchedule, Walkthrough, Rendering, SystemsAnalysisReport, Internal }
    public enum TemporaryViewMode { RevealHiddenElements, TemporaryHideIsolate, WorksharingDisplay, RevealConstraints, TemporaryViewProperties }
    public enum CheckoutStatus { OwnedByCurrentUser, OwnedByOtherUser, NotOwned }
    public enum ModelUpdatesStatus { CurrentWithCentral, NotYetInCentral, UpdatedInCentral, DeletedInCentral }
    public enum TransactionStatus { Uninitialized, Started, RolledBack, Committed, Pending, Error, Proceed }
    public enum FailureSeverity { None, Warning, Error, DocumentCorruption }
    public enum FailureProcessingResult { Continue, ProceedWithRollBack, ProceedWithCommit, WaitForUserInput }
    public enum SectionType { None = -1, Header = 0, Body = 1, Summary = 2, Footer = 3 }
    public enum ScheduleFieldType { Instance, ElementType, Count, ViewBased, Formula, Percentage, Room, FromRoom, ToRoom, ProjectInfo, Material, MaterialQuantity, RevitLinkInstance, RevitLinkType, StructuralMaterial, Space }

    public class Color : APIObject
    {
        public bool IsValid => throw X.N();
        public byte Red => throw X.N();
        public byte Green => throw X.N();
        public byte Blue => throw X.N();
    }

    public class Definition : APIObject
    {
        public string Name => throw X.N();
        public ForgeTypeId GetDataType() => throw X.N();
    }

    public class InternalDefinition : Definition
    {
        public BuiltInParameter BuiltInParameter => throw X.N();
        public bool VariesAcrossGroups => throw X.N();
    }

    public class Parameter : APIObject
    {
        public Definition Definition => throw X.N();
        public bool IsShared => throw X.N();
        public Guid GUID => throw X.N();
        public ElementId Id => throw X.N();
        public StorageType StorageType => throw X.N();
        public override bool IsReadOnly => throw X.N();
        public bool UserModifiable => throw X.N();
        public bool HasValue => throw X.N();
        public string AsString() => throw X.N();
        public int AsInteger() => throw X.N();
        public double AsDouble() => throw X.N();
        public ElementId AsElementId() => throw X.N();
        public string AsValueString() => throw X.N();
        public bool Set(string v) => throw X.N();
        public bool Set(int v) => throw X.N();
        public bool Set(double v) => throw X.N();
        public bool Set(ElementId v) => throw X.N();
        public bool ClearValue() => throw X.N();
    }

    public class ParameterSet : APIObject, IEnumerable
    {
        public IEnumerator GetEnumerator() => throw X.N();
    }

    public class CategoryNameMap : APIObject, IEnumerable
    {
        public IEnumerator GetEnumerator() => throw X.N();
    }

    public class Categories : APIObject, IEnumerable
    {
        public IEnumerator GetEnumerator() => throw X.N();
    }

    public class Settings : APIObject
    {
        public Categories Categories => throw X.N();
    }

    public class Material : Element { }

    public class Category : APIObject
    {
        public ElementId Id => throw X.N();
        public string Name => throw X.N();
        public Category Parent => throw X.N();
        public CategoryType CategoryType => throw X.N();
        public CategoryNameMap SubCategories => throw X.N();
        public Color LineColor => throw X.N();
        public Material Material => throw X.N();
        public int? GetLineWeight(GraphicsStyleType t) => throw X.N();
        public ElementId GetLinePatternId(GraphicsStyleType t) => throw X.N();
        public static Category GetCategory(Document doc, ElementId id) => throw X.N();
        public static Category GetCategory(Document doc, BuiltInCategory c) => throw X.N();
    }

    public class Location : APIObject { }

    public class Element : IDisposable
    {
        public ElementId Id => throw X.N();
        public string UniqueId => throw X.N();
        public Category Category => throw X.N();
        public virtual string Name { get => throw X.N(); set => throw X.N(); }
        public Document Document => throw X.N();
        public ElementId GroupId => throw X.N();
        public Location Location => throw X.N();
        public ParameterSet Parameters => throw X.N();
        public ElementId GetTypeId() => throw X.N();
        public Parameter get_Parameter(BuiltInParameter p) => throw X.N();
        public Parameter get_Parameter(Guid g) => throw X.N();
        public Parameter get_Parameter(Definition d) => throw X.N();
        public IList<Parameter> GetOrderedParameters() => throw X.N();
        public void Dispose() { }
    }

    public class ElementType : Element
    {
        public string FamilyName => throw X.N();
    }

    public class FamilySymbol : ElementType
    {
        public Family Family => throw X.N();
    }

    public class Instance : Element { }

    public class FamilyInstance : Instance
    {
        public FamilySymbol Symbol => throw X.N();
    }

    public class Family : Element
    {
        public Category FamilyCategory => throw X.N();
        public bool IsInPlace => throw X.N();
        public bool IsEditable => throw X.N();
        public ISet<ElementId> GetFamilySymbolIds() => throw X.N();
    }

    public class ProjectInfo : Element { }
    public class LinePatternElement : Element { public static ElementId GetSolidPatternId() => throw X.N(); }

    public class ParameterElement : Element
    {
        public InternalDefinition GetDefinition() => throw X.N();
    }

    public class SharedParameterElement : ParameterElement
    {
        public Guid GuidValue => throw X.N();
    }

    public class SpatialElement : Element
    {
        public double Area => throw X.N();
    }

    public class View : Element
    {
        public ViewType ViewType => throw X.N();
        public bool IsTemplate => throw X.N();
        public bool CanUseTemporaryVisibilityModes() => throw X.N();
        public void IsolateElementsTemporary(ICollection<ElementId> ids) => throw X.N();
        public void DisableTemporaryViewMode(TemporaryViewMode mode) => throw X.N();
    }

    public class ViewSheet : View { }

    public class TableSectionData : APIObject
    {
        public int FirstRowNumber => throw X.N();
        public int FirstColumnNumber => throw X.N();
        public int NumberOfRows => throw X.N();
        public int NumberOfColumns => throw X.N();
    }

    public class TableData : APIObject
    {
        public TableSectionData GetSectionData(SectionType t) => throw X.N();
    }

    public class TableView : View
    {
        public TableData GetTableData() => throw X.N();
        public string GetCellText(SectionType sectionType, int row, int column) => throw X.N();
    }

    public class ScheduleFieldId { }

    public class ScheduleField : APIObject
    {
        public bool IsHidden => throw X.N();
        public bool IsCalculatedField => throw X.N();
        public bool IsCombinedParameterField => throw X.N();
        public ScheduleFieldType FieldType => throw X.N();
        public string ColumnHeading => throw X.N();
        public ElementId ParameterId => throw X.N();
        public string GetName() => throw X.N();
    }

    public class ScheduleDefinition : APIObject
    {
        public bool IsItemized => throw X.N();
        public bool IsFilteredBySheet => throw X.N();
        public bool HasEmbeddedSchedule => throw X.N();
        public bool IncludeLinkedFiles => throw X.N();
        public bool IsKeySchedule => throw X.N();
        public bool IsMaterialTakeoff => throw X.N();
        public bool ShowHeaders => throw X.N();
        public ElementId CategoryId => throw X.N();
        public ElementId FamilyId => throw X.N();
        public int GetFieldCount() => throw X.N();
        public ScheduleField GetField(int index) => throw X.N();
        public ScheduleField GetField(ScheduleFieldId id) => throw X.N();
        public IList<ScheduleFieldId> GetFieldOrder() => throw X.N();
    }

    public class ViewSchedule : TableView
    {
        public ScheduleDefinition Definition => throw X.N();
        public bool IsTitleblockRevisionSchedule => throw X.N();
        public bool IsInternalKeynoteSchedule => throw X.N();
    }

    public class ScheduleSheetInstance : Element
    {
        public ElementId ScheduleId => throw X.N();
        public bool IsTitleblockRevisionSchedule => throw X.N();
    }

    public class RevitLinkInstance : Instance
    {
        public Document GetLinkDocument() => throw X.N();
    }

    public class ModelPath : APIObject
    {
        public Guid GetProjectGUID() => throw X.N();
        public Guid GetModelGUID() => throw X.N();
    }

    public static class ModelPathUtils
    {
        public static string ConvertModelPathToUserVisiblePath(ModelPath p) => throw X.N();
    }

    public class FormatOptions : APIObject
    {
        public ForgeTypeId GetUnitTypeId() => throw X.N();
        public ForgeTypeId GetSymbolTypeId() => throw X.N();
    }

    public class Units : APIObject
    {
        public FormatOptions GetFormatOptions(ForgeTypeId specTypeId) => throw X.N();
    }

    public static class UnitUtils
    {
        public static double ConvertToInternalUnits(double value, ForgeTypeId unitTypeId) => throw X.N();
        public static double ConvertFromInternalUnits(double value, ForgeTypeId unitTypeId) => throw X.N();
        public static bool IsUnit(ForgeTypeId typeId) => throw X.N();
        public static bool IsMeasurableSpec(ForgeTypeId specTypeId) => throw X.N();
    }

    public static class LabelUtils
    {
        public static string GetLabelForUnit(ForgeTypeId unitTypeId) => throw X.N();
        public static string GetLabelForSymbol(ForgeTypeId symbolTypeId) => throw X.N();
        public static string GetLabelForSpec(ForgeTypeId specTypeId) => throw X.N();
    }

    public class WorksharingTooltipInfo : APIObject
    {
        public string Owner => throw X.N();
    }

    public static class WorksharingUtils
    {
        public static CheckoutStatus GetCheckoutStatus(Document doc, ElementId id) => throw X.N();
        public static ModelUpdatesStatus GetModelUpdatesStatus(Document doc, ElementId id) => throw X.N();
        public static WorksharingTooltipInfo GetWorksharingTooltipInfo(Document doc, ElementId id) => throw X.N();
    }

    public class Document : APIObject
    {
        public string Title => throw X.N();
        public string PathName => throw X.N();
        public bool IsFamilyDocument => throw X.N();
        public bool IsWorkshared => throw X.N();
        public bool IsDetached => throw X.N();
        public bool IsModelInCloud => throw X.N();
        public bool IsModifiable => throw X.N();
        public override bool IsReadOnly => throw X.N();
        public ProjectInfo ProjectInformation => throw X.N();
        public Settings Settings => throw X.N();
        public Element GetElement(ElementId id) => throw X.N();
        public Element GetElement(string uniqueId) => throw X.N();
        public ModelPath GetCloudModelPath() => throw X.N();
        public ModelPath GetWorksharingCentralModelPath() => throw X.N();
        public Units GetUnits() => throw X.N();
    }

    public abstract class ElementFilter : APIObject { }
    public class ElementMulticategoryFilter : ElementFilter { public ElementMulticategoryFilter(ICollection<ElementId> ids) { } }
    public class ElementCategoryFilter : ElementFilter { public ElementCategoryFilter(ElementId id) { } }

    public class FilteredElementCollector : IEnumerable<Element>
    {
        public FilteredElementCollector(Document doc) { }
        public FilteredElementCollector(Document doc, ElementId viewId) { }
        public FilteredElementCollector(Document doc, ICollection<ElementId> ids) { }
        public FilteredElementCollector WhereElementIsNotElementType() => throw X.N();
        public FilteredElementCollector WhereElementIsElementType() => throw X.N();
        public FilteredElementCollector WherePasses(ElementFilter f) => throw X.N();
        public FilteredElementCollector OfClass(Type t) => throw X.N();
        public IEnumerator<Element> GetEnumerator() => throw X.N();
        IEnumerator IEnumerable.GetEnumerator() => throw X.N();
    }

    public class FailureMessageAccessor : IDisposable
    {
        public FailureSeverity GetSeverity() => throw X.N();
        public string GetDescriptionText() => throw X.N();
        public void Dispose() { }
    }

    public class FailuresAccessor : IDisposable
    {
        public IList<FailureMessageAccessor> GetFailureMessages() => throw X.N();
        public void Dispose() { }
    }

    public interface IFailuresPreprocessor
    {
        FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor);
    }

    public class FailureHandlingOptions : IDisposable
    {
        public FailureHandlingOptions SetFailuresPreprocessor(IFailuresPreprocessor p) => throw X.N();
        public FailureHandlingOptions SetClearAfterRollback(bool v) => throw X.N();
        public void Dispose() { }
    }

    public class Transaction : IDisposable
    {
        public Transaction(Document doc, string name) { }
        public TransactionStatus Start() => throw X.N();
        public TransactionStatus Commit() => throw X.N();
        public TransactionStatus RollBack() => throw X.N();
        public FailureHandlingOptions GetFailureHandlingOptions() => throw X.N();
        public void SetFailureHandlingOptions(FailureHandlingOptions o) => throw X.N();
        public void Dispose() { }
    }

    public class SubTransaction : IDisposable
    {
        public SubTransaction(Document doc) { }
        public TransactionStatus Start() => throw X.N();
        public TransactionStatus Commit() => throw X.N();
        public TransactionStatus RollBack() => throw X.N();
        public void Dispose() { }
    }

    public class TransactionGroup : IDisposable
    {
        public TransactionGroup(Document doc, string name) { }
        public TransactionStatus Start() => throw X.N();
        public TransactionStatus Assimilate() => throw X.N();
        public TransactionStatus RollBack() => throw X.N();
        public void Dispose() { }
    }
}

namespace Autodesk.Revit.UI.Selection
{
    using Autodesk.Revit.DB;
    public class Selection
    {
        public ICollection<ElementId> GetElementIds() => throw new NotImplementedException();
        public void SetElementIds(ICollection<ElementId> ids) => throw new NotImplementedException();
    }
}

namespace Autodesk.Revit.UI
{
    using Autodesk.Revit.DB;

    public class UIDocument
    {
        public Document Document => throw new NotImplementedException();
        public View ActiveView => throw new NotImplementedException();
        public Selection.Selection Selection => throw new NotImplementedException();
        public void ShowElements(ICollection<ElementId> ids) => throw new NotImplementedException();
    }

    public class UIApplication
    {
        public UIDocument ActiveUIDocument => throw new NotImplementedException();
        public Autodesk.Revit.ApplicationServices.Application Application => throw new NotImplementedException();
    }

    public enum ExternalEventRequest { Accepted, Pending, Denied, TimedOut }

    public interface IExternalEventHandler
    {
        void Execute(UIApplication app);
        string GetName();
    }

    public class ExternalEvent : IDisposable
    {
        public static ExternalEvent Create(IExternalEventHandler handler) => throw new NotImplementedException();
        public ExternalEventRequest Raise() => throw new NotImplementedException();
        public bool IsPending => throw new NotImplementedException();
        public void Dispose() { }
    }
}
