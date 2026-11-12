using Castle.DynamicProxy;
using Core.CrossCuttingConcerns.Logging;
using Core.CrossCuttingConcerns.Logging.Log4Net;
using Core.Utilities.Interceptors;
using Core.Utilities.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Aspects.Autofac.Exception
{
    public class ExceptionLogAspect : MethotInterception
    {
        private LoggerServiceBase _loggerServiceBase;

        // Kullanılacak logger tipini alır ve örneğini oluşturur.
        public ExceptionLogAspect(Type loggerService)
        {
            if (loggerService.BaseType != typeof(LoggerServiceBase))
            {

                throw new System.Exception(AspectMessages.WrongLoggerType);
            }
            _loggerServiceBase = (LoggerServiceBase)Activator.CreateInstance(loggerService);
        }

        // Servis metodunda hata olduğunda metot bilgisini ve hatayı log dosyasına yazar.
        protected override void OnExeption(IInvocation invocation, System.Exception e)
        {
            _loggerServiceBase.Error(BuildMessage(invocation, e));
        }

        // Log satırını üretir; şifre gibi değerler dosyaya düşmesin diye parametrelerin yalnızca adı ve tipi yazılır.
        private static string BuildMessage(IInvocation invocation, System.Exception e)
        {
            var parameters = invocation.GetConcreteMethod().GetParameters();
            var parts = new List<string>();

            for (int i = 0; i < invocation.Arguments.Length; i++)
            {
                string name = i < parameters.Length ? parameters[i].Name ?? "?" : "?";
                string type = invocation.Arguments[i] != null ? invocation.Arguments[i].GetType().Name : "null";
                parts.Add($"{name}: {type}");
            }

            string className = invocation.TargetType != null ? invocation.TargetType.Name : "?";

            return $"Servis hatası: {className}.{invocation.Method.Name}({string.Join(", ", parts)})" +
                   Environment.NewLine + e;
        }
    }
}