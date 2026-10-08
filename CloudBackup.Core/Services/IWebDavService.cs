using CloudBackup.Core.Models;

namespace CloudBackup.Core.Services;

public interface IWebDavService
{
    Task<bool> TestConnectionAsync(AppConfig config);
    Task<long> GetFreeSpaceAsync(AppConfig config);
    Task<bool> MountDriveAsync(AppConfig config);
    Task<bool> UnmountDriveAsync(AppConfig config);
    Task<bool> IsDriveMountedAsync(AppConfig config);
}