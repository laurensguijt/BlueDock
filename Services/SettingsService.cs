using System;
using System.IO;
using System.Text.Json;

namespace BlueDock.Services
{
    public sealed class AppSettings
    {
        public string Theme { get; set; } = ThemeService.Fluent;
        public bool NotifyOnConnect { get; set; } = true;
        public bool NotifyOnDisconnect { get; set; } = true;
        public bool NotifyOnLowBattery { get; set; } = true;
        public int LowBatteryThreshold { get; set; } = 20;
        public int BatteryRefreshSeconds { get; set; } = 120;
    }

    /// <summary>Loads and saves %AppData%\BlueDock\settings.json.</summary>
    public sealed class SettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string Folder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlueDock");

        public static string FilePath { get; } = Path.Combine(Folder, "settings.json");

        public AppSettings Current { get; private set; } = new AppSettings();

        public void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch
            {
                // Corrupt file: fall back to defaults, the next Save() rewrites it.
                Current = new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
            }
            catch
            {
                // Settings are a convenience; never crash the tray app over them.
            }
        }
    }
}
