using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace CloudBackup.App.Services;

/// <summary>
/// Сервис локализации. Управляет текущей культурой и уведомляет UI об изменениях.
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    private static readonly ResourceManager ResourceManager = new("CloudBackup.App.Resources.Strings", typeof(LocalizationService).Assembly);

    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Возвращает строку по ключу из текущего языка.
    /// </summary>
    public static string Get(string key)
    {
        try
        {
            return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }
        catch
        {
            return key;
        }
    }

    /// <summary>
    /// Возвращает форматированную строку.
    /// </summary>
    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentUICulture, template, args);
        }
        catch
        {
            return template;
        }
    }

    /// <summary>
    /// Меняет текущий язык.
    /// </summary>
    public void SetLanguage(string cultureCode)
    {
        var culture = new CultureInfo(cultureCode);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;

        // Уведомляем UI о том, что все строки изменились.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    /// <summary>
    /// Определяет язык системы и устанавливает его как текущий.
    /// Если система не ru/en — ставит английский.
    /// </summary>
    public void DetectSystemLanguage()
    {
        var sys = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        SetLanguage(sys == "ru" ? "ru" : "en");
    }

    public string CurrentLanguageCode => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
}