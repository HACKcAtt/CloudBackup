using CloudBackup.Core.Models;

namespace CloudBackup.Core.Services;

public interface IBackupService
{
    Task<bool> CanBackupAsync(AppConfig config, long freeSpace);

    /// <summary>
    /// Выполняет бэкап. <paramref name="cancellationToken"/> позволяет прервать
    /// копирование (например, при остановке службы через <c>sc stop</c>).
    /// </summary>
    Task PerformBackupAsync(AppConfig config, CancellationToken cancellationToken = default);

    long GetDirectorySize(string path);
}