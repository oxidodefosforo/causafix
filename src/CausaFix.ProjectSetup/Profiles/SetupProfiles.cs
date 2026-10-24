using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using CausaFix.ProjectSetup.Model;

namespace CausaFix.ProjectSetup.Profiles
{
    public static class SetupJson
    {
        private static DataContractJsonSerializer Serializer() => new DataContractJsonSerializer(typeof(SetupConfig),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public static string Serialize(SetupConfig c)
        {
            using (var ms = new MemoryStream())
            {
                Serializer().WriteObject(ms, c);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static SetupConfig Deserialize(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var c = (SetupConfig)Serializer().ReadObject(ms);
                if (c == null) throw new InvalidDataException("Empty profile.");
                if (c.Format > SetupConfig.CurrentFormat) throw new InvalidDataException("Profile format " + c.Format + " is newer than this add-in supports.");
                if (string.IsNullOrWhiteSpace(c.Name)) throw new InvalidDataException("Profile has no name.");
                return c;
            }
        }

        public static SetupConfig Clone(SetupConfig c) => Deserialize(Serialize(c));
    }

    /// <summary>What must be mapped when a profile is applied to another document.</summary>
    public sealed class ProfileMappingReport
    {
        public List<string> MissingLevels { get; } = new List<string>();
        public List<string> MissingViewTypes { get; } = new List<string>();
        public List<string> MissingTemplates { get; } = new List<string>();
        public List<string> MissingTitleBlocks { get; } = new List<string>();
        public List<string> MissingScopeBoxes { get; } = new List<string>();
        public List<string> AnchorsReset { get; } = new List<string>();
        public bool Complete => MissingLevels.Count + MissingViewTypes.Count + MissingTemplates.Count + MissingTitleBlocks.Count + MissingScopeBoxes.Count + AnchorsReset.Count == 0;

        public override string ToString()
        {
            var sb = new StringBuilder();
            void L(string title, List<string> items) { if (items.Count > 0) sb.AppendLine(title + ": " + string.Join(", ", items)); }
            L("Levels to map", MissingLevels);
            L("View types to choose", MissingViewTypes);
            L("View templates to choose", MissingTemplates);
            L("Title blocks to choose", MissingTitleBlocks);
            L("Scope boxes to choose", MissingScopeBoxes);
            L("Model anchors to pick again (anchors are never copied between documents)", AnchorsReset);
            return sb.ToString().TrimEnd();
        }
    }

    public static class ProfilePortability
    {
        /// <summary>
        /// Prepares a profile for the current document: project references that do not exist here are cleared
        /// (names are kept as search hints), and anchors from another document are reset. Never maps by name.
        /// </summary>
        public static ProfileMappingReport Prepare(SetupConfig c, SetupCatalog cat, string documentKey)
        {
            var r = new ProfileMappingReport();
            foreach (var l in c.Levels)
            {
                if (cat.Level(l.LevelUniqueId) != null) continue;
                r.MissingLevels.Add(l.LevelName);
                l.Selected = false;
            }
            foreach (var v in c.ViewSets)
            {
                if (!string.IsNullOrEmpty(v.ViewFamilyTypeUniqueId) && cat.ViewType(v.ViewFamilyTypeUniqueId) == null) { r.MissingViewTypes.Add(v.Name); v.ViewFamilyTypeUniqueId = string.Empty; }
                if (!string.IsNullOrEmpty(v.TemplateUniqueId) && cat.Template(v.TemplateUniqueId) == null) { r.MissingTemplates.Add(v.Name); v.TemplateUniqueId = string.Empty; }
                if (!string.IsNullOrEmpty(v.TitleBlockUniqueId) && cat.TitleBlock(v.TitleBlockUniqueId) == null) { r.MissingTitleBlocks.Add(v.Name); v.TitleBlockUniqueId = string.Empty; }
                if (!string.IsNullOrEmpty(v.ScopeBoxUniqueId) && !cat.ScopeBoxes.Any(s => s.UniqueId == v.ScopeBoxUniqueId)) { r.MissingScopeBoxes.Add(v.Name); v.ScopeBoxUniqueId = string.Empty; }
                v.LevelUniqueIds.RemoveAll(u => cat.Level(u) == null);
            }
            foreach (var g in c.Groups)
            {
                if (g.Anchor.IsSet && g.Anchor.DocumentKey != documentKey)
                {
                    r.AnchorsReset.Add(g.Key);
                    g.Anchor = new ModelAnchor();
                }
            }
            if (c.DocumentKey != documentKey)
            {
                // Overrides, sequence numbers and existing-element choices belong to the source document.
                c.Overrides.Clear();
                c.SequenceAssignments.Clear();
                c.Placements.Clear();
            }
            c.DocumentKey = documentKey;
            return r;
        }
    }

    /// <summary>One JSON file per profile under %AppData%\CausaFix\ProjectSetup\Profiles.</summary>
    public sealed class SetupProfileStore
    {
        public string Folder { get; }

        public SetupProfileStore(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(folder);
        }

        public List<string> Names(List<string> errors = null)
        {
            var list = new List<string>();
            foreach (var f in Directory.GetFiles(Folder, "*.json"))
            {
                try { list.Add(SetupJson.Deserialize(File.ReadAllText(f, Encoding.UTF8)).Name); }
                catch (Exception ex) { errors?.Add(Path.GetFileName(f) + ": " + ex.Message); }
            }
            return list.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public bool Exists(string name) => File.Exists(PathFor(name));

        public void Save(SetupConfig c)
        {
            Core.Profiles.ProfileStore.ValidateName(c.Name);
            string path = PathFor(c.Name), tmp = path + ".tmp";
            File.WriteAllText(tmp, SetupJson.Serialize(c), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null, true); else File.Move(tmp, path);
        }

        public SetupConfig Load(string name) => SetupJson.Deserialize(File.ReadAllText(PathFor(name), Encoding.UTF8));

        public void Delete(string name) { if (File.Exists(PathFor(name))) File.Delete(PathFor(name)); }

        public void Rename(string oldName, string newName)
        {
            if (Exists(newName) && !string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)) throw new IOException("A profile named '" + newName + "' already exists.");
            var c = Load(oldName);
            c.Name = newName;
            Delete(oldName);
            Save(c);
        }

        public void Duplicate(string name, string newName)
        {
            if (Exists(newName)) throw new IOException("A profile named '" + newName + "' already exists.");
            var c = Load(name);
            c.Name = newName;
            Save(c);
        }

        public SetupConfig Import(string file)
        {
            var c = SetupJson.Deserialize(File.ReadAllText(file, Encoding.UTF8));
            Save(c);
            return c;
        }

        public void Export(string name, string file) => File.WriteAllText(file, SetupJson.Serialize(Load(name)), new UTF8Encoding(false));

        private string PathFor(string name) => Path.Combine(Folder, "setup-" + Core.Profiles.ProfileStore.FileNameFor(name));
    }
}
