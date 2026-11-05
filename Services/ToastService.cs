using System;
using System.IO;
using System.Security;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace BlueDock.Services
{
    /// <summary>
    /// Windows toasts, carrying the device's icon.
    ///
    /// An unpackaged app may only notify under an application user model id that Windows can
    /// resolve. The installer provides one through its Start Menu shortcut; without it
    /// CreateToastNotifier fails with ERROR_NOT_FOUND (0x80070490). So the portable build, and
    /// a run from the build folder, have no toasts and the caller falls back to a tray balloon.
    ///
    /// Availability is settled once, up front. It cannot be inferred from a failing Show():
    /// Show() returns happily while Windows discards the notification, which is exactly how an
    /// earlier attempt managed to show the user nothing at all. The only honest check is
    /// whether the identity exists before anything is sent.
    /// </summary>
    public static class ToastService
    {
        /// <summary>Must match AppUserModelID on the shortcut in Installer\BlueDock.iss.</summary>
        private const string Aumid = "LaurensGuijt.BlueDock";

        private static readonly Lazy<ToastNotifier?> Notifier = new(CreateNotifier);

        /// <summary>True when this install can actually deliver toasts.</summary>
        public static bool IsAvailable => Notifier.Value != null;

        /// <summary>Why toasts are off, for the log and for diagnosis; null when they are on.</summary>
        public static string? Unavailable { get; private set; }

        private static ToastNotifier? CreateNotifier()
        {
            // The shortcut is what makes the id resolvable, so its absence means "not installed".
            string shortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs\BlueDock.lnk");

            if (!File.Exists(shortcut))
            {
                Unavailable = "niet geinstalleerd (geen snelkoppeling in het startmenu)";
                return null;
            }

            try
            {
                var notifier = ToastNotificationManager.CreateToastNotifier(Aumid);
                if (notifier.Setting != NotificationSetting.Enabled)
                {
                    Unavailable = "meldingen staan uit: " + notifier.Setting;
                    return null;
                }
                return notifier;
            }
            catch (Exception ex)
            {
                Unavailable = ex.GetType().Name + " 0x" + ex.HResult.ToString("X8");
                return null;
            }
        }

        /// <summary>
        /// Notes in the settings folder which of the two notification routes is in use. A tray
        /// app fails invisibly otherwise: there is no window to show a warning in.
        /// </summary>
        public static void LogRoute()
        {
            try
            {
                string line = IsAvailable
                    ? "toasts actief (" + Aumid + ")"
                    : "tray-ballonnen, want toasts zijn niet beschikbaar: " + Unavailable;
                File.WriteAllText(Path.Combine(SettingsService.Folder, "notifications.log"),
                                  DateTime.Now + "  " + line);
            }
            catch
            {
                // Diagnostics are a nicety, never a reason to fail.
            }
        }

        /// <summary>
        /// Shows a toast. Returns false when this install cannot deliver them, so the caller
        /// shows a tray balloon instead of leaving the user with nothing.
        /// </summary>
        public static bool Show(string title, string text, DeviceKind kind)
        {
            var notifier = Notifier.Value;
            if (notifier == null) return false;

            try
            {
                var document = new XmlDocument();
                document.LoadXml(BuildXml(title, text, ToastIcons.PathFor(kind)));
                notifier.Show(new ToastNotification(document));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildXml(string title, string text, string? iconPath)
        {
            string logo = iconPath == null
                ? ""
                : $"<image placement=\"appLogoOverride\" hint-crop=\"circle\" src=\"{SecurityElement.Escape(iconPath)}\" />";

            return "<toast>"
                 + "<visual><binding template=\"ToastGeneric\">"
                 + $"<text>{SecurityElement.Escape(title)}</text>"
                 + $"<text>{SecurityElement.Escape(text)}</text>"
                 + logo
                 + "</binding></visual>"
                 + "</toast>";
        }

        /// <summary>Removes anything this app still has in the action centre.</summary>
        public static void Clear()
        {
            if (!IsAvailable) return;
            try
            {
                ToastNotificationManager.History.Clear(Aumid);
            }
            catch
            {
                // Nothing to clear.
            }
        }
    }
}
