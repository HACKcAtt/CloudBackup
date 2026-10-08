using System.Diagnostics;
using System.Security.Principal;
using CliWrap;
using CliWrap.Buffered;

namespace CloudBackup.Core.Services;

public class ServiceInstaller
{
    public const string ServiceName = "CloudBackupService";
    private const string ServiceDisplayName = "Cloud Mail.ru Backup Service";
    private const string ServiceDescription = "Автоматический бэкап выбранной папки в Облако Mail.ru";

    public static bool IsRunningAsAdmin()
    {
        if (!OperatingSystem.IsWindows())
            return true;

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Перезапускает приложение с правами администратора (появится UAC).
    /// </summary>
    public static void RestartAsAdmin(string[] args)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath))
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = string.Join(" ", args),
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            Process.Start(startInfo);
        }
        catch
        {
            // Пользователь отказался от UAC
        }
    }

    public async Task<bool> IsServiceInstalledAsync()
    {
        if (!OperatingSystem.IsWindows()) return false;

        var result = await Cli.Wrap("sc.exe")
            .WithArguments($"query {ServiceName}")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        return result.ExitCode == 0;
    }

    public async Task<bool> IsServiceRunningAsync()
    {
        if (!OperatingSystem.IsWindows()) return false;

        var result = await Cli.Wrap("sc.exe")
            .WithArguments($"query {ServiceName}")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        return result.StandardOutput.Contains("RUNNING");
    }

    public async Task<bool> InstallServiceAsync()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        if (!IsRunningAsAdmin())
            return false;

        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return false;

        // Удалить старую версию, если есть.
        await Cli.Wrap("sc.exe")
            .WithArguments($"delete {ServiceName}")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        await Task.Delay(1500); // Ждать освобождение имени.

        // Создать новую.
        var createResult = await Cli.Wrap("sc.exe")
            .WithArguments(
                $"create {ServiceName} binpath= \"{exePath}\" start= auto DisplayName= \"{ServiceDisplayName}\"")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (createResult.ExitCode != 0)
            return false;

        // Описание.
        await Cli.Wrap("sc.exe")
            .WithArguments($"description {ServiceName} \"{ServiceDescription}\"")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        // Автоперезапуск при сбое.
        await Cli.Wrap("sc.exe")
            .WithArguments(
                $"failure {ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        return true;
    }

    public async Task<bool> StartServiceAsync()
    {
        if (!OperatingSystem.IsWindows()) return false;

        var result = await Cli.Wrap("sc.exe")
            .WithArguments($"start {ServiceName}")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        return result.ExitCode == 0 || result.StandardOutput.Contains("RUNNING");
    }

    private async Task StopServiceAsync()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await Cli.Wrap("sc.exe")
            .WithArguments($"stop {ServiceName}")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();
    }

    public async Task<bool> UninstallServiceAsync()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        if (!IsRunningAsAdmin())
            return false;

        await StopServiceAsync();
        await Task.Delay(1500);

        var result = await Cli.Wrap("sc.exe")
            .WithArguments($"delete {ServiceName}")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        return result.ExitCode == 0;
    }
}