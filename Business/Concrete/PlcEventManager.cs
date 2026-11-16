using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Abstract;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Entities.Enums;
using Entities.Structures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Business.Concrete
{
    // PLC durumundaki olayları izler: saat eşitleme, kaçak testi sonucu ve PID otomatik ayar sonucu
    public class PlcEventManager : IPlcEventService
    {
        private const int ClockSyncHours = 24;          // Saat eşitleme aralığı
        private const int ClockRetryMinutes = 5;        // Başarısız eşitlemeden sonra yeniden deneme
        private const int LeakParamOffset = 96;         // DB204'te kaçak testi parametrelerinin başlangıcı
        private const int LeakParamSize = 12;

        private readonly IS7DriverDal _s7DriverDal;
        private readonly IPlcService _plcService;
        private readonly IAuditTrailService _auditTrailService;
        private readonly IConfiguration _configuration;

        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private volatile bool _clockDue = true;         // Bağlantıdan sonra saat eşitlenmeli
        private DateTime _lastClockSync = DateTime.MinValue;
        private DateTime _clockRetryAfter = DateTime.MinValue;

        private bool _leakInitialized;
        private short _lastLeakCount;

        private bool _tuneInitialized;
        private short _lastTuneStatus;
        private string? _gainsBeforeTune;

        // Şimdiki zamanı veren işlev; testlerde zamanı ilerletebilmek için değiştirilebilir.
        public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

        // Sürücü, PLC, işlem kaydı ve ayar bağımlılıklarını alır.
        public PlcEventManager(IS7DriverDal s7DriverDal, IPlcService plcService, IAuditTrailService auditTrailService, IConfiguration configuration)
        {
            _s7DriverDal = s7DriverDal;
            _plcService = plcService;
            _auditTrailService = auditTrailService;
            _configuration = configuration;
        }

        // PLC'ye (yeniden) bağlanıldığını işaretler; bir sonraki izlemede saat eşitlenir.
        public void OnConnected()
        {
            _clockDue = true;
        }

        // Saat eşitleme, kaçak testi ve PID ayar izlemesini sırayla yapar; biri hata verse de diğerleri çalışır.
        public async Task<IResult> TrackAsync(ScadaStatus status)
        {
            await _lock.WaitAsync();
            try
            {
                var messages = new List<string>();
                var errors = new List<string>();

                await RunPartAsync(() => SyncClockIfDueAsync(), messages, errors);
                await RunPartAsync(() => TrackLeakTestAsync(status), messages, errors);
                await RunPartAsync(() => TrackPidTuneAsync(status), messages, errors);

                if (errors.Count > 0)
                    return new ErrorResult(string.Join(" ", errors.Concat(messages)));

                return new SuccessResult(string.Join(" ", messages));
            }
            finally
            {
                _lock.Release();
            }
        }

        // Verilen tarih aralığındaki kaçak testi kayıtlarını yeniden eskiye listeler.
        public async Task<IDataResult<List<LeakTestRecord>>> GetLeakTestsAsync(DateTime start, DateTime end)
        {
            try
            {
                await using var context = new NorthwindContext();

                var list = await context.LeakTestRecords
                    .AsNoTracking()
                    .Where(l => l.Timestamp >= start && l.Timestamp <= end)
                    .OrderByDescending(l => l.Timestamp)
                    .ToListAsync();

                return new SuccessDataResult<List<LeakTestRecord>>(list);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<List<LeakTestRecord>>($"Kaçak testi kayıtları okunamadı: {ex.Message}");
            }
        }

        // İzleme parçasını çalıştırır; mesajını ya da hatasını ilgili listeye ekler.
        private static async Task RunPartAsync(Func<Task<string?>> part, List<string> messages, List<string> errors)
        {
            try
            {
                string? message = await part();
                if (!string.IsNullOrEmpty(message))
                    messages.Add(message);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }

        // Bağlantıdan sonra ve 24 saatte bir PLC saatini bilgisayarın yerel saatine ayarlar (komut 12).
        private async Task<string?> SyncClockIfDueAsync()
        {
            if (string.Equals(_configuration["Plc:ClockSync"], "false", StringComparison.OrdinalIgnoreCase))
                return null;

            DateTime now = Clock();

            bool due = _clockDue || (now - _lastClockSync).TotalHours >= ClockSyncHours;
            if (!due || now < _clockRetryAfter)
                return null;

            // PLC yerel saat bekler: 1970'ten beri saniye, saat dilimi dönüşümü yapılmadan.
            int unixLocal = (int)(now - new DateTime(1970, 1, 1)).TotalSeconds;
            short low = unchecked((short)(unixLocal & 0xFFFF));
            short high = unchecked((short)(unixLocal >> 16));

            var result = await _plcService.SendCommandAsync(ScadaCommandCode.SetClock, low, high);
            if (!result.IsSuccess)
            {
                _clockRetryAfter = now.AddMinutes(ClockRetryMinutes);
                throw new InvalidOperationException($"PLC saati eşitlenemedi: {result.Message}");
            }

            _clockDue = false;
            _lastClockSync = now;
            return $"PLC saati eşitlendi: {now:yyyy-MM-dd HH:mm:ss}.";
        }

        // LeakCount değiştiğinde sonuçlanan kaçak testini, test anındaki parametrelerle birlikte kaydeder.
        private async Task<string?> TrackLeakTestAsync(ScadaStatus status)
        {
            // İlk okumada sayaç yalnızca öğrenilir; SCADA açılmadan önceki testler kaydedilmez.
            if (!_leakInitialized)
            {
                _leakInitialized = true;
                _lastLeakCount = status.LeakCount;
                return null;
            }

            if (status.LeakCount == _lastLeakCount)
                return null;

            int testMin = 0;
            float maxRise = 0f;
            int vacTarget = 0;

            try
            {
                var parameters = await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbParam, LeakParamOffset, LeakParamSize);
                vacTarget = S7Bytes.GetInt(parameters, 2);          // LeakVacTarget (adres 98)
                testMin = S7Bytes.GetInt(parameters, 8);            // LeakTestMin (adres 104)
                maxRise = S7Bytes.GetInt(parameters, 10) / 10f;     // LeakMaxRise (adres 106, mbar x10)
            }
            catch
            {
                // Parametreler okunamasa da testin sonucu kaydedilir.
            }

            var record = new LeakTestRecord
            {
                Timestamp = Clock(),
                Result = status.LeakResult,
                ResultText = DescribeLeakResult(status.LeakResult),
                RiseMbar = status.LeakRise,
                RiseRateMbarPerMin = testMin > 0 ? status.LeakRise / testMin : 0f,
                TestMin = testMin,
                MaxRiseMbar = maxRise,
                VacTargetMbar = vacTarget,
                TempPota = status.TempPota
            };

            await using (var context = new NorthwindContext())
            {
                await context.LeakTestRecords.AddAsync(record);
                await context.SaveChangesAsync();
            }

            // Sayaç yalnızca kayıt başarılıysa güncellenir; hata olursa bir sonraki izlemede yeniden denenir.
            _lastLeakCount = status.LeakCount;

            return $"Kaçak testi sonuçlandı: {record.ResultText}, artış {record.RiseMbar:0.0} mbar.";
        }

        // PID otomatik ayarı bittiğinde eski ve yeni PID değerlerini işlem kaydına yazar.
        private async Task<string?> TrackPidTuneAsync(ScadaStatus status)
        {
            bool running = status.TuneStatus == 1 || status.TuneStatus == 2;

            if (!_tuneInitialized)
            {
                _tuneInitialized = true;
                _lastTuneStatus = status.TuneStatus;
                if (running)
                    _gainsBeforeTune = FormatGains(status);
                return null;
            }

            if (status.TuneStatus == _lastTuneStatus)
                return null;

            bool wasRunning = _lastTuneStatus == 1 || _lastTuneStatus == 2;
            string? message = null;

            if (running && !wasRunning)
            {
                // Ayar başladı: karşılaştırma için o anki değerler saklanır.
                _gainsBeforeTune = FormatGains(status);
            }
            else if (wasRunning && (status.TuneStatus == 3 || status.TuneStatus == 4))
            {
                bool success = status.TuneStatus == 3;
                string after = FormatGains(status);

                message = success
                    ? $"PID otomatik ayarı başarılı. Yeni değerler: {after}."
                    : $"PID otomatik ayarı başarısız ya da iptal edildi (PID hata kodu {status.PidError}).";

                await _auditTrailService.LogActionAsync(
                    0,
                    success ? "PID_TUNE_OK" : "PID_TUNE_FAILED",
                    $"Sistem: {message}",
                    _gainsBeforeTune,
                    after,
                    "local");
            }

            _lastTuneStatus = status.TuneStatus;
            return message;
        }

        // PID değerlerini işlem kaydında okunabilir tek satıra çevirir.
        private static string FormatGains(ScadaStatus status)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Kp={0:0.####}; Ti={1:0.##} sn; Td={2:0.##} sn", status.PidGain, status.PidTi, status.PidTd);
        }

        // Kaçak testi sonuç kodunu operatöre gösterilecek metne çevirir.
        private static string DescribeLeakResult(int result)
        {
            return result switch
            {
                1 => "Geçti",
                2 => "Kaldı (artış sınırın üstünde)",
                3 => "Hedef vakuma inilemedi",
                4 => "Operatör iptal etti",
                5 => "İzin kalktı (kapak, acil stop, alarm, motor arızası)",
                6 => "Basınç sensörü arızası",
                7 => "Sıcaklık sınırı aşıldı",
                _ => $"Bilinmeyen sonuç ({result})"
            };
        }
    }
}