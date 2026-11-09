using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        public PopupWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            SourceInitialized += (s, e) => ApplyEffects();
            ThemeService.ThemeChanged += (s, e) => ApplyEffects();
            Deactivated += (s, e) => HidePopup();
            SizeChanged += (s, e) => Reposition();
            PreviewKeyDown += OnPreviewKeyDown;
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
