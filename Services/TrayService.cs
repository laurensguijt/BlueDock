using System;
using System.Diagnostics;
using System.Windows;
using BlueDock.Views;
using Forms = System.Windows.Forms;

namespace BlueDock.Services
{
    /// <summary>System tray icon, context menu and Windows notifications.</summary>
    public sealed class TrayService : IDisposable
    {
        private readonly Forms.NotifyIcon _icon;
        private readonly PopupWindow _popup;

        public TrayService(PopupWindow popup, Action exit)
        {
            _popup = popup;

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open BlueDock", null, (s, e) => _popup.ShowPopup());
            menu.Items.Add("Windows Bluetooth settings", null, (s, e) => OpenUri("ms-settings:bluetooth"));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => exit());

            _icon = new Forms.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "BlueDock",
                Visible = true,
                ContextMenuStrip = menu,
            };

            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                    _popup.TogglePopup();
            };
        }

        /// <summary>
        /// Shows a Windows toast carrying the device's icon, falling back to a tray balloon
        /// when toasts are unavailable on this machine.
        /// </summary>
        public void Notify(string title, string text, DeviceKind kind = DeviceKind.Other)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (ToastService.Show(title, text, kind)) return;
                _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.None);
            });
        }

        public static void OpenUri(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch
            {
                // Ignore: the URI handler is part of Windows and should exist.
            }
        }

        private static System.Drawing.Icon LoadIcon()
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/bluedock.ico"));
            using var stream = info.Stream;
            return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
