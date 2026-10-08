using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CloudBackup.App.ViewModels;
using CloudBackup.App.Views;
using CloudBackup.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CloudBackup.App;

public partial class App : Application
{
    public static IHost? HostInstance { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var host = HostInstance;
            if (host == null)
                throw new InvalidOperationException(
                    "HostInstance не установлен. Проверьте Program.Main.");

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(
                    host.Services.GetRequiredService<IWebDavService>(),
                    host.Services.GetRequiredService<IBackupService>(),
                    host.Services.GetRequiredService<BackupWorker>(),
                    new ServiceInstaller())
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}