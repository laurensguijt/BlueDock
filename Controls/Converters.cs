using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace BlueDock.Controls
{
    /// <summary>Shared converter instances, usable from any dictionary as {x:Static c:Conv.BoolToVis}.</summary>
    public static class Conv
    {
        public static readonly BooleanToVisibilityConverter BoolToVis = new();
        public static readonly InverseBoolToVisibilityConverter InverseBoolToVis = new();
        public static readonly PercentToWidthConverter PercentToWidth = new();
    }

    /// <summary>true → Collapsed, false → Visible.</summary>
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility v && v != Visibility.Visible;
    }

    /// <summary>Battery percentage (0-100) → width for a bar of the given max width (ConverterParameter).</summary>
    public sealed class PercentToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double pct = value is double d ? d : 0;
            double max = double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 28;
            return Math.Clamp(pct, 0, 100) / 100 * max;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
