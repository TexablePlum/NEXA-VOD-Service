using Microsoft.UI.Xaml.Data;
using System;

namespace Nexa.Client.Converters;

public class DateTimeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTime dateTime)
        {
            return dateTime.ToString("d"); // Krótki format daty (np. 15.06.2023)
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
