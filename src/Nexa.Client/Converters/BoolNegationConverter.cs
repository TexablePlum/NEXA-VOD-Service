using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Nexa.Client.Converters
{
    /// <summary>
    /// Konwertuje bool na odwrotność (true -> false, false -> true)
    /// Używane głównie dla Visibility
    /// </summary>
    public class BoolNegationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                if (targetType == typeof(Visibility))
                {
                    return boolValue ? Visibility.Collapsed : Visibility.Visible;
                }
                return !boolValue;
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }

            return value;
        }
    }
}
