using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Microsoft.UI;

namespace Nexa.Client.Converters
{
    /// <summary>
    /// Konwertuje plan (free/premium/vip) na kolor badge'a
    /// </summary>
    public class PlanToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string plan)
            {
                return plan.ToLower() switch
                {
                    "free" => new SolidColorBrush(Colors.Gray),
                    "basic" => new SolidColorBrush(Colors.DeepSkyBlue),
                    "premium" => new SolidColorBrush(Colors.DeepSkyBlue),
                    "pro" => new SolidColorBrush(Colors.Gold),
                    "vip" => new SolidColorBrush(Colors.Gold),
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }

            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
