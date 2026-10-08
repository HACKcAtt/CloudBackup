using CloudBackup.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudBackup.Core.Services;

public class BackupWorker : BackgroundService
{
    private readonly ILogger<BackupWorker> _logger;
    private readonly IWebDavService _webDavService;
    private readonly IBackupService _backupService;
    private AppConfig _config;

    public BackupWorker(
        ILogger<BackupWorker> logger,
        IWebDavService webDavService,
        IBackupService backupService)
    {
        _logger = logger;
        _webDavService = webDavService;
        _backupService = backupService;
        _config = AppConfig.Load();
    }

    /// <summary>
    /// Принудительно перечитать конфиг из файла (вызывается после настройки через UI).
    /// </summary>
    public void ReloadConfig()
    {
        _config = AppConfig.Load();
        _logger.LogInformation("Конфиг перезагружен.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BackupWorker запущен.");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Если конфиг не настроен, то ждать.
            if (!_config.IsConfigured)
            {
                _logger.LogWarning("Конфигурация не найдена. Ожидание настройки через UI...");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                _config = AppConfig.Load();
                continue;
            }

            try
            {
                _logger.LogInformation("=== Запуск бэкапа: {time} ===", DateTime.Now);

                // Проверка монтирования.
                if (!await _webDavService.IsDriveMountedAsync(_config))
                {
                    _logger.LogWarning("Диск не смонтирован. Попытка монтирования...");
                    if (!await _webDavService.MountDriveAsync(_config))
                    {
                        _logger.LogError("Не удалось смонтировать диск. Повтор через 1 минуту.");
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                        continue;
                    }
                }

                // Проверка места.
                var freeSpace = await _webDavService.GetFreeSpaceAsync(_config);
                if (!await _backupService.CanBackupAsync(_config, freeSpace))
                {
                    _logger.LogError(
                        "Недостаточно места. Свободно: {free} MB. Бэкап отменён.",
                        freeSpace / 1024 / 1024);
                }
                else
                {
                    await _backupService.PerformBackupAsync(_config);
                    _logger.LogInformation("Бэкап успешно завершён.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в процессе бэкапа.");
            }

            _logger.LogInformation("Ожидание {interval} до следующего запуска...", _config.Interval);
            await Task.Delay(_config.Interval, stoppingToken);
        }
    }
}