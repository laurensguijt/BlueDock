using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlueDock.Services
{
    /// <summary>
    /// Renders the Tabler device icons to PNG files for use in toasts.
    ///
    /// A toast can only show an image from disk or a URL, and its background follows the Windows
    /// theme, so the icon is drawn in white on a filled accent circle: that reads on a light and
    /// on a dark toast alike. Files are cached next to the settings and only written once.
    /// </summary>
    public static class ToastIcons
    {
        private const int Size = 128;
        private const double Grid = 24.0;

        /// <summary>Matches the blue of the app icon rather than the current theme's accent,
        /// because a toast can appear long after the panel was last themed.</summary>
        private static readonly Color Background = Color.FromRgb(0x2F, 0x6B, 0xED);

        private static string Folder => Path.Combine(SettingsService.Folder, "toast-icons");

        /// <summary>
        /// Path to a PNG for this device kind, or null when it could not be produced.
        /// Never throws: a toast without an icon is still a useful toast.
        /// </summary>
        public static string? PathFor(DeviceKind kind)
        {
            try
            {
                string file = Path.Combine(Folder, kind + ".png");
                if (File.Exists(file)) return file;

                var geometry = Application.Current?.TryFindResource(KeyFor(kind)) as Geometry;
                if (geometry == null) return null;

                Directory.CreateDirectory(Folder);
                Render(geometry, file);
                return File.Exists(file) ? file : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Drops the cache so the next toast redraws the icons.</summary>
        public static void Clear()
        {
            try
            {
                if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
            }
            catch
            {
                // Stale icons are harmless.
            }
        }

        private static string KeyFor(DeviceKind kind) => kind switch
        {
            DeviceKind.Headphones => "Icon.Headphones",
            DeviceKind.Speaker => "Icon.Speaker",
            DeviceKind.Mouse => "Icon.Mouse",
            DeviceKind.Keyboard => "Icon.Keyboard",
            DeviceKind.Phone => "Icon.Phone",
            DeviceKind.Gamepad => "Icon.Gamepad",
            DeviceKind.Computer => "Icon.Computer",
            _ => "Icon.Bluetooth",
        };

        private static void Render(Geometry geometry, string file)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                double half = Size / 2.0;
                dc.DrawEllipse(new SolidColorBrush(Background), null, new Point(half, half), half, half);

                // The icon occupies the middle 50%, which leaves a comfortable ring of padding.
                double icon = Size * 0.5;
                double scale = icon / Grid;
                double offset = (Size - icon) / 2.0;

                var pen = new Pen(Brushes.White, 2)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round,
                };
                pen.Freeze();

                dc.PushTransform(new TranslateTransform(offset, offset));
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawGeometry(null, pen, geometry);
                dc.Pop();
                dc.Pop();
            }

            var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(file);
            encoder.Save(stream);
        }
    }
}
