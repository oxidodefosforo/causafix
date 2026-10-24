using System;
using System.Collections.Generic;

namespace CausaFix.ExcelLink
{
    /// <summary>Dialogs implemented by the WPF host. View models never touch WPF directly.</summary>
    public interface IDialogService
    {
        string PickSaveWorkbook(string suggestedName, string folder);
        string PickOpenWorkbook(string folder);
        string PickOpenProfile();
        string PickSaveProfile(string suggestedName);
        bool Confirm(string title, string message);
        void Info(string title, string message);
        void Error(string title, string message);
        /// <summary>Returns null when cancelled.</summary>
        string Prompt(string title, string label, string defaultValue);
        /// <summary>Asks for one choice among options; returns the index or -1.</summary>
        int Choose(string title, string message, IList<string> options);
        void OpenWithShell(string path);
        void ShowReview(ReviewViewModel review);
    }

    public interface IClipboard
    {
        string GetText();
        void SetText(string text);
    }

    /// <summary>Paths used by the module (overridable in tests).</summary>
    public sealed class ModulePaths
    {
        public string ProfilesFolder { get; set; }
        public string ReportsFolder { get; set; }
        public string SettingsFile { get; set; }
        public string LogsFolder { get; set; }

        public static ModulePaths Default()
        {
            string roaming = Core.Settings.SettingsStore.DefaultRoot;
            string local = Core.Settings.SettingsStore.DefaultLocalRoot;
            return new ModulePaths
            {
                ProfilesFolder = System.IO.Path.Combine(roaming, "ExcelLink", "Profiles"),
                SettingsFile = System.IO.Path.Combine(roaming, "settings.json"),
                ReportsFolder = System.IO.Path.Combine(local, "Reports"),
                LogsFolder = System.IO.Path.Combine(local, "Logs")
            };
        }
    }
}
