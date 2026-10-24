using System;
using System.Collections.Generic;

namespace CausaFix.Core.Model
{
    /// <summary>
    /// Identity of a Revit document as captured at export time.
    ///
    /// Strategy (documented in docs/IDENTIDAD_DOCUMENTO.md):
    ///  1. Cloud models: cloud model GUID (+ project GUID) is authoritative.
    ///  2. Otherwise: Document.CreationGUID (when the API exposes it) + ProjectInformation.UniqueId.
    ///  3. Location (central path or local path) only distinguishes "same location" from "same lineage, other location".
    /// Name and path alone never verify identity.
    /// Known limits: a Save As copy, a detached model or a file copy share CreationGUID and UniqueIds with
    /// the original — CausaFix can tell they belong to the same lineage but cannot tell copies apart; the
    /// user must confirm explicitly when location differs.
    /// </summary>
    public sealed class DocumentIdentity
    {
        public string Title { get; set; } = string.Empty;
        public string PathName { get; set; } = string.Empty;
        public bool IsCloud { get; set; }
        public string CloudProjectGuid { get; set; } = string.Empty;
        public string CloudModelGuid { get; set; } = string.Empty;
        public bool IsWorkshared { get; set; }
        public string CentralPath { get; set; } = string.Empty;
        public bool IsDetached { get; set; }
        /// <summary>Document.CreationGUID when available; empty otherwise.</summary>
        public string CreationGuid { get; set; } = string.Empty;
        public string ProjectInfoUniqueId { get; set; } = string.Empty;
        public bool IsFamilyDocument { get; set; }

        public IEnumerable<KeyValuePair<string, string>> ToPairs()
        {
            yield return new KeyValuePair<string, string>("doc.title", Title);
            yield return new KeyValuePair<string, string>("doc.path", PathName);
            yield return new KeyValuePair<string, string>("doc.isCloud", IsCloud ? "1" : "0");
            yield return new KeyValuePair<string, string>("doc.cloudProject", CloudProjectGuid);
            yield return new KeyValuePair<string, string>("doc.cloudModel", CloudModelGuid);
            yield return new KeyValuePair<string, string>("doc.isWorkshared", IsWorkshared ? "1" : "0");
            yield return new KeyValuePair<string, string>("doc.central", CentralPath);
            yield return new KeyValuePair<string, string>("doc.isDetached", IsDetached ? "1" : "0");
            yield return new KeyValuePair<string, string>("doc.creationGuid", CreationGuid);
            yield return new KeyValuePair<string, string>("doc.projectInfoUid", ProjectInfoUniqueId);
            yield return new KeyValuePair<string, string>("doc.isFamily", IsFamilyDocument ? "1" : "0");
        }

        public static DocumentIdentity FromPairs(IDictionary<string, string> map)
        {
            string g(string k) => map.TryGetValue(k, out string v) ? (v ?? string.Empty) : string.Empty;
            return new DocumentIdentity
            {
                Title = g("doc.title"),
                PathName = g("doc.path"),
                IsCloud = g("doc.isCloud") == "1",
                CloudProjectGuid = g("doc.cloudProject"),
                CloudModelGuid = g("doc.cloudModel"),
                IsWorkshared = g("doc.isWorkshared") == "1",
                CentralPath = g("doc.central"),
                IsDetached = g("doc.isDetached") == "1",
                CreationGuid = g("doc.creationGuid"),
                ProjectInfoUniqueId = g("doc.projectInfoUid"),
                IsFamilyDocument = g("doc.isFamily") == "1"
            };
        }
    }

    public enum IdentityMatch
    {
        /// <summary>Same model and same location. Import may proceed.</summary>
        Verified,
        /// <summary>Same lineage (copy, Save As, detached, moved). Requires explicit user confirmation.</summary>
        SameLineageDifferentLocation,
        /// <summary>Lineage cannot be proven with the data available. Requires explicit user confirmation.</summary>
        Unverifiable,
        /// <summary>Different model. Import is blocked.</summary>
        Mismatch
    }

    public sealed class IdentityResult
    {
        public IdentityMatch Match { get; set; }
        public List<string> Reasons { get; } = new List<string>();
        public bool RequiresConfirmation => Match == IdentityMatch.SameLineageDifferentLocation || Match == IdentityMatch.Unverifiable;
        public bool Blocked => Match == IdentityMatch.Mismatch;
    }

    public static class DocumentIdentityMatcher
    {
        public static IdentityResult Compare(DocumentIdentity exported, DocumentIdentity current)
        {
            var r = new IdentityResult();
            if (exported == null || current == null)
            {
                r.Match = IdentityMatch.Mismatch;
                r.Reasons.Add("Document identity missing from the workbook.");
                return r;
            }

            // 1. Cloud models
            if (exported.IsCloud && current.IsCloud)
            {
                if (Eq(exported.CloudModelGuid, current.CloudModelGuid) && !string.IsNullOrEmpty(exported.CloudModelGuid))
                {
                    r.Match = IdentityMatch.Verified;
                    r.Reasons.Add("Cloud model GUID matches.");
                }
                else
                {
                    r.Match = IdentityMatch.Mismatch;
                    r.Reasons.Add("Cloud model GUID differs: the workbook was exported from another cloud model.");
                }
                return r;
            }

            bool hasCreation = !string.IsNullOrEmpty(exported.CreationGuid) && !string.IsNullOrEmpty(current.CreationGuid);
            bool projInfoEq = !string.IsNullOrEmpty(exported.ProjectInfoUniqueId) && Eq(exported.ProjectInfoUniqueId, current.ProjectInfoUniqueId);

            if (hasCreation && !Eq(exported.CreationGuid, current.CreationGuid))
            {
                r.Match = IdentityMatch.Mismatch;
                r.Reasons.Add("Document creation GUID differs: different model.");
                return r;
            }
            if (!projInfoEq)
            {
                r.Match = IdentityMatch.Mismatch;
                r.Reasons.Add("Project Information element differs: different model.");
                return r;
            }

            bool sameLocation = false;
            if (exported.IsWorkshared && current.IsWorkshared && !string.IsNullOrEmpty(exported.CentralPath))
                sameLocation = PathEq(exported.CentralPath, current.CentralPath);
            else if (!string.IsNullOrEmpty(exported.PathName))
                sameLocation = PathEq(exported.PathName, current.PathName);

            if (exported.IsCloud != current.IsCloud)
                r.Reasons.Add(exported.IsCloud ? "Exported from a cloud model; current document is local (detached or downloaded copy?)."
                                               : "Exported from a local model; current document is a cloud model.");
            if (current.IsDetached && !exported.IsDetached)
                r.Reasons.Add("Current document is detached from central.");

            if (hasCreation)
            {
                r.Reasons.Add("Creation GUID and Project Information match (same model lineage).");
                if (sameLocation && exported.IsCloud == current.IsCloud && !current.IsDetached)
                {
                    r.Match = IdentityMatch.Verified;
                }
                else
                {
                    r.Match = IdentityMatch.SameLineageDifferentLocation;
                    r.Reasons.Add("File location differs (Save As, copy, detached or moved). Copies cannot be told apart; confirm this is the intended model.");
                }
                return r;
            }

            // No creation GUID available: Project Information UniqueId alone is weak (projects created
            // from the same template can share it).
            r.Reasons.Add("Creation GUID unavailable; only Project Information and location could be compared.");
            if (sameLocation)
            {
                r.Match = IdentityMatch.Unverifiable;
                r.Reasons.Add("Same location and Project Information. Lineage cannot be fully proven; confirm before importing.");
            }
            else
            {
                r.Match = IdentityMatch.Unverifiable;
                r.Reasons.Add("Different location. Projects created from the same template can share Project Information; confirm before importing.");
            }
            return r;
        }

        private static bool Eq(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

        private static bool PathEq(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string n(string p) => p.Replace('/', '\\').TrimEnd('\\');
            return string.Equals(n(a), n(b), StringComparison.OrdinalIgnoreCase);
        }
    }
}
