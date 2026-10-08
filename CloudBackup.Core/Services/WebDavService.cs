using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using CliWrap;
using CliWrap.Buffered;
using CloudBackup.Core.Models;

namespace CloudBackup.Core.Services;

public class WebDavService : IWebDavService
{
    private readonly HttpClient _httpClient;
    private readonly WebDavSettings _settings;

    public WebDavService()
    {
        _settings = WebDavSettings.Load();
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds)
        };
    }

    /// <summary>
    /// Создает PROPFIND-запрос с авторизацией (DRY: используется в TestConnection и GetFreeSpace).
    /// </summary>
    private HttpRequestMessage CreatePropfindRequest(AppConfig config, string? url = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(_settings.PropfindMethod), url ?? _settings.WebDavUrl);

        var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.Email}:{config.AppPassword}"));

        request.Headers.Authorization = new AuthenticationHeaderValue(_settings.AuthScheme, authToken);
        request.Headers.Add("Depth", _settings.DepthHeader);

        return request;
    }

    public async Task<bool> TestConnectionAsync(AppConfig config)
    {
        try
        {
            using var request = CreatePropfindRequest(config);
            using var response = await _httpClient.SendAsync(request);
            return (int)response.StatusCode == 207;
        }
        catch
        {
            return false;
        }
    }

    public async Task<long> GetFreeSpaceAsync(AppConfig config)
    {
        try
        {
            using var request = CreatePropfindRequest(config);
            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return -1;

            var content = await response.Content.ReadAsStringAsync();
            var xml = XDocument.Parse(content);

            var quotaElement = xml.Descendants()
                .FirstOrDefault(x => x.Name.LocalName == _settings.QuotaAvailableElementName);

            if (quotaElement != null && long.TryParse(quotaElement.Value, out var freeBytes))
                return freeBytes;

            return -1;
        }
        catch
        {
            return -1;
        }
    }

    public async Task<bool> MountDriveAsync(AppConfig config)
    {
        if (OperatingSystem.IsWindows())
        {
            var result = await Cli.Wrap(_settings.WindowsMountCommand)
                .WithArguments(
                    $"use {config.DriveLetter} {_settings.WebDavUrl} /user:{config.Email} {config.AppPassword} /persistent:yes")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            return result.ExitCode == 0;
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                Directory.CreateDirectory(config.MountPoint);
                var result = await Cli.Wrap(_settings.LinuxMountCommand)
                    .WithArguments(
                        $"-t {_settings.LinuxFileSystemType} {_settings.WebDavUrl} {config.MountPoint}")
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync();

                return result.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    public async Task<bool> UnmountDriveAsync(AppConfig config)
    {
        if (OperatingSystem.IsWindows())
        {
            var result = await Cli.Wrap(_settings.WindowsMountCommand)
                .WithArguments($"use {config.DriveLetter} /delete")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            return result.ExitCode == 0;
        }
        
        if (OperatingSystem.IsLinux())
        {
            var result = await Cli.Wrap(_settings.LinuxUnmountCommand)
                .WithArguments(config.MountPoint)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            return result.ExitCode == 0;
        }

        return false;
    }

    public Task<bool> IsDriveMountedAsync(AppConfig config)
    {
        if (OperatingSystem.IsWindows())
            return Task.FromResult(Directory.Exists(config.DriveLetter + "\\"));

        if (!OperatingSystem.IsLinux())
            return Task.FromResult(false);

        var exists = Directory.Exists(config.MountPoint) && Directory.EnumerateFileSystemEntries(config.MountPoint).Any();

        return Task.FromResult(exists);
    }
}