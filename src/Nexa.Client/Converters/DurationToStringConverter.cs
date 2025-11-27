using Microsoft.UI.Xaml.Data;
using System;

namespace Nexa.Client.Converters
{
    /// <summary>
    /// Konwertuje czas w sekundach na czytelny format (np. "1h 45m")
    /// </summary>
    public class DurationToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double seconds)
            {
                var timeSpan = TimeSpan.FromSeconds(seconds);

                if (timeSpan.TotalHours >= 1)
                {
                    return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
                }
                else
                {
                    return $"{timeSpan.Minutes}m";
                }
            }

            return "N/A";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
