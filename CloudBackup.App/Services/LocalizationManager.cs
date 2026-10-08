using System.ComponentModel;
using System.Globalization;

namespace CloudBackup.App.Services;

/// <summary>
/// Синглтон для биндинга строк в XAML.
/// Индексатор this[key] возвращает локализованную строку. При смене языка вызывается PropertyChanged(null) — все биндинги обновляются.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    private LocalizationManager() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Индексатор для биндинга: {Binding [Header_Title], Source={x:Static svc:LocalizationManager.Instance}}
    /// </summary>
    public string this[string key] => LocalizationService.Get(key);

    /// <summary>
    /// Текущий код языка (ru/en). Оставлено на будущее.
    /// </summary>
    public string CurrentLanguageCode => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    /// <summary>
    /// Меняет текущий язык и уведомляет UI об изменениях.
    /// </summary>
    public void SetLanguage(string code)
    {
        var culture = new CultureInfo(code);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;

        RaiseChanged();
    }

    /// <summary>
    /// Уведомляет UI о необходимости перечитать все локализованные строки.
    /// </summary>
    private void RaiseChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

/// <summary>
/// Расширения для <see cref="LocalizationManager"/>.
/// </summary>
public static class LocalizationManagerExtensions
{
    /// <summary>
    /// Определяет язык системы и устанавливает его (ru/en). Если система не русская, ставится английский.
    /// </summary>
    public static void DetectSystemLanguageOrDefault(this LocalizationManager manager)
    {
        var sys = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        manager.SetLanguage(sys == "ru" ? "ru" : "en");
    }
}