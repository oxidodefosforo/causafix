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
    public class OperationCanceledException : ApplicationException { }
}

namespace Autodesk.Revit.ApplicationServices
{
    public class Application
    {
        public string VersionNumber => throw new NotImplementedException();
        public string VersionBuild => throw new NotImplementedException();
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
        public static ForgeTypeId Length => throw X.N();
    }

    public static class UnitFormatUtils
    {
        public static string Format(Units units, ForgeTypeId specTypeId, double value, bool forEditing) => throw X.N();
    }

    public enum StorageType { None, Integer, Double, String, ElementId }
    public enum ViewFamily { Invalid, ThreeDimensional, Walkthrough, ImageView, Schedule, CostReport, LoadsReport, PressureLossReport, Legend, PanelSchedule, GraphicalColumnSchedule, StructuralPlan, AreaPlan, Detail, Section, Elevation, Drafting, Sheet, CeilingPlan, FloorPlan, SystemsAnalysisReport }
    public enum ViewDuplicateOption { Duplicate, AsDependent, WithDetailing }
    public enum ViewportRotation { None, Clockwise, Counterclockwise }
    public enum ElementTypeGroup { ViewportType }

    public class XYZ
    {
        public XYZ(double x, double y, double z) { }
        public double X => throw X_.N(); public double Y => throw X_.N(); public double Z => throw X_.N();
        public static XYZ Zero => throw X_.N();
        public static XYZ operator -(XYZ a, XYZ b) => throw X_.N();
        public static XYZ operator +(XYZ a, XYZ b) => throw X_.N();
        public double GetLength() => throw X_.N();
    }
    internal static class X_ { public static Exception N() => new NotImplementedException(); }

    public class Transform : APIObject
    {
        public XYZ Origin => throw X.N(); public XYZ BasisX => throw X.N(); public XYZ BasisY => throw X.N();
        public XYZ OfPoint(XYZ p) => throw X.N();
        public Transform Multiply(Transform right) => throw X.N();
    }

    public class Outline : APIObject
    {
        public XYZ MinimumPoint => throw X.N(); public XYZ MaximumPoint => throw X.N();
    }

    public class CurveLoop { }

    public class Reference { public ElementId ElementId => throw X.N(); }

    public class TransformWithBoundary : IDisposable
    {
        public Transform GetModelToProjectionTransform() => throw X.N();
        public CurveLoop GetBoundary() => throw X.N();
        public void Dispose() { }
    }

    public class ViewCropRegionShapeManager : APIObject { public bool ShapeSet => throw X.N(); }

    public class Curve : APIObject { public XYZ GetEndPoint(int i) => throw X.N(); }
    public enum CategoryType { Invalid, Model, Annotation, Internal, AnalyticalModel }
    public enum GraphicsStyleType { Projection, Cut }
    public enum BuiltInCategory { INVALID = -1, OST_TitleBlocks = -2000280, OST_VolumeOfInterest = -2006000, OST_Viewports = -2000510, OST_Rooms = -2000160, OST_MEPSpaces = -2003600, OST_Areas = -2003200, OST_Sheets = -2003100, OST_Views = -2000279, OST_Lines = -2000051, OST_ProjectInformation = -2003101 }
    public enum BuiltInParameter { INVALID = -1, VIEW_SCALE_PULLDOWN_METRIC = -1005150, VIEW_SCALE = -1005151, VIEW_DISCIPLINE = -1005152, VIEWER_VOLUME_OF_INTEREST_CROP = -1005153, VIEWER_CROP_REGION = -1005154, SHEET_WIDTH = -1005155, SHEET_HEIGHT = -1005156, VIEW_DESCRIPTION = -1005157, ALL_MODEL_MARK = -1001203, ELEM_TYPE_PARAM = -1002052, ELEM_FAMILY_AND_TYPE_PARAM = -1002051, ELEM_FAMILY_PARAM = -1002053, SYMBOL_ID_PARAM = -1002050 }
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
        public ElementId Id => throw X.N();
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
        public Element Element => throw X.N();
        public ElementId GetAssociatedGlobalParameter() => throw X.N();
    }

    public abstract class Binding : APIObject { }
    public abstract class ElementBinding : Binding { }
    public class InstanceBinding : ElementBinding { }
    public class TypeBinding : ElementBinding { }

    public class DefinitionBindingMapIterator : APIObject
    {
        public Definition Key => throw X.N();
        public object Current => throw X.N();
        public bool MoveNext() => throw X.N();
        public void Reset() => throw X.N();
    }

    public class DefinitionBindingMap : APIObject
    {
        public DefinitionBindingMapIterator ForwardIterator() => throw X.N();
    }

    public class BindingMap : DefinitionBindingMap { }

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
        public bool Pinned { get => throw X.N(); set => throw X.N(); }
        public ElementId OwnerViewId => throw X.N();
        public ElementId ChangeTypeId(ElementId typeId) => throw X.N();
        public Autodesk.Revit.DB.ExtensibleStorage.Entity GetEntity(Autodesk.Revit.DB.ExtensibleStorage.Schema schema) => throw X.N();
        public void SetEntity(Autodesk.Revit.DB.ExtensibleStorage.Entity entity) => throw X.N();
        public void Dispose() { }
    }

    public class Level : Element { public double Elevation => throw X.N(); }

    public class Grid : Element { public Curve Curve => throw X.N(); public bool IsCurved => throw X.N(); }

    public class SketchPlane : Element { }

    public class ViewFamilyType : ElementType { public ViewFamily ViewFamily => throw X.N(); }

    public class ElementType : Element
    {
        public string FamilyName => throw X.N();
    }

    public class FamilySymbol : ElementType
    {
        public Family Family => throw X.N();
        public bool IsActive => throw X.N();
        public void Activate() => throw X.N();
    }

    public class Instance : Element { public Transform GetTransform() => throw X.N(); }

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

    public class ProjectInfo : Element { public string Number => throw X.N(); }
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
        public int Scale { get => throw X.N(); set => throw X.N(); }
        public ElementId ViewTemplateId { get => throw X.N(); set => throw X.N(); }
        public bool IsValidViewTemplate(ElementId templateId) => throw X.N();
        public ICollection<ElementId> GetTemplateParameterIds() => throw X.N();
        public ICollection<ElementId> GetNonControlledTemplateParameterIds() => throw X.N();
        public ElementId GetPrimaryViewId() => throw X.N();
        public bool CanViewBeDuplicated(ViewDuplicateOption o) => throw X.N();
        public ElementId Duplicate(ViewDuplicateOption o) => throw X.N();
        public bool CropBoxActive { get => throw X.N(); set => throw X.N(); }
        public ViewCropRegionShapeManager GetCropRegionShapeManager() => throw X.N();
        public IList<TransformWithBoundary> GetModelToProjectionTransforms() => throw X.N();
        public SketchPlane SketchPlane => throw X.N();
    }

    public class ViewPlan : View
    {
        public static ViewPlan Create(Document doc, ElementId viewFamilyTypeId, ElementId levelId) => throw X.N();
        public Level GenLevel => throw X.N();
    }

    public class ViewSheet : View
    {
        public static ViewSheet Create(Document doc, ElementId titleBlockTypeId) => throw X.N();
        public string SheetNumber { get => throw X.N(); set => throw X.N(); }
        public bool IsPlaceholder => throw X.N();
        public ISet<ElementId> GetAllPlacedViews() => throw X.N();
    }

    public class Viewport : Element
    {
        public static Viewport Create(Document doc, ElementId sheetId, ElementId viewId, XYZ point) => throw X.N();
        public static bool CanAddViewToSheet(Document doc, ElementId sheetId, ElementId viewId) => throw X.N();
        public ElementId ViewId => throw X.N();
        public ElementId SheetId => throw X.N();
        public XYZ GetBoxCenter() => throw X.N();
        public void SetBoxCenter(XYZ p) => throw X.N();
        public Outline GetBoxOutline() => throw X.N();
        public Transform GetProjectionToSheetTransform() => throw X.N();
        public ViewportRotation Rotation { get => throw X.N(); set => throw X.N(); }
        public XYZ LabelOffset { get => throw X.N(); set => throw X.N(); }
        public double LabelLineLength { get => throw X.N(); set => throw X.N(); }
        public Outline GetLabelOutline() => throw X.N();
    }

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
        public ElementId GetDefaultElementTypeId(ElementTypeGroup g) => throw X.N();
        public void Regenerate() => throw X.N();
        public BindingMap ParameterBindings => throw X.N();
        public Autodesk.Revit.ApplicationServices.Application Application => throw X.N();
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
        public FilteredElementCollector OfCategory(BuiltInCategory c) => throw X.N();
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
        public string GetName() => throw X.N();
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

namespace Autodesk.Revit.DB.ExtensibleStorage
{
    using System;
    public enum AccessLevel { Public, Vendor, Application }
    public class Schema
    {
        public static Schema Lookup(Guid g) => throw new NotImplementedException();
    }
    public class FieldBuilder { }
    public class SchemaBuilder
    {
        public SchemaBuilder(Guid g) { }
        public SchemaBuilder SetSchemaName(string n) => throw new NotImplementedException();
        public SchemaBuilder SetDocumentation(string d) => throw new NotImplementedException();
        public SchemaBuilder SetReadAccessLevel(AccessLevel a) => throw new NotImplementedException();
        public SchemaBuilder SetWriteAccessLevel(AccessLevel a) => throw new NotImplementedException();
        public FieldBuilder AddSimpleField(string name, Type t) => throw new NotImplementedException();
        public Schema Finish() => throw new NotImplementedException();
    }
    public class Entity
    {
        public Entity(Schema s) { }
        public bool IsValid() => throw new NotImplementedException();
        public T Get<T>(string field) => throw new NotImplementedException();
        public void Set<T>(string field, T value) => throw new NotImplementedException();
    }
}

namespace Autodesk.Revit.UI.Selection
{
    using Autodesk.Revit.DB;
    public enum ObjectType { Nothing, Element, PointOnElement, Edge, Face, LinkedElement, Subelement }
    public interface ISelectionFilter
    {
        bool AllowElement(Element elem);
        bool AllowReference(Reference reference, XYZ position);
    }
    public class Selection
    {
        public Reference PickObject(ObjectType t, ISelectionFilter f, string prompt) => throw new NotImplementedException();
        public XYZ PickPoint(string prompt) => throw new NotImplementedException();
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
        public void ShowElements(ElementId id) => throw new NotImplementedException();
        public void RequestViewChange(View v) => throw new NotImplementedException();
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
