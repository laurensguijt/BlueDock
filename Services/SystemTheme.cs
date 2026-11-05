using System;
using Microsoft.Win32;

namespace BlueDock.Services
{
    /// <summary>
    /// The light/dark preference Windows itself uses for apps.
    ///
    /// There is no WinRT API for this on a plain desktop app, so it is read from the same
    /// registry value the shell writes: AppsUseLightTheme (not SystemUsesLightTheme, which
    /// is the taskbar and Start). Missing value means light, as it does for Windows itself.
    /// </summary>
    public static class SystemTheme
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string Value = "AppsUseLightTheme";

        /// <summary>Raised when the user switches Windows between light and dark.</summary>
        public static event EventHandler? Changed;

        static SystemTheme()
        {
            try
            {
                // A theme switch arrives as a general preference change; the registry then
                // already holds the new value. Fires on a system thread, so handlers must
                // marshal to the UI thread themselves.
                SystemEvents.UserPreferenceChanged += (s, e) =>
                {
                    if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
                        Changed?.Invoke(null, EventArgs.Empty);
                };
            }
            catch
            {
                // Without the notification the setting still works, it just stops following live.
            }
        }

        public static bool IsDark
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(Key);
                    return key?.GetValue(Value) is int light && light == 0;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
