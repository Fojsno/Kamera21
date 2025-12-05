using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Kamera21.Converters
{
    public class DefectsToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int defectCount)
            {
                return defectCount switch
                {
                    0 => Brushes.Green,
                    <= 3 => Brushes.Yellow,
                    <= 10 => Brushes.Orange,
                    _ => Brushes.Red
                };
            }

            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}