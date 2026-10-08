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
    /// Создаёт PROPFIND-запрос с авторизацией.
    /// Если <paramref name="requestQuota"/> == true — добавляет в тело запроса
    /// явный запрос <c>quota-available-bytes</c> и <c>quota-used-bytes</c>.
    /// Mail.ru (и многие другие WebDAV-серверы) отдают квоту, только когда её явно запрашивают в теле PROPFIND. Пустой PROPFIND возвращает
    /// лишь обязательные свойства (displayname, resourcetype) без квоты.
    /// </summary>
    private HttpRequestMessage CreatePropfindRequest(AppConfig config, bool requestQuota = false)
    {
        var request = new HttpRequestMessage(
            new HttpMethod(_settings.PropfindMethod),
            _settings.WebDavUrl);

        var authToken = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{config.Email}:{config.AppPassword}"));

        request.Headers.Authorization = new AuthenticationHeaderValue(_settings.AuthScheme, authToken);
        request.Headers.Add("Depth", _settings.DepthHeader);

        if (requestQuota)
        {
            const string propfindBody = """
                                        <?xml version="1.0" encoding="utf-8"?>
                                        <d:propfind xmlns:d="DAV:">
                                          <d:prop>
                                            <d:quota-available-bytes/>
                                            <d:quota-used-bytes/>
                                          </d:prop>
                                        </d:propfind>
                                        """;

            request.Content = new StringContent(propfindBody, Encoding.UTF8, "application/xml");
        }

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
            using var request = CreatePropfindRequest(config, requestQuota: true);
            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return -1;

            var content = await response.Content.ReadAsStringAsync();
            var xml = XDocument.Parse(content);

            var quotaElement = xml.Descendants().FirstOrDefault(x => x.Name.LocalName == _settings.QuotaAvailableElementName);

            return quotaElement != null && long.TryParse(quotaElement.Value, out var freeBytes)
                ? freeBytes
                : -1;
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
                .WithArguments($"use {config.DriveLetter} {_settings.WebDavUrl} /user:{config.Email} {config.AppPassword} /persistent:yes")
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
                    .WithArguments($"-t {_settings.LinuxFileSystemType} {_settings.WebDavUrl} {config.MountPoint}")
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

        var isExist = Directory.Exists(config.MountPoint) && Directory.EnumerateFileSystemEntries(config.MountPoint).Any();

        return Task.FromResult(isExist);
    }
}