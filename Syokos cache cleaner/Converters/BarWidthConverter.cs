using System.Globalization;
using System.Windows.Data;

namespace ShaderCacheCleaner.Converters;

public sealed class BarWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return 0.0;

        double fill = values[0] is double d ? d : 0;
        double width = values[1] is double w ? w : 0;
        if (double.IsNaN(width) || width <= 0)
            return 0.0;

        return Math.Max(0, Math.Min(width, width * Math.Clamp(fill, 0, 1)));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
