using log4net;
using log4net.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Core.CrossCuttingConcerns.Logging.Log4Net
{
    public class LoggerServiceBase
    {
        // log4net ayarı uygulama boyunca yalnızca bir kez yüklenir.
        private static readonly object ConfigLock = new object();
        private static ILoggerRepository _repository;

        private ILog _log;

        // Verilen addaki logger'ı (örneğin "JsonFileLogger") ayar dosyasından alır.
        public LoggerServiceBase(string name)
        {
            ILoggerRepository loggerRepository = GetRepository();
            _log = LogManager.GetLogger(loggerRepository.Name, name);
        }

        // log4net.config dosyasını ilk çağrıda okuyup yapılandırır, sonraki çağrılarda hazır olanı döner.
        private static ILoggerRepository GetRepository()
        {
            lock (ConfigLock)
            {
                if (_repository == null)
                {
                    XmlDocument xmlDocument = new XmlDocument();
                    using (var stream = File.OpenRead("log4net.config"))
                    {
                        xmlDocument.Load(stream);
                    }

                    ILoggerRepository loggerRepository = LogManager.CreateRepository(Assembly.GetEntryAssembly(),
                        typeof(log4net.Repository.Hierarchy.Hierarchy));
                    log4net.Config.XmlConfigurator.Configure(loggerRepository, xmlDocument["log4net"]);

                    _repository = loggerRepository;
                }

                return _repository;
            }
        }

        public bool IsInfoEnabled => _log.IsInfoEnabled;
        public bool IsDebugEnabled => _log.IsDebugEnabled;
        public bool IsWarnEnabled => _log.IsWarnEnabled;
        public bool IsFatalEnabled => _log.IsFatalEnabled;
        public bool IsErrorEnabled => _log.IsErrorEnabled;

        // Bilgi seviyesinde log yazar.
        public void Info(object logMessage)
        {
            if (IsInfoEnabled)
            {
                _log.Info(logMessage);
            }
        }

        // Hata ayıklama seviyesinde log yazar.
        public void Debug(object logMessage)
        {
            if (IsDebugEnabled)
            {
                _log.Debug(logMessage);
            }
        }

        // Uyarı seviyesinde log yazar.
        public void Warn(object logMessage)
        {
            if (IsWarnEnabled)
            {
                _log.Warn(logMessage);
            }
        }

        // Kritik hata seviyesinde log yazar.
        public void Fatal(object logMessage)
        {
            if (IsFatalEnabled)
            {
                _log.Fatal(logMessage);
            }
        }

        // Hata seviyesinde log yazar.
        public void Error(object logMessage)
        {
            if (IsErrorEnabled)
            {
                _log.Error(logMessage);
            }
        }
    }
}