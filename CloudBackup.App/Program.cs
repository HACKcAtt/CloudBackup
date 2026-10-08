using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using CloudBackup.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;

namespace CloudBackup.App;

internal static class Program
{
    private const string AdminInstallFlag = "--install-service-as-admin";
    private const string AdminUninstallFlag = "--uninstall-service-as-admin";

    [STAThread]
    public static void Main(string[] args)
    {
        // Тихая установка службы (после UAC-перезапуска).
        if (args.Any(a => a == AdminInstallFlag))
        {
            RunServiceInstallation().GetAwaiter().GetResult();
            return;
        }

        // Тихая деинсталляция службы (после UAC-перезапуска).
        if (args.Any(a => a == AdminUninstallFlag))
        {
            RunServiceUninstallation().GetAwaiter().GetResult();
            return;
        }

        var host = BuildHost(args);
        App.HostInstance = host;

        // Режим Windows-службы: работа в фоне, без UI.
        if (WindowsServiceHelpers.IsWindowsService())
        {
            host.Run();
            return;
        }

        // UI-режим на Windows: НЕ запускать BackupWorker в этом процессе. Фоновую работу выполняет отдельный процесс-служба, запущенный через SCM.
        if (OperatingSystem.IsWindows())
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return;
        }

        // UI-режим на Linux: запуска фона в этом же процессе (нет systemd-интеграции).
        _ = host.StartAsync();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static async Task RunServiceInstallation()
    {
        Console.WriteLine("Установка службы CloudBackupService...");

        var installer = new ServiceInstaller();
        var isInstalled = await installer.InstallServiceAsync();

        if (isInstalled)
        {
            await ServiceInstaller.StartServiceAsync();
            Console.WriteLine("Служба успешно установлена и запущена.");
        }
        else
        {
            Console.WriteLine("Ошибка установки службы.");
        }

        await Task.Delay(3000);
    }

    private static async Task RunServiceUninstallation()
    {
        Console.WriteLine("Удаление службы CloudBackupService...");

        var installer = new ServiceInstaller();
        var isUninstalled = await installer.UninstallServiceAsync();

        Console.WriteLine(isUninstalled
            ? "Служба успешно удалена."
            : "Ошибка удаления службы.");

        await Task.Delay(3000);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    private static IHost BuildHost(string[] args)
    {
        // Логи: на Windows в ProgramData, на Linux в ~/.config.
        var logDir = OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "CloudBackup", "logs")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "CloudBackup", "logs");

        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(
                Path.Combine(logDir, "backup-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        var builder = Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IWebDavService, WebDavService>();
                services.AddSingleton<IBackupService, BackupService>();
                services.AddSingleton<BackupWorker>();
                services.AddHostedService(sp => sp.GetRequiredService<BackupWorker>());
            });

        if (OperatingSystem.IsWindows())
            builder.UseWindowsService(options => { options.ServiceName = ServiceInstaller.ServiceName; });

        return builder.Build();
    }
}