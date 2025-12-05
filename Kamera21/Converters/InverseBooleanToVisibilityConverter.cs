using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Kamera21.Converters
{
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return Visibility.Visible;

            if (value is bool boolValue)
                return boolValue ? Visibility.Collapsed : Visibility.Visible;

            // Для других типов (например, ImageSource) проверяем, не null ли
            return value != null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}