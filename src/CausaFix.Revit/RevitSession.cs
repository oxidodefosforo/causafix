using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CausaFix.Revit
{
    public sealed class DocumentChangedException : Exception
    {
        public DocumentChangedException(string message) : base(message) { }
    }

    /// <summary>
    /// The document a tool window is bound to. Every API request re-validates that the active document is
    /// still that one; if the user switched or closed it, the request fails with a clear message instead
    /// of operating on the wrong model.
    /// </summary>
    public sealed class RevitSession
    {
        public string DocumentKey { get; private set; }
        public string DocumentTitle { get; private set; }

        public void Bind(Document doc)
        {
            DocumentKey = DocumentIdentityReader.SessionKey(doc);
            DocumentTitle = doc?.Title ?? string.Empty;
        }

        public bool IsBound => !string.IsNullOrEmpty(DocumentKey);

        public Document Require(UIApplication app)
        {
            UIDocument uidoc = app?.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null) throw new DocumentChangedException("No document is open in Revit.");
            if (doc.IsFamilyDocument) throw new DocumentChangedException("Excel Link works on project documents, not families.");
            if (!IsBound) Bind(doc);
            if (DocumentIdentityReader.SessionKey(doc) != DocumentKey)
                throw new DocumentChangedException("The active document is now '" + doc.Title + "', not '" + DocumentTitle +
                                                   "'. Switch back, or press Refresh to work with the active document.");
            return doc;
        }

        public UIDocument RequireUi(UIApplication app)
        {
            Require(app);
            return app.ActiveUIDocument;
        }
    }
}
