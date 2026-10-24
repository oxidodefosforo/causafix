using System;
using Autodesk.Revit.DB;
using CausaFix.Core.Model;

namespace CausaFix.Revit
{
    public static class DocumentIdentityReader
    {
        public static DocumentIdentity Read(Document doc)
        {
            var id = new DocumentIdentity
            {
                Title = doc.Title ?? string.Empty,
                PathName = doc.PathName ?? string.Empty,
                IsFamilyDocument = doc.IsFamilyDocument,
                IsWorkshared = doc.IsWorkshared,
                IsDetached = doc.IsDetached,
                CreationGuid = RevitCompat.CreationGuid(doc),
                ProjectInfoUniqueId = doc.ProjectInformation?.UniqueId ?? string.Empty
            };
            try
            {
                id.IsCloud = doc.IsModelInCloud;
                if (id.IsCloud)
                {
                    ModelPath cloud = doc.GetCloudModelPath();
                    id.CloudProjectGuid = cloud.GetProjectGUID().ToString("D");
                    id.CloudModelGuid = cloud.GetModelGUID().ToString("D");
                }
            }
            catch (Exception) { id.IsCloud = false; }

            if (doc.IsWorkshared && !doc.IsDetached)
            {
                try
                {
                    ModelPath central = doc.GetWorksharingCentralModelPath();
                    if (central != null) id.CentralPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(central) ?? string.Empty;
                }
                catch (Exception) { /* central not available */ }
            }
            return id;
        }

        /// <summary>Stable key of the open document for the session (used to detect document switches).</summary>
        public static string SessionKey(Document doc)
        {
            if (doc == null) return string.Empty;
            return (doc.ProjectInformation?.UniqueId ?? "") + "|" + RevitCompat.CreationGuid(doc) + "|" + (doc.PathName ?? "") + "|" + doc.Title;
        }
    }
}
