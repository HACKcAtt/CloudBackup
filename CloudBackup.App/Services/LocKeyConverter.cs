using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CloudBackup.App.Services;

/// <summary>
/// Конвертер ключа локализации в текст. Используется в ComboBox для расписания, где ItemsSource = ключи, а отображать нужно локализованные строки.
/// </summary>
public class LocKeyConverter : IValueConverter
{
    public static readonly LocKeyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? LocalizationService.Get(key) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Обратное преобразование не требуется (ComboBox хранит ключ)ю
        throw new NotSupportedException();
    }
}