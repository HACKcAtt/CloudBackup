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
            // Пользователь отказался от UAC.
        }
    }

    public static async Task<bool> IsServiceInstalledAsync()
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

        var state = await GetServiceStateAsync();
        return state == "Running";
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

        await Task.Delay(1500); // Ждать освобождения имени.

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

    public static async Task<bool> StartServiceAsync()
    {
        if (!OperatingSystem.IsWindows()) return false;

        var result = await Cli.Wrap("sc.exe")
            .WithArguments($"start {ServiceName}")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        return result.ExitCode == 0 || result.StandardOutput.Contains("RUNNING");
    }

    public async Task<bool> UninstallServiceAsync()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        if (!IsRunningAsAdmin())
            return false;

        // Остановить и дождаться остановки.
        var stopped = await StopServiceAndWaitAsync();
        if (!stopped)
            return false;

        // Удалить.
        var result = await Cli.Wrap("sc.exe")
            .WithArguments($"delete {ServiceName}")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (result.ExitCode != 0)
            return false;

        // Убедиться, что служба действительно исчезла.
        // Иногда sc delete возвращает 0, но служба в статусе DELETE_PENDING.
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(500);
            if (!await IsServiceInstalledAsync())
                return true;
        }

        return false;
    }

    /// <summary>
    /// Останавливает службу и ждёт, пока она перейдёт в состояние "Stopped".
    /// Если за 10 секунд не остановилась — принудительно убивает процесс.
    /// </summary>
    private async Task<bool> StopServiceAndWaitAsync()
    {
        // Отправка команды stop.
        await Cli.Wrap("sc.exe")
            .WithArguments($"stop {ServiceName}")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        // Ожидание до 10 секунд с проверкой состояния через WMI (локализационно-независимо).
        for (var i = 0; i < 20; i++)
        {
            var state = await GetServiceStateAsync();
            if (state == "Stopped")
                return true;

            await Task.Delay(500);
        }

        // Если не остановилась, то принудительное убийство процесса.
        var pid = await GetServiceProcessIdAsync();
        if (pid > 0)
        {
            await Cli.Wrap("taskkill")
                .WithArguments($"/PID {pid} /F")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            // Дать системе освободить handle.
            await Task.Delay(2000);
        }

        return true;
    }

    /// <summary>
    /// Возвращает состояние службы через WMI: "Running", "Stopped", "Stop Pending" и т.д.
    /// Локализационно-независимый способ — не парсит вывод sc.exe.
    /// </summary>
    private async Task<string> GetServiceStateAsync()
    {
        try
        {
            var result = await Cli.Wrap("powershell")
                .WithArguments(
                    $"-NoProfile -Command \"(Get-CimInstance Win32_Service -Filter \\\"Name='{ServiceName}'\\\").State\"")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            return result.StandardOutput.Trim();
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// Возвращает PID процесса службы через WMI или 0, если не удалось.
    /// </summary>
    private static async Task<int> GetServiceProcessIdAsync()
    {
        try
        {
            var result = await Cli.Wrap("powershell")
                .WithArguments(
                    $"-NoProfile -Command \"(Get-CimInstance Win32_Service -Filter \\\"Name='{ServiceName}'\\\").ProcessId\"")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            return int.TryParse(result.StandardOutput.Trim(), out var pid) ? pid : 0;
        }
        catch
        {
            return 0;
        }
    }
}