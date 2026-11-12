using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Dtos;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Business.Concrete
{
    public class MaintenanceManager : IMaintenanceService
    {
        private const string BackupPrefix = "rotascada_";
        private const int BackupTimeoutMinutes = 10;

        // Saklama süresi dolan ölçüm, alarm ve işlem kayıtlarını siler.
        public async Task<IDataResult<int>> CleanupAsync(MaintenanceOptions options, DateTime now)
        {
            try
            {
                await using var context = new NorthwindContext();

                int idleLogs = 0, batchLogs = 0, alarms = 0, audits = 0;

                if (options.IdleLogKeepDays > 0)
                {
                    DateTime cutoff = now.AddDays(-options.IdleLogKeepDays);
                    idleLogs = await context.ProcessDataLogs
                        .Where(p => p.BatchId == null && p.Timestamp < cutoff)
                        .ExecuteDeleteAsync();
                }

                if (options.BatchLogKeepDays > 0)
                {
                    DateTime cutoff = now.AddDays(-options.BatchLogKeepDays);
                    batchLogs = await context.ProcessDataLogs
                        .Where(p => p.BatchId != null && p.Timestamp < cutoff)
                        .ExecuteDeleteAsync();
                }

                if (options.AlarmKeepDays > 0)
                {
                    // Süren alarmlar ne kadar eski olursa olsun silinmez.
                    DateTime cutoff = now.AddDays(-options.AlarmKeepDays);
                    alarms = await context.Alarms
                        .Where(a => !a.IsActive && a.TriggeredAt < cutoff)
                        .ExecuteDeleteAsync();
                }

                if (options.AuditKeepDays > 0)
                {
                    DateTime cutoff = now.AddDays(-options.AuditKeepDays);
                    audits = await context.AuditLogs
                        .Where(l => l.Timestamp < cutoff)
                        .ExecuteDeleteAsync();
                }

                int total = idleLogs + batchLogs + alarms + audits;
                return new SuccessDataResult<int>(total,
                    $"Temizlik: {idleLogs} şarj dışı ölçüm, {batchLogs} şarj ölçümü, {alarms} alarm, {audits} işlem kaydı silindi.");
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<int>(0, $"Temizlik yapılamadı: {ex.Message}");
            }
        }

        // Bugünün yedeği yoksa mysqldump ile yedek alır ve fazla eski yedekleri siler.
        public async Task<IDataResult<bool>> BackupAsync(MaintenanceOptions options, DateTime now)
        {
            if (!options.BackupEnabled)
                return new SuccessDataResult<bool>(false, "Yedekleme kapalı.");

            if (string.IsNullOrWhiteSpace(options.BackupFolder))
                return new ErrorDataResult<bool>(false, "Yedek klasörü (Maintenance:BackupFolder) tanımlı değil.");

            if (!File.Exists(options.MysqldumpPath))
                return new ErrorDataResult<bool>(false, $"mysqldump bulunamadı: {options.MysqldumpPath}");

            string? credentialFile = null;
            string? tempFile = null;

            try
            {
                Directory.CreateDirectory(options.BackupFolder);

                string targetFile = Path.Combine(options.BackupFolder, $"{BackupPrefix}{now:yyyyMMdd}.sql");
                if (File.Exists(targetFile))
                    return new SuccessDataResult<bool>(false, "Bugünün yedeği zaten alınmış.");

                var connection = new MySqlConnectionStringBuilder(NorthwindContext.ConnectionString);

                credentialFile = WriteCredentialFile(connection);
                tempFile = targetFile + ".tmp";

                var startInfo = new ProcessStartInfo
                {
                    FileName = options.MysqldumpPath,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add($"--defaults-extra-file={credentialFile}");   // İlk parametre olmak zorunda
                startInfo.ArgumentList.Add($"--host={connection.Server}");
                startInfo.ArgumentList.Add($"--port={connection.Port}");
                startInfo.ArgumentList.Add("--single-transaction");                      // Tabloları kilitlemeden tutarlı yedek
                startInfo.ArgumentList.Add("--no-tablespaces");                          // Ek yetki gerektirmesin
                startInfo.ArgumentList.Add($"--result-file={tempFile}");
                startInfo.ArgumentList.Add(connection.Database);

                using var process = Process.Start(startInfo);
                if (process == null)
                    return new ErrorDataResult<bool>(false, "mysqldump başlatılamadı.");

                var errorTask = process.StandardError.ReadToEndAsync();

                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(BackupTimeoutMinutes));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(true);
                    return new ErrorDataResult<bool>(false, $"Yedekleme {BackupTimeoutMinutes} dakikada bitmedi, durduruldu.");
                }

                string errorText = await errorTask;

                if (process.ExitCode != 0 || !File.Exists(tempFile) || new FileInfo(tempFile).Length == 0)
                    return new ErrorDataResult<bool>(false, $"Yedek alınamadı: {errorText.Trim()}");

                File.Move(tempFile, targetFile);
                tempFile = null;

                int removed = RemoveOldBackups(options.BackupFolder, options.BackupKeepCount);
                long sizeKb = new FileInfo(targetFile).Length / 1024;

                return new SuccessDataResult<bool>(true,
                    $"Yedek alındı: {targetFile} ({sizeKb} KB). {removed} eski yedek silindi.");
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<bool>(false, $"Yedek alınamadı: {ex.Message}");
            }
            finally
            {
                TryDelete(credentialFile);
                TryDelete(tempFile);
            }
        }

        // Kullanıcı adı ve şifreyi mysqldump'ın okuyacağı geçici bir dosyaya yazar; dosyanın yolunu döner.
        private static string WriteCredentialFile(MySqlConnectionStringBuilder connection)
        {
            string path = Path.Combine(Path.GetTempPath(), $"rs_{Guid.NewGuid():N}.cnf");
            string password = (connection.Password ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

            File.WriteAllText(path, $"[client]\nuser={connection.UserID}\npassword=\"{password}\"\n");
            return path;
        }

        // En yeni yedekleri saklayıp fazlasını siler; silinen dosya sayısını döner.
        private static int RemoveOldBackups(string folder, int keepCount)
        {
            if (keepCount <= 0)
                return 0;

            var oldFiles = Directory.GetFiles(folder, $"{BackupPrefix}*.sql")
                .OrderByDescending(f => Path.GetFileName(f))
                .Skip(keepCount)
                .ToList();

            foreach (var file in oldFiles)
                TryDelete(file);

            return oldFiles.Count;
        }

        // Dosya varsa siler; silme hatası yok sayılır.
        private static void TryDelete(string? path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Geçici dosya silinemese de bakım sonucu etkilenmemelidir.
            }
        }
    }
}