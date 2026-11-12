using Autofac;
using Autofac.Extras.DynamicProxy;
using Business.Abstract;
using Business.Concrete;
using Castle.DynamicProxy;
using Core.Utilities.Interceptors;
using Core.Utilities.IoC;
using Core.Utilities.Security.Jwt;
using DataAccess.Abstract;
using DataAccess.Concrete.EntityFramework;
using DataAccess.Concrete.S7Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Business.DependencyResolvers.Autofac
{
    public class AutofacBusinessModule : Module
    {
        // Arayüzlerin hangi sınıflarla karşılanacağını kaydeder.
        protected override void Load(ContainerBuilder builder)
        {
            // DataAccess & Driver Kayıtları
            builder.RegisterType<S7DriverDal>().As<IS7DriverDal>().SingleInstance();
            builder.RegisterType<EfAlarmDal>().As<IAlarmDal>().SingleInstance();
            builder.RegisterType<EfRecipeDal>().As<IRecipeDal>().SingleInstance();
            builder.RegisterType<EfAuditLogDal>().As<IAuditLogDal>().SingleInstance();

            // Business Servis Kayıtları
            builder.RegisterType<PlcManager>().As<IPlcService>().SingleInstance();
            builder.RegisterType<AlarmManager>().As<IAlarmService>().SingleInstance();
            builder.RegisterType<HistorianManager>().As<IHistorianService>().SingleInstance();
            builder.RegisterType<RecipeManager>().As<IRecipeService>().SingleInstance();
            builder.RegisterType<BatchManager>().As<IBatchService>().SingleInstance();
            builder.RegisterType<AuditTrailManager>().As<IAuditTrailService>().SingleInstance();
            builder.RegisterType<ReportManager>().As<IReportService>().SingleInstance();
            builder.RegisterType<InterlockManager>().As<IInterlockService>().SingleInstance();
            builder.RegisterType<SystemSettingManager>().As<ISystemSettingService>().SingleInstance();

            // Kullanıcı ve Kimlik Doğrulama Kayıtları
            builder.RegisterType<UserManager>().As<IUserService>();
            builder.RegisterType<EfUserDal>().As<IUserDal>();
            builder.RegisterType<AuthManager>().As<IAuthService>();
            builder.RegisterType<JwtHelper>().As<ITokenHelper>();
            builder.RegisterType<UserContextHelper>().As<IUserContextHelper>().SingleInstance();

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            builder.RegisterAssemblyTypes(assembly).AsImplementedInterfaces()
                .EnableInterfaceInterceptors(new ProxyGenerationOptions()
                {
                    Selector = new AspectInterceptorSelector()
                }).SingleInstance();
        }
    }
}