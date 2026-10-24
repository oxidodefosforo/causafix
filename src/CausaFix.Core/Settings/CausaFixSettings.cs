using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using CausaFix.Core.Reports;
using CausaFix.Core.Values;

namespace CausaFix.Core.Settings
{
    /// <summary>User settings shared by CausaFix modules (stored in %AppData%\CausaFix\settings.json).</summary>
    [DataContract]
    public sealed class CausaFixSettings
    {
        /// <summary>How numbers typed as text in Excel are read. Grid input uses the Windows regional separator when Auto.</summary>
        [DataMember(Order = 1)] public DecimalSeparatorMode DecimalMode { get; set; } = DecimalSeparatorMode.Auto;
        [DataMember(Order = 2)] public ApplyMode DefaultApplyMode { get; set; } = ApplyMode.Atomic;
        [DataMember(Order = 3)] public string LastFolder { get; set; } = string.Empty;
        [DataMember(Order = 4)] public bool OpenFileAfterExport { get; set; } = true;
        [DataMember(Order = 5)] public bool WriteReportAfterApply { get; set; } = true;
        [DataMember(Order = 6)] public string ClearToken { get; set; } = Values.ImportOptions.DefaultClearToken;

        /// <summary>DataContract deserialisation skips constructors: restore defaults for fields missing in older files.</summary>
        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            DecimalMode = DecimalSeparatorMode.Auto;
            DefaultApplyMode = ApplyMode.Atomic;
            LastFolder = string.Empty;
            OpenFileAfterExport = true;
            WriteReportAfterApply = true;
            ClearToken = Values.ImportOptions.DefaultClearToken;
        }

        public Values.ImportOptions ToImportOptions() => new Values.ImportOptions
        {
            DecimalMode = DecimalMode,
            ClearToken = string.IsNullOrWhiteSpace(ClearToken) ? Values.ImportOptions.DefaultClearToken : ClearToken.Trim()
        };
    }

    public sealed class SettingsStore
    {
        public string Path { get; }

        public SettingsStore(string path) { Path = path; }

        public static string DefaultRoot => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CausaFix");
        public static string DefaultLocalRoot => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CausaFix");

        public CausaFixSettings Load()
        {
            try
            {
                if (!File.Exists(Path)) return new CausaFixSettings();
                using (var fs = File.OpenRead(Path))
                    return (CausaFixSettings)new DataContractJsonSerializer(typeof(CausaFixSettings)).ReadObject(fs) ?? new CausaFixSettings();
            }
            catch (Exception)
            {
                return new CausaFixSettings(); // damaged settings never block the tool
            }
        }

        public void Save(CausaFixSettings s)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(CausaFixSettings)).WriteObject(ms, s);
                File.WriteAllText(Path, Encoding.UTF8.GetString(ms.ToArray()), new UTF8Encoding(false));
            }
        }
    }

    /// <summary>Very small file logger (one file per day under %LocalAppData%\CausaFix\Logs).</summary>
    public sealed class FileLog
    {
        private readonly string _folder;
        private readonly object _lock = new object();

        public FileLog(string folder) { _folder = folder; }

        public string CurrentFile => System.IO.Path.Combine(_folder, "causafix-" + DateTime.Now.ToString("yyyyMMdd") + ".log");

        public void Info(string m) => Write("INFO", m);
        public void Warn(string m) => Write("WARN", m);
        public void Error(string m, Exception ex = null) => Write("ERROR", m + (ex != null ? Environment.NewLine + ex : ""));

        private void Write(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(_folder);
                    File.AppendAllText(CurrentFile, DateTime.Now.ToString("HH:mm:ss.fff") + " " + level + " " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception) { /* logging never throws */ }
        }
    }
}
