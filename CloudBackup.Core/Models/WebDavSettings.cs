using System.Text.Json;

namespace CloudBackup.Core.Models;

/// <summary>
/// Настройки интеграции с WebDAV. Загружаются из webdav-settings.json.
/// </summary>
public class WebDavSettings
{
    public string WebDavUrl { get; set; } = "https://webdav.cloud.mail.ru";
    public string DefaultWindowsDriveLetter { get; set; } = "Z:";
    public string DefaultLinuxMountPoint { get; set; } = "/mnt/mailru_cloud";
    public string PropfindMethod { get; set; } = "PROPFIND";
    public string DepthHeader { get; set; } = "0";
    public string QuotaAvailableElementName { get; set; } = "quota-available-bytes";
    public int RequestTimeoutSeconds { get; set; } = 30;
    public string BackupSubfolderName { get; set; } = "Backup";
    public string AuthScheme { get; set; } = "Basic";
    public string WindowsMountCommand { get; set; } = "net";
    public string LinuxMountCommand { get; set; } = "mount";
    public string LinuxUnmountCommand { get; set; } = "umount";
    public string LinuxFileSystemType { get; set; } = "davfs";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Загружает настройки из webdav-settings.json рядом с исполняемым файлом.
    /// Если файл не найден или битый — возвращает значения по умолчанию.
    /// </summary>
    public static WebDavSettings Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Config", "webdav-settings.json");
            if (!File.Exists(path))
                return new WebDavSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<WebDavSettings>(json, JsonOptions) ?? new WebDavSettings();
        }
        catch
        {
            return new WebDavSettings();
        }
    }
}