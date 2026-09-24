using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FeatherScribe.App;

/// <summary>
/// Converts an element height into a pill corner radius (half the height).
/// WPF <c>Border</c> scales oversized radii proportionally in both directions, so a large
/// fixed radius such as 999 renders an ellipse instead of a pill. The optional
/// <see cref="CornerRadius"/> parameter caps the result (the theme's pill radius token).
/// </summary>
public sealed class PillCornerRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var height = value is double actualHeight && double.IsFinite(actualHeight) && actualHeight > 0
            ? actualHeight
            : 0;
        var maximum = parameter is CornerRadius cap ? cap.TopLeft : double.MaxValue;
        return new CornerRadius(Math.Min(maximum, height / 2));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
