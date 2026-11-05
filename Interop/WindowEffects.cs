using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BlueDock.Services;

namespace BlueDock.Interop
{
    /// <summary>
    /// Applies the Windows 11 system backdrop (Mica / Acrylic), dark title mode and rounded corners
    /// through DWM. On builds without backdrop support the theme's opaque fallback brush is used.
    /// </summary>
    public static class WindowEffects
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWCP_ROUND = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int Left, Right, Top, Bottom; }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        /// <summary>System backdrops for normal windows arrived in Windows 11 22H2 (build 22621).</summary>
        public static bool SupportsBackdrop => Environment.OSVersion.Version.Build >= 22621;

        public static bool SupportsRoundedCorners => Environment.OSVersion.Version.Build >= 22000;

        /// <returns>True when a translucent system backdrop is active.</returns>
        public static bool Apply(Window window, BackdropKind backdrop, bool dark)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;

            int darkValue = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));

            if (SupportsRoundedCorners)
            {
                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            }

            bool useBackdrop = SupportsBackdrop && backdrop != BackdropKind.None;
            var source = HwndSource.FromHwnd(hwnd);

            if (useBackdrop)
            {
                if (source?.CompositionTarget != null)
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;

                var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
            }
            else
            {
                var margins = new MARGINS();
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
            }

            int type = useBackdrop ? (int)backdrop : (int)BackdropKind.None;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
            return useBackdrop;
        }
    }
}
