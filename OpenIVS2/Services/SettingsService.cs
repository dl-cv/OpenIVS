using System;
using System.IO;
using Newtonsoft.Json;
using OpenIVS2.Models;

namespace OpenIVS2.Services
{
    public sealed class SettingsService
    {
        public string FilePath { get; private set; }

        public SettingsService(string filePath = null)
        {
            FilePath = string.IsNullOrWhiteSpace(filePath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "openivs2.settings.json")
                : filePath;
        }

        public AppSettings Load()
        {
            if (!File.Exists(FilePath)) return AppSettings.CreateDefault();
            var settings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(FilePath));
            if (settings == null) settings = AppSettings.CreateDefault();
            settings.Normalize();
            return settings;
        }

        public void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            settings.Normalize();
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(settings, Formatting.Indented));
        }
    }
}
