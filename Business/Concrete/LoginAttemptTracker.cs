using Business.Abstract;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Business.Concrete
{
    // Art arda hatalı giriş denemelerini sayar ve sınır aşılınca bir süre bekletir (bellekte tutulur)
    public class LoginAttemptTracker : ILoginAttemptTracker
    {
        private const int DefaultMaxFailures = 5;
        private const int DefaultBlockSeconds = 60;
        private const int DefaultForgetMinutes = 15;
        private const int CleanupThreshold = 1000;

        private readonly object _lock = new object();
        private readonly Dictionary<string, Attempt> _attempts = new();

        public int MaxFailures { get; } = DefaultMaxFailures;       // Bu kadar hatalı denemeden sonra bekletme başlar
        public int BlockSeconds { get; } = DefaultBlockSeconds;     // Bekletme süresi
        public int ForgetMinutes { get; } = DefaultForgetMinutes;   // Bu kadar süre deneme olmazsa sayaç unutulur

        // Şimdiki zamanı veren işlev; testlerde zamanı ilerletebilmek için değiştirilebilir.
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        // Varsayılan değerlerle (5 deneme, 60 saniye, 15 dakika) sayaç oluşturur; testler bunu kullanır.
        public LoginAttemptTracker()
        {
        }

        // Değerleri appsettings içindeki "Login" bölümünden okur; eksik ya da geçersiz olanlarda varsayılanı kullanır.
        public LoginAttemptTracker(IConfiguration configuration)
        {
            MaxFailures = ReadPositive(configuration, "Login:MaxFailures", DefaultMaxFailures);
            BlockSeconds = ReadPositive(configuration, "Login:BlockSeconds", DefaultBlockSeconds);
            ForgetMinutes = ReadPositive(configuration, "Login:ForgetMinutes", DefaultForgetMinutes);
        }

        // Anahtar bekletiliyorsa kalan saniyeyi, bekletilmiyorsa 0 döner.
        public int GetBlockedSeconds(string key)
        {
            lock (_lock)
            {
                if (!_attempts.TryGetValue(key, out var attempt))
                    return 0;

                double remaining = (attempt.BlockedUntil - Clock()).TotalSeconds;
                return remaining > 0 ? (int)Math.Ceiling(remaining) : 0;
            }
        }

        // Hatalı denemeyi sayar; sınıra ulaşıldıysa bekletme süresini başlatır.
        public void RegisterFailure(string key)
        {
            lock (_lock)
            {
                DateTime now = Clock();

                if (_attempts.Count > CleanupThreshold)
                    RemoveExpired(now);

                // Uzun süredir deneme yoksa sayaç baştan başlar.
                if (!_attempts.TryGetValue(key, out var attempt) ||
                    (now - attempt.LastFailure).TotalMinutes > ForgetMinutes)
                {
                    attempt = new Attempt();
                }

                attempt.Failures++;
                attempt.LastFailure = now;

                if (attempt.Failures >= MaxFailures)
                    attempt.BlockedUntil = now.AddSeconds(BlockSeconds);

                _attempts[key] = attempt;
            }
        }

        // Başarılı girişten sonra anahtarın sayacını siler.
        public void Reset(string key)
        {
            lock (_lock)
            {
                _attempts.Remove(key);
            }
        }

        // Ayardaki değeri okur; yoksa, sayı değilse ya da 1'den küçükse varsayılanı döner.
        private static int ReadPositive(IConfiguration configuration, string key, int defaultValue)
        {
            return int.TryParse(configuration[key], out int value) && value > 0 ? value : defaultValue;
        }

        // Süresi geçmiş kayıtları bellekten temizler.
        private void RemoveExpired(DateTime now)
        {
            var expired = _attempts
                .Where(pair => (now - pair.Value.LastFailure).TotalMinutes > ForgetMinutes)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var key in expired)
                _attempts.Remove(key);
        }

        // Tek bir anahtarın deneme bilgisi
        private class Attempt
        {
            public int Failures { get; set; }
            public DateTime LastFailure { get; set; }
            public DateTime BlockedUntil { get; set; }
        }
    }
}