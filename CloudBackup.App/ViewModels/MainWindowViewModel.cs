using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CloudBackup.App.Services;
using CloudBackup.Core.Models;
using CloudBackup.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace CloudBackup.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IWebDavService _webDavService;
    private readonly IBackupService _backupService;
    private readonly BackupWorker _worker;
    private readonly ServiceInstaller _serviceInstaller;
    private readonly LocalizationManager _loc = LocalizationManager.Instance;

    // Поля для хранения ключей локализации.
    private string _statusKey = "Status_Initial";
    private object[]? _statusArgs;

    private string? _freeSpaceKey;
    private object[]? _freeSpaceArgs;

    private string? _sourceSizeKey;
    private object[]? _sourceSizeArgs;

    // Observable-свойства.

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _sourceFolder = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _selectedIntervalKey = "Interval_1Hour";

    // Вычисляемые локализованные свойства.
    public string StatusMessage => _statusArgs is null or { Length: 0 }
        ? LocalizationService.Get(_statusKey)
        : LocalizationService.Format(_statusKey, _statusArgs);

    public string FreeSpaceInfo => string.IsNullOrEmpty(_freeSpaceKey)
        ? string.Empty
        : _freeSpaceArgs is null or { Length: 0 }
            ? LocalizationService.Get(_freeSpaceKey)
            : LocalizationService.Format(_freeSpaceKey, _freeSpaceArgs);

    public string SourceSizeInfo => string.IsNullOrEmpty(_sourceSizeKey)
        ? string.Empty
        : _sourceSizeArgs is null or { Length: 0 }
            ? LocalizationService.Get(_sourceSizeKey)
            : LocalizationService.Format(_sourceSizeKey, _sourceSizeArgs);

    public ObservableCollection<string> IntervalKeys { get; } =
    [
        "Interval_1Minute", "Interval_30Minutes", "Interval_1Hour",
        "Interval_3Hours", "Interval_6Hours", "Interval_12Hours",
        "Interval_24Hours", "Interval_7Days", "Interval_30Days"
    ];

    public MainWindowViewModel(
        IWebDavService webDavService,
        IBackupService backupService,
        BackupWorker worker,
        ServiceInstaller serviceInstaller)
    {
        _webDavService = webDavService;
        _backupService = backupService;
        _worker = worker;
        _serviceInstaller = serviceInstaller;

        _loc.DetectSystemLanguageOrDefault();
        _ = LoadExistingConfigAsync();

        // При смене языка уведомляем UI, чтобы вычисляемые свойства перечитались.
        _loc.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(FreeSpaceInfo));
            OnPropertyChanged(nameof(SourceSizeInfo));
        };
    }

    // Хелперы для установки локализованных значений.
    private void SetStatus(string key)
    {
        _statusKey = key;
        _statusArgs = null;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void SetStatusFormat(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void SetFreeSpace(string key, params object[] args)
    {
        _freeSpaceKey = key;
        _freeSpaceArgs = args.Length > 0 ? args : null;
        OnPropertyChanged(nameof(FreeSpaceInfo));
    }

    private void ClearFreeSpace()
    {
        _freeSpaceKey = null;
        _freeSpaceArgs = null;
        OnPropertyChanged(nameof(FreeSpaceInfo));
    }

    private void SetSourceSize(long bytes)
    {
        _sourceSizeKey = "Info_SourceSize";
        _sourceSizeArgs = [FormatBytes(bytes)];
        OnPropertyChanged(nameof(SourceSizeInfo));
    }

    private void ClearSourceSize()
    {
        _sourceSizeKey = null;
        _sourceSizeArgs = null;
        OnPropertyChanged(nameof(SourceSizeInfo));
    }

    // Смена языка.
    [RelayCommand]
    private void SwitchToRussian() => _loc.SetLanguage("ru");

    [RelayCommand]
    private void SwitchToEnglish() => _loc.SetLanguage("en");

    // Основные команды.
    [RelayCommand]
    private async Task SelectFolder()
    {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = TopLevel.GetTopLevel(desktop?.MainWindow);
        if (topLevel == null)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.Get("Label_SourceFolder"),
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            SourceFolder = folders[0].Path.LocalPath;

            if (Directory.Exists(SourceFolder))
                SetSourceSize(_backupService.GetDirectorySize(SourceFolder));
            else
                ClearSourceSize();
        }
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        var validationError = ValidateCredentials();
        if (validationError is not null)
        {
            SetStatus(validationError);
            await ShowAlertAsync("Alert_Title_Error", validationError, Icon.Error);
            return;
        }

        IsBusy = true;
        SetStatus("Status_CheckingConnection");

        var tempConfig = new AppConfig { Email = Email, AppPassword = Password };
        var isSuccess = await _webDavService.TestConnectionAsync(tempConfig);

        if (isSuccess)
        {
            var free = await _webDavService.GetFreeSpaceAsync(tempConfig);
            if (free > 0)
                SetFreeSpace("Info_FreeSpace", FormatBytes(free));
            else
                SetFreeSpace("Info_FreeSpaceUnknown");

            SetStatus("Status_ConnectionOk");
        }
        else
        {
            ClearFreeSpace();
            SetStatus("Status_ConnectionFailed");
            await ShowAlertAsync("Alert_Title_Error", "Status_ConnectionFailed", Icon.Error);
        }

        IsBusy = false;
    }

    [RelayCommand]
    private async Task InstallAndRunService()
    {
        if (string.IsNullOrEmpty(SourceFolder))
        {
            SetStatus("Status_FolderEmpty");
            await ShowAlertAsync("Alert_Title_Error", "Status_FolderEmpty", Icon.Error);
            return;
        }

        if (!Directory.Exists(SourceFolder))
        {
            SetStatus("Status_FolderNotExist");
            await ShowAlertAsync("Alert_Title_Error", "Status_FolderNotExist", Icon.Error);
            return;
        }

        if (string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(Password))
        {
            SetStatus("Status_EmailPasswordEmpty");
            await ShowAlertAsync("Alert_Title_Error", "Status_EmailPasswordEmpty", Icon.Error);
            return;
        }

        IsBusy = true;
        SetStatus("Status_CheckingConnection");

        var tempConfig = new AppConfig { Email = Email, AppPassword = Password };
        if (!await _webDavService.TestConnectionAsync(tempConfig))
        {
            SetStatus("Status_ConnectionFailed");
            await ShowAlertAsync("Alert_Title_Error", "Status_ConnectionFailed", Icon.Error);
            IsBusy = false;
            return;
        }

        SetStatus("Status_CheckingFreeSpace");

        var freeSpace = await _webDavService.GetFreeSpaceAsync(tempConfig);
        var sourceSize = _backupService.GetDirectorySize(SourceFolder);

        if (freeSpace > 0 && sourceSize > freeSpace)
        {
            SetStatusFormat("Status_SpaceWarning", FormatBytes(sourceSize), FormatBytes(freeSpace));
            await Task.Delay(4000);
        }

        var config = new AppConfig
        {
            Email = Email,
            AppPassword = Password,
            SourceFolder = SourceFolder,
            IntervalSeconds = LabelToSeconds(SelectedIntervalKey),
            IsConfigured = true
        };
        config.Save();

        SetStatus("Status_ConfigSaved");

        if (!OperatingSystem.IsWindows())
        {
            _worker.ReloadConfig();
            SetStatus("Status_LinuxManualSetup");
            IsBusy = false;
            return;
        }

        if (!ServiceInstaller.IsRunningAsAdmin())
        {
            SetStatus("Status_AdminRequired");
            ServiceInstaller.RestartAsAdmin(["--install-service-as-admin"]);
            await Task.Delay(1500);
            SetStatus("Status_UacPrompt");
            IsBusy = false;
            return;
        }

        var installed = await _serviceInstaller.InstallServiceAsync();
        if (!installed)
        {
            SetStatus("Status_ServiceInstallError");
            IsBusy = false;
            return;
        }

        var started = await _serviceInstaller.StartServiceAsync();
        SetStatus(started ? "Status_ServiceInstalledOk" : "Status_ServiceInstalledFailed");

        _worker.ReloadConfig();
        IsBusy = false;
    }

    [RelayCommand]
    private async Task UninstallService()
    {
        IsBusy = true;

        if (!OperatingSystem.IsWindows())
        {
            SetStatus("Status_ServiceLinuxUninstallHint");
            IsBusy = false;
            await ShowAlertAsync("Alert_Title_Warning", "Status_ServiceLinuxUninstallHint", Icon.Warning);
            return;
        }

        if (!await _serviceInstaller.IsServiceInstalledAsync())
        {
            SetStatus("Status_ServiceNotInstalled");
            IsBusy = false;
            await ShowAlertAsync("Alert_Title_Warning", "Status_ServiceNotInstalled", Icon.Warning);
            return;
        }

        if (!ServiceInstaller.IsRunningAsAdmin())
        {
            ServiceInstaller.RestartAsAdmin(["--install-service-as-admin"]);
            SetStatus("Status_AdminRequired");
            IsBusy = false;
            return;
        }

        var isSuccessfullyUninstalled = await _serviceInstaller.UninstallServiceAsync();
        SetStatus(isSuccessfullyUninstalled ? "Status_ServiceUninstalled" : "Status_ServiceUninstallError");
        IsBusy = false;
    }

    private async Task LoadExistingConfigAsync()
    {
        var config = AppConfig.Load();
        if (config.IsConfigured)
        {
            Email = config.Email;
            SourceFolder = config.SourceFolder;
            SelectedIntervalKey = SecondsToKey(config.IntervalSeconds);
        }

        if (OperatingSystem.IsWindows())
        {
            if (await _serviceInstaller.IsServiceRunningAsync())
                SetStatus("Status_ServiceAlreadyRunning");
            else if (await _serviceInstaller.IsServiceInstalledAsync())
                SetStatus("Status_ServiceInstalledButStopped");
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private static int LabelToSeconds(string key) => key switch
    {
        "Interval_1Minute" => 60,
        "Interval_30Minutes" => 1800,
        "Interval_1Hour" => 3600,
        "Interval_3Hours" => 10800,
        "Interval_6Hours" => 21600,
        "Interval_12Hours" => 43200,
        "Interval_24Hours" => 86400,
        "Interval_7Days" => 604800,
        "Interval_30Days" => 2592000,
        _ => 3600
    };

    private static string SecondsToKey(int seconds) => seconds switch
    {
        60 => "Interval_1Minute",
        1800 => "Interval_30Minutes",
        3600 => "Interval_1Hour",
        10800 => "Interval_3Hours",
        21600 => "Interval_6Hours",
        43200 => "Interval_12Hours",
        86400 => "Interval_24Hours",
        604800 => "Interval_7Days",
        2592000 => "Interval_30Days",
        _ => "Interval_1Hour"
    };
    
    private static async Task ShowAlertAsync(string titleKey, string messageKey, Icon icon = Icon.Info)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(
            LocalizationService.Get(titleKey),
            LocalizationService.Get(messageKey),
            ButtonEnum.Ok,
            icon);
        await box.ShowAsync();
    }
    
    /// <summary>
    /// Проверяет Email и пароль. Возвращает null, если всё валидно, иначе — ключ локализации с описанием проблемы.
    /// </summary>
    private string? ValidateCredentials()
    {
        var emailEmpty = string.IsNullOrWhiteSpace(Email);
        var passwordEmpty = string.IsNullOrWhiteSpace(Password);

        switch (emailEmpty)
        {
            case true when passwordEmpty:
                return "Status_EmailPasswordEmpty";
            case true:
                return "Status_EmailEmpty";
        }

        if (passwordEmpty)
            return "Status_PasswordEmpty";

        return !IsValidEmail(Email) ? "Status_EmailInvalidFormat" : null;
    }

    /// <summary>
    /// Простая проверка формата Email.
    /// </summary>
    private static bool IsValidEmail(string email) => System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
}