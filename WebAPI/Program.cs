using Autofac;
using Autofac.Extensions.DependencyInjection;
using Business.DependencyResolvers.Autofac;
using DataAccess.Concrete.EntityFramework;
using DataAccess.Concrete.EntityFramework.Contexts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using WebAPI.Logging;
using WebAPI.Services;

namespace WebAPI
{
    public class Program
    {
        // Uygulamayı kurar, veritabanını hazırlar ve çalıştırır.
        public static void Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            PrepareDatabase(host);
            host.Run();
        }

        // Bağlantı metnini ayarlardan alır; veritabanını, tabloları, rolleri ve ilk yöneticiyi hazırlar.
        private static void PrepareDatabase(IHost host)
        {
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            NorthwindContext.ConnectionString = configuration.GetConnectionString("Default");

            try
            {
                DbInitializer.Initialize(configuration["Seed:AdminEmail"], configuration["Seed:AdminPassword"]);
                logger.LogInformation("Veritabanı hazır.");
            }
            catch (Exception ex)
            {
                // Veritabanı yoksa da canlı veri ve PLC komutları çalışmaya devam etsin diye uygulama durdurulmaz.
                logger.LogError(ex, "Veritabanı hazırlanamadı. MySQL çalışıyor mu, ConnectionStrings:Default doğru mu?");
            }
        }

        // Host'u Autofac ile kurar; gizli ayar dosyasını, dosya logunu ve günlük bakım servisini ekler.
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .UseServiceProviderFactory(new AutofacServiceProviderFactory())
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
            })
            .ConfigureLogging(logging =>
            {
                logging.AddProvider(new FileLoggerProvider());
            })
            .ConfigureServices(services =>
            {
                services.AddHostedService<MaintenanceBackgroundService>();
            })
            .ConfigureContainer<ContainerBuilder>(builder =>
            {
                builder.RegisterModule(new AutofacBusinessModule());

            })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}