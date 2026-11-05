using System;
using System.Windows;
using System.Windows.Media;

namespace BlueDock.Controls
{
    /// <summary>Circular battery indicator (0-100) used by the Soft Tiles theme.</summary>
    public sealed class BatteryRing : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(BatteryRing),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            nameof(Stroke), typeof(Brush), typeof(BatteryRing),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
            nameof(Track), typeof(Brush), typeof(BatteryRing),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)),
                FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
            nameof(Thickness), typeof(double), typeof(BatteryRing),
            new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
        public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
        public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return;

            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double r = (size - Thickness) / 2;

            dc.DrawEllipse(null, new Pen(Track, Thickness), center, r, r);

            double v = Math.Clamp(Value, 0, 100);
            if (v <= 0) return;
            if (v >= 100)
            {
                dc.DrawEllipse(null, new Pen(Stroke, Thickness), center, r, r);
                return;
            }

            double angle = v / 100 * 360;
            var start = new Point(center.X, center.Y - r);
            double rad = (angle - 90) * Math.PI / 180;
            var end = new Point(center.X + r * Math.Cos(rad), center.Y + r * Math.Sin(rad));

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(r, r), 0, angle > 180, SweepDirection.Clockwise, true, false);
            }
            geo.Freeze();

            var pen = new Pen(Stroke, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, geo);
        }
    }
}
