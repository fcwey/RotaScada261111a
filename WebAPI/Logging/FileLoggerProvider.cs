using Core.CrossCuttingConcerns.Logging.Log4Net;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace WebAPI.Logging
{
    // Uygulamanın standart log çağrılarını log4net üzerinden dönen log dosyasına yazan sağlayıcı
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentDictionary<string, FileLoggerAdapter> _loggers = new();

        // Verilen kategori (sınıf adı) için dosyaya yazan logger'ı döner; aynı kategori için tek nesne kullanılır.
        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new FileLoggerAdapter(name));
        }

        // Serbest bırakılacak kaynak yoktur.
        public void Dispose()
        {
        }
    }

    // Tek bir kategori için log satırlarını log4net'e ileten bağdaştırıcı
    internal class FileLoggerAdapter : ILogger
    {
        private readonly LoggerServiceBase? _log;

        // Kategori adıyla log4net logger'ını hazırlar; ayar dosyası okunamazsa dosyaya yazmadan devam eder.
        public FileLoggerAdapter(string categoryName)
        {
            try
            {
                _log = new LoggerServiceBase(categoryName);
            }
            catch
            {
                // log4net.config bulunamazsa uygulama çökmemeli; konsol logu çalışmaya devam eder.
                _log = null;
            }
        }

        // Kapsam (scope) desteği kullanılmıyor.
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        // Bilgi seviyesi ve üstünü dosyaya yazar; ayrıntı seviyeleri appsettings içindeki Logging bölümünden süzülür.
        public bool IsEnabled(LogLevel logLevel)
        {
            return _log != null && logLevel >= LogLevel.Information;
        }

        // Log satırını seviyesine göre dosyaya yazar; hata varsa ayrıntısını (yığın izini) ekler.
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            string message = formatter(state, exception);
            if (exception != null)
                message += Environment.NewLine + exception;

            switch (logLevel)
            {
                case LogLevel.Critical:
                    _log!.Fatal(message);
                    break;
                case LogLevel.Error:
                    _log!.Error(message);
                    break;
                case LogLevel.Warning:
                    _log!.Warn(message);
                    break;
                default:
                    _log!.Info(message);
                    break;
            }
        }
    }
}