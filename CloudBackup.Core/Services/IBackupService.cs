using CloudBackup.Core.Models;

namespace CloudBackup.Core.Services;

public interface IBackupService
{
    Task<bool> CanBackupAsync(AppConfig config, long freeSpace);
    Task PerformBackupAsync(AppConfig config);
    long GetDirectorySize(string path);
}