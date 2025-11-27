using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;

namespace Nexa.Client.Converters;

/// <summary>
/// Konwertuje string planu na bool dla RadioButton.
/// ConverterParameter określa wartość planu do porównania.
/// </summary>
public class PlanToBoolConverter : IValueConverter
{
    // Cache dla ostatniej wybranej wartości per grupa RadioButton
    private static readonly Dictionary<string, string> _lastValues = new();
    private const string CacheKey = "PlanSelection";

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string currentPlan && parameter is string targetPlan)
        {
            // Zapamiętaj ostatnią wartość
            _lastValues[CacheKey] = currentPlan;
            return currentPlan.Equals(targetPlan, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isChecked && parameter is string targetPlan)
        {
            if (isChecked)
            {
                // RadioButton został zaznaczony - zwróć nową wartość
                _lastValues[CacheKey] = targetPlan;
                return targetPlan;
            }
            else
            {
                // RadioButton został odznaczony - zwróć ostatnią zapamiętaną wartość
                // To zapobiega resetowaniu wartości do pustego stringa
                return _lastValues.TryGetValue(CacheKey, out var lastValue) ? lastValue : "free";
            }
        }
        return _lastValues.TryGetValue(CacheKey, out var cachedValue) ? cachedValue : "free";
    }
}
