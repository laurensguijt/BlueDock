using System;
using System.Windows;
using CommunityToolkit.WinUI.Notifications;

namespace BlueDock.Services
{
    /// <summary>
    /// Windows toast notifications, with the device's own icon.
    ///
    /// An unpackaged desktop app cannot just ask for a toast: Windows only accepts one from an
    /// application user model id that is registered, together with a COM activator, under
    /// HKCU\Software\Classes. Without that registration CreateToastNotifier fails with
    /// ERROR_NOT_FOUND (0x80070490). The toolkit performs that registration, which is why it is
    /// used here rather than calling the WinRT API directly.
    ///
    /// Toasts can still be unavailable (notifications switched off by policy, for instance), so
    /// every call reports whether it got through and the caller falls back to a tray balloon.
    /// </summary>
    public static class ToastService
    {
        private static bool _failed;

        /// <summary>Why the last toast failed, for diagnosis; null while all is well.</summary>
        public static string? LastError { get; private set; }

        /// <summary>False once a toast has failed; the caller should fall back from then on.</summary>
        public static bool IsAvailable => !_failed;

        /// <summary>
        /// Shows a toast. Returns false when Windows would not take it, so the caller can fall
        /// back to the tray balloon instead of silently showing the user nothing.
        /// </summary>
        public static bool Show(string title, string text, DeviceKind kind)
        {
            if (_failed) return false;

            try
            {
                var builder = new ToastContentBuilder()
                    .AddText(title)
                    .AddText(text);

                string? icon = ToastIcons.PathFor(kind);
                if (icon != null)
                    builder.AddAppLogoOverride(new Uri(icon), ToastGenericAppLogoCrop.Circle);

                builder.Show();
                return true;
            }
            catch (Exception ex)
            {
                // Toasts are unavailable on this machine; stop trying and let the caller fall back.
                _failed = true;
                LastError = ex.GetType().Name + " 0x" + ex.HResult.ToString("X8") + ": " + ex.Message;
                try
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(SettingsService.Folder, "toast-error.log"),
                        DateTime.Now + "\r\n" + ex);
                }
                catch
                {
                    // Diagnostics are a nicety, never a reason to fail harder.
                }
                return false;
            }
        }

        /// <summary>Removes anything this app still has on screen or in the action centre.</summary>
        public static void Clear()
        {
            try
            {
                ToastNotificationManagerCompat.History.Clear();
            }
            catch
            {
                // Nothing to clean up, or no registration at all.
            }
        }

        /// <summary>
        /// Releases the toolkit's registration on exit. Without this a click on an old toast can
        /// start the app again through the COM activator.
        /// </summary>
        public static void Uninstall()
        {
            try
            {
                ToastNotificationManagerCompat.Uninstall();
            }
            catch
            {
                // Already gone.
            }
        }

        /// <summary>True when Windows started this process by activating a toast.</summary>
        public static bool WasStartedByToast =>
            ToastNotificationManagerCompat.WasCurrentProcessToastActivated();

        /// <summary>Runs <paramref name="onActivated"/> when the user clicks one of our toasts.</summary>
        public static void OnActivated(Action onActivated)
        {
            try
            {
                ToastNotificationManagerCompat.OnActivated += _ =>
                {
                    var dispatcher = Application.Current?.Dispatcher;
                    dispatcher?.BeginInvoke(onActivated);
                };
            }
            catch
            {
                // No activation handling; the toast still shows.
            }
        }
    }
}
