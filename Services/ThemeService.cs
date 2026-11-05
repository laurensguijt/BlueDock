using System;
using System.Collections.Generic;
using System.Windows;

namespace BlueDock.Services
{
    /// <summary>What the window chrome needs from a theme (read from the theme's resources).</summary>
    public enum BackdropKind { None = 1, Mica = 2, Acrylic = 3 }

    /// <summary>Light, dark, or whatever Windows is set to.</summary>
    public enum ThemeMode { Auto, Light, Dark }

    /// <summary>
    /// Builds the active look out of two dictionaries: a palette (colours, per mode) and a
    /// structure (templates, radii, spacing, shared by both modes of a theme). That way a
    /// light/dark switch is a palette swap instead of a second copy of every template.
    /// </summary>
    public static class ThemeService
    {
        public const string Fluent = "Fluent";
        public const string Glass = "Glass";
        public const string Tiles = "Tiles";

        /// <summary>Theme key → display name shown in the Settings dropdown.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Available { get; } = new[]
        {
            new KeyValuePair<string, string>(Fluent, "Windows 11 Fluent"),
            new KeyValuePair<string, string>(Glass, "Liquid Glass"),
            new KeyValuePair<string, string>(Tiles, "Soft Tiles"),
        };

        public static event EventHandler? ThemeChanged;

        public static string CurrentTheme { get; private set; } = Fluent;

        public static ThemeMode CurrentMode { get; private set; } = ThemeMode.Auto;

        /// <summary>The mode actually being shown, with Auto resolved against Windows.</summary>
        public static bool IsDark { get; private set; }

        static ThemeService()
        {
            SystemTheme.Changed += (s, e) =>
            {
                // Only Auto follows Windows, and the event arrives on a system thread.
                if (CurrentMode != ThemeMode.Auto) return;
                var dispatcher = Application.Current?.Dispatcher;
                dispatcher?.BeginInvoke(new Action(() =>
                {
                    if (CurrentMode == ThemeMode.Auto && IsDark != SystemTheme.IsDark)
                        Apply(CurrentTheme, CurrentMode);
                }));
            };
        }

        public static ThemeMode ParseMode(string? mode) =>
            Enum.TryParse<ThemeMode>(mode, ignoreCase: true, out var m) ? m : ThemeMode.Auto;

        public static bool ResolveDark(ThemeMode mode) => mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => SystemTheme.IsDark,
        };

        public static void Apply(string theme, ThemeMode mode)
        {
            if (theme != Fluent && theme != Glass && theme != Tiles)
                theme = Fluent;

            bool dark = ResolveDark(mode);

            var palette = Load($"{theme}.{(dark ? "Dark" : "Light")}");
            var structure = Load(theme);

            var merged = Application.Current.Resources.MergedDictionaries;
            // Index 0 = Shared.xaml, 1 = palette, 2 = structure (see App.xaml)
            while (merged.Count < 3) merged.Add(new ResourceDictionary());
            merged[1] = palette;
            merged[2] = structure;

            CurrentTheme = theme;
            CurrentMode = mode;
            IsDark = dark;
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static ResourceDictionary Load(string name) => new()
        {
            Source = new Uri($"pack://application:,,,/BlueDock;component/Themes/{name}.xaml", UriKind.Absolute)
        };

        public static BackdropKind Backdrop =>
            Application.Current.TryFindResource("Theme.Backdrop") is string s
            && Enum.TryParse(s, out BackdropKind b) ? b : BackdropKind.None;
    }
}
