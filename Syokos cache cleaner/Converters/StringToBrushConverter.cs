using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ShaderCacheCleaner.Converters;

public sealed class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(s)!;
                return new SolidColorBrush(color);
            }
            catch
            {
                // fall through
            }
        }

        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
