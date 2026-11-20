using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BlueDock.Interop;
using BlueDock.Services;
using BlueDock.ViewModels;

namespace BlueDock.Views
{
    public partial class PopupWindow : Window
    {
        private const double ScreenMargin = 12;

        private readonly MainViewModel _vm;
        private DateTime _lastHidden = DateTime.MinValue;
        private bool _allowClose;
        private bool _warming;
        private bool _warmed;

        public PopupWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            SourceInitialized += (s, e) => ApplyEffects();
            ThemeService.ThemeChanged += (s, e) => ApplyEffects();
            Deactivated += (s, e) => { if (!_warming) HidePopup(); };
            SizeChanged += (s, e) => { if (!_warming) Reposition(); };
            PreviewKeyDown += OnPreviewKeyDown;
        }

        /// <summary>
        /// Pays WPF's one-off cost up front instead of on the user's first click.
        ///
        /// Showing the first window in a process starts the rendering stack, the font cache and
        /// a good deal of JIT: measured at about three seconds on this machine, against twenty
        /// milliseconds for every open after it. That is exactly the grey rectangle people see
        /// the first time they click the tray icon. Doing it here, off screen and transparent
        /// while nobody is waiting, makes the first real open as quick as the rest.
        /// </summary>
        public void PreWarm()
        {
            if (_warmed) return;
            _warmed = true;
            _warming = true;

            try
            {
                // Never steal focus or appear anywhere a person could notice it.
                ShowActivated = false;
                Opacity = 0;
                Left = -10000;
                Top = -10000;

                Show();
                UpdateLayout();
                // Layout alone is not enough; let the render thread produce an actual frame.
                Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);

                // The settings panel lives in this same window but is only built the first time
                // it is shown, so it would have its own smaller stall. Build it now as well.
                bool wasOpen = _vm.IsSettingsOpen;
                _vm.IsSettingsOpen = true;
                UpdateLayout();
                _vm.IsSettingsOpen = wasOpen;
                UpdateLayout();

                Hide();
            }
            catch
            {
                // A warm-up that fails is not worth bothering anyone with; the app still works.
            }
            finally
            {
                ShowActivated = true;
                _warming = false;
                // The hide above must not count as "just closed" and swallow the first click.
                _lastHidden = DateTime.MinValue;
            }
        }

        /// <summary>Left click on the tray icon.</summary>
        public void TogglePopup()
        {
            if (IsVisible)
            {
                HidePopup();
                return;
            }

            // Clicking the tray icon first deactivates (and hides) the popup; don't reopen it right away.
            if ((DateTime.UtcNow - _lastHidden).TotalMilliseconds < 300)
                return;

            ShowPopup();
        }

        public void ShowPopup()
        {
            if (!IsVisible)
            {
                Opacity = 0;
                Show();
                UpdateLayout();
                Reposition();
                Opacity = 1;
            }
            Activate();
        }

        public void HidePopup()
        {
            if (!IsVisible) return;
            _lastHidden = DateTime.UtcNow;
            Hide();
            _vm.IsSettingsOpen = false; // always reopen on the main panel
        }

        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true; // Alt+F4 only hides, exit through the tray menu
                HidePopup();
            }
            base.OnClosing(e);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;

            if (_vm.IsSettingsOpen)
                _vm.IsSettingsOpen = false;
            else
                HidePopup();
            e.Handled = true;
        }

        /// <summary>Anchor the popup next to the tray (bottom-right, or top-right with a top taskbar).</summary>
        private void Reposition()
        {
            var work = SystemParameters.WorkArea;
            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight;

            Left = work.Right - width - ScreenMargin;

            bool taskbarOnTop = work.Top > 0;
            Top = taskbarOnTop
                ? work.Top + ScreenMargin
                : work.Bottom - height - ScreenMargin;
        }

        private void ApplyEffects()
        {
            bool backdropActive = WindowEffects.Apply(this, ThemeService.Backdrop, ThemeService.IsDark);
            Root.SetResourceReference(Border.BackgroundProperty,
                backdropActive ? "Brush.WindowBackground" : "Brush.WindowFallback");
        }
    }
}
