using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace BlueDock.Controls
{
    /// <summary>
    /// Draws a Tabler icon from Themes/Icons.xaml.
    ///
    /// Tabler icons are stroked outlines on a 24x24 grid with a 2 unit stroke and round caps,
    /// so they are drawn with a pen and never filled. The geometry stays in grid units and the
    /// whole drawing is scaled to <see cref="IconSize"/>, which keeps the stroke weight in
    /// proportion at any size or DPI. Foreground is inherited, so an icon picks up the colour of
    /// the card it sits on and follows a theme switch without any extra binding.
    /// </summary>
    public sealed class TablerIcon : FrameworkElement
    {
        /// <summary>The grid the upstream icons are drawn on.</summary>
        private const double Grid = 24.0;

        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(Geometry), typeof(TablerIcon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
            nameof(IconSize), typeof(double), typeof(TablerIcon),
            new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure
                                              | FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Stroke width in grid units; scaled along with the icon.</summary>
        public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(
            nameof(StrokeWidth), typeof(double), typeof(TablerIcon),
            new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ForegroundProperty =
            TextElement.ForegroundProperty.AddOwner(typeof(TablerIcon),
                new FrameworkPropertyMetadata(Brushes.Black,
                    FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

        public Geometry? Data
        {
            get => (Geometry?)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public double IconSize
        {
            get => (double)GetValue(IconSizeProperty);
            set => SetValue(IconSizeProperty, value);
        }

        public double StrokeWidth
        {
            get => (double)GetValue(StrokeWidthProperty);
            set => SetValue(StrokeWidthProperty, value);
        }

        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double side = IconSize;
            return new Size(side, side);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var data = Data;
            var brush = Foreground;
            if (data == null || brush == null || IconSize <= 0) return;

            var pen = new Pen(brush, StrokeWidth)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();

            double scale = IconSize / Grid;
            drawingContext.PushTransform(new ScaleTransform(scale, scale));
            drawingContext.DrawGeometry(null, pen, data);
            drawingContext.Pop();
        }
    }
}
