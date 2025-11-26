using Microsoft.UI.Xaml.Data;
using System;

namespace Nexa.Client.Converters;

/// <summary>
/// Konwertuje string planu na bool dla RadioButton.
/// ConverterParameter określa wartość planu do porównania.
/// </summary>
public class PlanToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string currentPlan && parameter is string targetPlan)
        {
            return currentPlan.Equals(targetPlan, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isChecked && isChecked && parameter is string targetPlan)
        {
            return targetPlan;
        }
        // Nie zmieniaj wartości jeśli RadioButton został odznaczony
        return Microsoft.UI.Xaml.DependencyProperty.UnsetValue;
    }
}
