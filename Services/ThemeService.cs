using System;
using System.Collections.Generic;
using System.Windows;

namespace BlueDock.Services
{
    /// <summary>What the window chrome needs from a theme (read from the theme's resources).</summary>
    public enum BackdropKind { None = 1, Mica = 2, Acrylic = 3 }

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

        public static void Apply(string theme)
        {
            if (theme != Fluent && theme != Glass && theme != Tiles)
                theme = Fluent;

            var dict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/BlueDock;component/Themes/{theme}.xaml", UriKind.Absolute)
            };

            var merged = Application.Current.Resources.MergedDictionaries;
            // Index 0 = Shared.xaml, index 1 = active theme (see App.xaml)
            if (merged.Count > 1)
                merged[1] = dict;
            else
                merged.Add(dict);

            CurrentTheme = theme;
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        public static BackdropKind Backdrop =>
            Application.Current.TryFindResource("Theme.Backdrop") is string s
            && Enum.TryParse(s, out BackdropKind b) ? b : BackdropKind.None;

        public static bool IsDark =>
            Application.Current.TryFindResource("Theme.IsDark") is bool d && d;
    }
}
