using CloudBackup.Core.Models;

namespace CloudBackup.Core.Services;

public class BackupService : IBackupService
{
    private readonly WebDavSettings _settings;

    public BackupService()
    {
        _settings = WebDavSettings.Load();
    }

    public Task<bool> CanBackupAsync(AppConfig config, long freeSpace)
    {
        if (freeSpace == -1)
            return Task.FromResult(true);

        var sourceSize = GetDirectorySize(config.SourceFolder);
        return Task.FromResult(sourceSize <= freeSpace);
    }

    public async Task PerformBackupAsync(AppConfig config)
    {
        var sourceName = new DirectoryInfo(config.SourceFolder).Name;
        if (string.IsNullOrEmpty(sourceName))
            sourceName = "backup";

        var targetRoot = OperatingSystem.IsWindows()
            ? Path.Combine(config.DriveLetter + "\\", _settings.BackupSubfolderName)
            : Path.Combine(config.MountPoint, _settings.BackupSubfolderName);

        var targetPath = Path.Combine(targetRoot, sourceName);

        // CreateDirectory НЕ очищает папку, если она уже есть, то гарантирует, что она существует.
        Directory.CreateDirectory(targetPath);

        await CopyDirectoryAsync(config.SourceFolder, targetPath);
    }

    private static async Task CopyDirectoryAsync(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);   // Не трогает существующее.

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var targetFile = Path.Combine(targetDir, fileName);

            var shouldCopy = true;
            if (File.Exists(targetFile))
            {
                var sourceInfo = new FileInfo(file);
                var targetInfo = new FileInfo(targetFile);

                if (sourceInfo.LastWriteTime <= targetInfo.LastWriteTime && sourceInfo.Length == targetInfo.Length)
                    shouldCopy = false;   // Пропуск идентичных файлов.
            }

            if (shouldCopy)
            {
                try
                {
                    File.Copy(file, targetFile, true);
                }
                catch (IOException) { /* файл занят — пропускаем */ }
            }
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(directory);
            var targetSubDir = Path.Combine(targetDir, dirName);
            await CopyDirectoryAsync(directory, targetSubDir);
        }
    }

    public long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;

        long size = 0;
        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; }
                catch { /* файл удален */ }
            }
        }
        catch { /* нет доступа */ }

        return size;
    }
}