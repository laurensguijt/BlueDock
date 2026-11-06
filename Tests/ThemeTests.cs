using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using BlueDock.Services;
using Xunit;

namespace BlueDock.Tests
{
    /// <summary>
    /// A theme is a structure file plus a light and a dark palette. If a palette misses a key
    /// the app does not fail to build: it silently draws that brush as nothing, in one mode
    /// only. These tests load the real dictionaries and compare them.
    /// </summary>
    public class ThemeTests
    {
        private static readonly string[] Themes = { ThemeService.Fluent, ThemeService.Glass, ThemeService.Tiles };

        /// <summary>Resource dictionaries need a WPF application on an STA thread.</summary>
        private static T OnStaThread<T>(Func<T> work)
        {
            T result = default!;
            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    if (Application.Current == null) new Application();
                    result = work();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null) throw failure;
            return result;
        }

        private static ResourceDictionary Load(string name) => new()
        {
            Source = new Uri("pack://application:,,,/BlueDock;component/Themes/" + name + ".xaml", UriKind.Absolute)
        };

        private static List<string> Keys(ResourceDictionary dictionary) =>
            dictionary.Keys.Cast<object>().Select(k => k.ToString() ?? "").OrderBy(k => k).ToList();

        [Fact]
        public void Both_palettes_of_a_theme_define_the_same_keys()
        {
            foreach (string theme in Themes)
            {
                var (light, dark) = OnStaThread(() => (Keys(Load(theme + ".Light")), Keys(Load(theme + ".Dark"))));

                Assert.False(light.Count == 0, theme + " light palette is empty");
                Assert.Equal(light, dark);
            }
        }

        [Fact]
        public void Every_palette_states_which_mode_it_is()
        {
            foreach (string theme in Themes)
            {
                var (light, dark) = OnStaThread(() =>
                    (Load(theme + ".Light")["Theme.IsDark"], Load(theme + ".Dark")["Theme.IsDark"]));

                Assert.Equal(false, light);
                Assert.Equal(true, dark);
            }
        }

        [Fact]
        public void A_theme_can_be_assembled_the_way_the_app_does_it()
        {
            // Shared + palette + structure, then the resources the window actually asks for.
            string[] required =
            {
                "Brush.Text", "Brush.TextSecondary", "Brush.Accent", "Brush.OnAccent",
                "Brush.WindowBackground", "Brush.WindowFallback", "Brush.DeviceButton",
                "Template.ConnectedItem", "Template.PairedItem", "Panel.Connected", "Panel.Paired",
                "Style.CardButton", "Style.DeviceButton", "Style.CardIconButton", "Icon.Bluetooth",
            };

            foreach (string theme in Themes)
            {
                var missing = OnStaThread(() =>
                {
                    var merged = new ResourceDictionary();
                    merged.MergedDictionaries.Add(Load("Shared"));
                    merged.MergedDictionaries.Add(Load(theme + ".Dark"));
                    merged.MergedDictionaries.Add(Load(theme));
                    return required.Where(k => !merged.Contains(k)).ToList();
                });

                Assert.True(missing.Count == 0, theme + " is missing: " + string.Join(", ", missing));
            }
        }

        [Theory]
        [InlineData("Auto", ThemeMode.Auto)]
        [InlineData("light", ThemeMode.Light)]
        [InlineData("DARK", ThemeMode.Dark)]
        public void Reads_the_stored_mode_whatever_its_casing(string stored, ThemeMode expected) =>
            Assert.Equal(expected, ThemeService.ParseMode(stored));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("nonsense")]
        public void Falls_back_to_following_windows(string? stored) =>
            Assert.Equal(ThemeMode.Auto, ThemeService.ParseMode(stored));

        [Fact]
        public void Light_and_dark_do_not_consult_windows()
        {
            Assert.False(ThemeService.ResolveDark(ThemeMode.Light));
            Assert.True(ThemeService.ResolveDark(ThemeMode.Dark));
        }
    }
}
