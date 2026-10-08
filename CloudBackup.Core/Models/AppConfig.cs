using System.Text.Json;

namespace CloudBackup.Core.Models;

public class AppConfig
{
    // Безопасные поля (в app-settings.json).
    public string SourceFolder { get; set; } = string.Empty;
    public string DriveLetter { get; set; } = "Z:";
    public string MountPoint { get; set; } = "/mnt/mailru_cloud";
    public int IntervalSeconds { get; set; } = 3600;
    public bool IsConfigured { get; set; }

    // Чувствительные поля (в secrets.json, не коммитятся).
    public string Email { get; set; } = string.Empty;
    public string AppPassword { get; set; } = string.Empty;

    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);

    private static string ConfigDir => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CloudBackup")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "CloudBackup");

    private static string AppSettingsPath => Path.Combine(ConfigDir, "app-settings.json");
    private static string SecretsPath => Path.Combine(ConfigDir, "secrets.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppConfig Load()
    {
        var settings = WebDavSettings.Load();
        var config = new AppConfig();

        try
        {
            if (File.Exists(AppSettingsPath))
            {
                var json = File.ReadAllText(AppSettingsPath);
                var fromFile = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (fromFile is not null)
                {
                    config.SourceFolder = fromFile.SourceFolder;
                    config.DriveLetter = fromFile.DriveLetter;
                    config.MountPoint = fromFile.MountPoint;
                    config.IntervalSeconds = fromFile.IntervalSeconds;
                    config.IsConfigured = fromFile.IsConfigured;
                }
            }
        }
        catch
        {
            /* ignore, используем дефолты */
        }

        try
        {
            if (File.Exists(SecretsPath))
            {
                var json = File.ReadAllText(SecretsPath);
                var fromFile = JsonSerializer.Deserialize<Secrets>(json, JsonOptions);
                if (fromFile is not null)
                {
                    config.Email = fromFile.Email;
                    config.AppPassword = fromFile.AppPassword;
                }
            }
        }
        catch
        {
            /* ignore */
        }

        if (string.IsNullOrWhiteSpace(config.DriveLetter))
            config.DriveLetter = settings.DefaultWindowsDriveLetter;

        if (string.IsNullOrWhiteSpace(config.MountPoint))
            config.MountPoint = settings.DefaultLinuxMountPoint;

        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);

        // app-settings.json — безопасный.
        var appSettings = new AppSettings
        {
            SourceFolder = SourceFolder,
            DriveLetter = DriveLetter,
            MountPoint = MountPoint,
            IntervalSeconds = IntervalSeconds,
            IsConfigured = IsConfigured
        };
        File.WriteAllText(AppSettingsPath,
            JsonSerializer.Serialize(appSettings, JsonOptions));

        // secrets.json — только для локального хранения.
        var secrets = new Secrets
        {
            Email = Email,
            AppPassword = AppPassword
        };
        File.WriteAllText(SecretsPath,
            JsonSerializer.Serialize(secrets, JsonOptions));

        // На Unix сразу ставим права 600 (только владелец может читать).
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(SecretsPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
                /* если не получилось — не критично */
            }
        }
    }

    private class AppSettings
    {
        public string SourceFolder { get; set; } = string.Empty;
        public string DriveLetter { get; set; } = "Z:";
        public string MountPoint { get; set; } = "/mnt/mailru_cloud";
        public int IntervalSeconds { get; set; } = 3600;
        public bool IsConfigured { get; set; }
    }

    private class Secrets
    {
        public string Email { get; set; } = string.Empty;
        public string AppPassword { get; set; } = string.Empty;
    }
}