using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Nexa.Client.Converters;

/// <summary>
/// Konwertuje string na Visibility. Jeśli string jest null lub pusty, zwraca Collapsed.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string str && !string.IsNullOrWhiteSpace(str))
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException("StringToVisibilityConverter does not support ConvertBack.");
    }
}
