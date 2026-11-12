using Business.Abstract;
using Entities.Dtos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace WebAPI.Services
{
    // Günde bir kez eski veriyi temizleyen ve veritabanı yedeğini alan arka plan servisi
    public class MaintenanceBackgroundService : BackgroundService
    {
        private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

        private readonly IMaintenanceService _maintenanceService;
        private readonly IAuditTrailService _auditTrailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MaintenanceBackgroundService> _logger;

        private DateTime _lastCleanupDate = DateTime.MinValue;
        private DateTime _lastFailureLogDate = DateTime.MinValue;

        // Servis bağımlılıklarını alır.
        public MaintenanceBackgroundService(
            IMaintenanceService maintenanceService,
            IAuditTrailService auditTrailService,
            IConfiguration configuration,
            ILogger<MaintenanceBackgroundService> logger)
        {
            _maintenanceService = maintenanceService;
            _auditTrailService = auditTrailService;
            _configuration = configuration;
            _logger = logger;
        }

        // Yarım saatte bir kontrol eder; bakım saati geçtiyse o günün temizliğini ve yedeğini yapar.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(StartDelay, stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await RunIfDueAsync(DateTime.Now);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Bakım görevi hata verdi.");
                    }

                    await Task.Delay(CheckInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Uygulama kapanıyor.
            }
        }

        // Bakım saati geldiyse günde bir kez temizlik yapar ve bugünün yedeği yoksa yedek alır.
        private async Task RunIfDueAsync(DateTime now)
        {
            var options = _configuration.GetSection("Maintenance").Get<MaintenanceOptions>() ?? new MaintenanceOptions();

            if (now.Hour < options.RunHour)
                return;

            if (_lastCleanupDate != now.Date)
            {
                var cleanup = await _maintenanceService.CleanupAsync(options, now);
                if (cleanup.IsSuccess)
                {
                    _lastCleanupDate = now.Date;
                    _logger.LogInformation("{Message}", cleanup.Message);

                    if (cleanup.Data > 0)
                        await RecordAsync("MAINTENANCE_CLEANUP", cleanup.Message);
                }
                else
                {
                    await RecordFailureAsync("MAINTENANCE_CLEANUP_FAILED", cleanup.Message, now);
                }
            }

            var backup = await _maintenanceService.BackupAsync(options, now);
            if (!backup.IsSuccess)
            {
                await RecordFailureAsync("BACKUP_FAILED", backup.Message, now);
            }
            else if (backup.Data)
            {
                _logger.LogInformation("{Message}", backup.Message);
                await RecordAsync("BACKUP", backup.Message);
            }
        }

        // Başarısız bakımı konsola her seferinde, işlem kaydına günde bir kez yazar.
        private async Task RecordFailureAsync(string actionType, string message, DateTime now)
        {
            _logger.LogError("{Message}", message);

            if (_lastFailureLogDate != now.Date)
            {
                _lastFailureLogDate = now.Date;
                await RecordAsync(actionType, message);
            }
        }

        // Bakım sonucunu sistem adına işlem kaydına yazar; kayıt hatası yok sayılır.
        private async Task RecordAsync(string actionType, string message)
        {
            try
            {
                await _auditTrailService.LogActionAsync(0, actionType, $"Sistem: {message}", null, null, "local");
            }
            catch
            {
                // İşlem kaydı yazılamasa da bakım görevi çalışmaya devam etmelidir.
            }
        }
    }
}