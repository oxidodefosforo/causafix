using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CausaFix.Core.Profiles
{
    [DataContract]
    public sealed class ProfileItem
    {
        /// <summary>Technical key (category id / ParamKey / schedule UniqueId). Governs matching.</summary>
        [DataMember(Order = 1)] public string Key { get; set; }
        /// <summary>Name at save time — shown to the user when the key is missing; never used to match.</summary>
        [DataMember(Order = 2)] public string Name { get; set; }

        public ProfileItem() { }
        public ProfileItem(string key, string name) { Key = key; Name = name; }
    }

    [DataContract]
    public sealed class ExcelLinkProfile
    {
        public const int CurrentFormat = 1;

        [DataMember(Order = 1)] public int Format { get; set; } = CurrentFormat;
        [DataMember(Order = 2)] public string Name { get; set; }
        [DataMember(Order = 3)] public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        [DataMember(Order = 4)] public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
        /// <summary>Tab the profile was saved from (ModelCategories, AnnotationCategories, Elements, Schedules, Spatial).</summary>
        [DataMember(Order = 5)] public string Tab { get; set; }
        [DataMember(Order = 6)] public List<ProfileItem> Categories { get; set; } = new List<ProfileItem>();
        /// <summary>Selected parameters in user order.</summary>
        [DataMember(Order = 7)] public List<ProfileItem> Parameters { get; set; } = new List<ProfileItem>();
        [DataMember(Order = 8)] public List<ProfileItem> Schedules { get; set; } = new List<ProfileItem>();
        [DataMember(Order = 9)] public string Scope { get; set; } = "WholeModel";
        [DataMember(Order = 10)] public string RowMode { get; set; } = "Instance";
        [DataMember(Order = 11)] public bool IncludeLinks { get; set; }
        [DataMember(Order = 12)] public bool CommonParametersOnly { get; set; }
        [DataMember(Order = 13)] public bool ShowInstance { get; set; } = true;
        [DataMember(Order = 14)] public bool ShowType { get; set; } = true;
        [DataMember(Order = 15)] public bool ShowReadOnly { get; set; } = true;
        [DataMember(Order = 16)] public string ScheduleMode { get; set; } = "DataExchange";

        public ExcelLinkProfile Clone(string newName)
        {
            var json = ProfileJson.Serialize(this);
            var copy = ProfileJson.Deserialize(json);
            copy.Name = newName;
            copy.CreatedUtc = DateTime.UtcNow;
            copy.ModifiedUtc = DateTime.UtcNow;
            return copy;
        }
    }

    /// <summary>Result of applying a profile to the current project: what is missing (by key).</summary>
    public sealed class ProfileResolution
    {
        public List<ProfileItem> MissingCategories { get; } = new List<ProfileItem>();
        public List<ProfileItem> MissingParameters { get; } = new List<ProfileItem>();
        public List<ProfileItem> MissingSchedules { get; } = new List<ProfileItem>();
        public bool Complete => MissingCategories.Count == 0 && MissingParameters.Count == 0 && MissingSchedules.Count == 0;

        public static ProfileResolution Resolve(ExcelLinkProfile p, ISet<string> categoryKeys, ISet<string> parameterKeys, ISet<string> scheduleKeys)
        {
            var r = new ProfileResolution();
            r.MissingCategories.AddRange(p.Categories.Where(x => categoryKeys != null && !categoryKeys.Contains(x.Key)));
            r.MissingParameters.AddRange(p.Parameters.Where(x => parameterKeys != null && !parameterKeys.Contains(x.Key)));
            r.MissingSchedules.AddRange(p.Schedules.Where(x => scheduleKeys != null && !scheduleKeys.Contains(x.Key)));
            return r;
        }
    }

    public static class ProfileJson
    {
        private static DataContractJsonSerializer Serializer() => new DataContractJsonSerializer(typeof(ExcelLinkProfile),
            new DataContractJsonSerializerSettings { DateTimeFormat = new DateTimeFormat("yyyy-MM-ddTHH:mm:ssZ") });

        public static string Serialize(ExcelLinkProfile p)
        {
            using (var ms = new MemoryStream())
            {
                Serializer().WriteObject(ms, p);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static ExcelLinkProfile Deserialize(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var p = (ExcelLinkProfile)Serializer().ReadObject(ms);
                if (p == null) throw new InvalidDataException("Empty profile.");
                if (p.Format > ExcelLinkProfile.CurrentFormat) throw new InvalidDataException("Profile format " + p.Format + " is newer than this add-in supports.");
                p.Categories = p.Categories ?? new List<ProfileItem>();
                p.Parameters = p.Parameters ?? new List<ProfileItem>();
                p.Schedules = p.Schedules ?? new List<ProfileItem>();
                if (string.IsNullOrWhiteSpace(p.Name)) throw new InvalidDataException("Profile has no name.");
                return p;
            }
        }
    }

    /// <summary>Stores profiles as one JSON file per profile in a folder (default %AppData%\CausaFix\ExcelLink\Profiles).</summary>
    public sealed class ProfileStore
    {
        public string Folder { get; }

        public ProfileStore(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(folder);
        }

        public List<ExcelLinkProfile> List(List<string> errors = null)
        {
            var list = new List<ExcelLinkProfile>();
            foreach (var f in Directory.GetFiles(Folder, "*.json"))
            {
                try { list.Add(ProfileJson.Deserialize(File.ReadAllText(f, Encoding.UTF8))); }
                catch (Exception ex) { errors?.Add(Path.GetFileName(f) + ": " + ex.Message); }
            }
            return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public bool Exists(string name) => File.Exists(PathFor(name));

        public void Save(ExcelLinkProfile p, bool overwrite = true)
        {
            ValidateName(p.Name);
            string path = PathFor(p.Name);
            if (!overwrite && File.Exists(path)) throw new IOException("A profile named '" + p.Name + "' already exists.");
            p.ModifiedUtc = DateTime.UtcNow;
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, ProfileJson.Serialize(p), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null, true);
            else File.Move(tmp, path);
        }

        public ExcelLinkProfile Load(string name) => ProfileJson.Deserialize(File.ReadAllText(PathFor(name), Encoding.UTF8));

        public void Delete(string name)
        {
            string path = PathFor(name);
            if (File.Exists(path)) File.Delete(path);
        }

        public void Rename(string oldName, string newName)
        {
            ValidateName(newName);
            if (string.Equals(oldName, newName, StringComparison.Ordinal)) return;
            if (Exists(newName) && !string.Equals(FileNameFor(oldName), FileNameFor(newName), StringComparison.OrdinalIgnoreCase))
                throw new IOException("A profile named '" + newName + "' already exists.");
            var p = Load(oldName);
            p.Name = newName;
            Delete(oldName);
            Save(p);
        }

        public ExcelLinkProfile Duplicate(string name, string newName)
        {
            ValidateName(newName);
            if (Exists(newName)) throw new IOException("A profile named '" + newName + "' already exists.");
            var copy = Load(name).Clone(newName);
            Save(copy, overwrite: false);
            return copy;
        }

        public ExcelLinkProfile Import(string file, bool overwrite)
        {
            var p = ProfileJson.Deserialize(File.ReadAllText(file, Encoding.UTF8));
            Save(p, overwrite);
            return p;
        }

        public void Export(string name, string file) => File.WriteAllText(file, ProfileJson.Serialize(Load(name)), new UTF8Encoding(false));

        public static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Profile name is required.");
            if (name.Length > 80) throw new ArgumentException("Profile name is too long (max 80).");
        }

        private string PathFor(string name) => Path.Combine(Folder, FileNameFor(name));

        public static string FileNameFor(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name.Trim())
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == '.' ? '_' : c);
            // Short hash keeps names that differ only in invalid characters distinct.
            uint h = 2166136261;
            foreach (char c in name.Trim()) { h ^= c; h *= 16777619; }
            return sb + "-" + h.ToString("x8") + ".json";
        }
    }
}
