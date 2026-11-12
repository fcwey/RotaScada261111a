using Business.Abstract;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Business.Concrete
{
    // Art arda hatalı giriş denemelerini sayar ve sınır aşılınca bir süre bekletir (bellekte tutulur)
    public class LoginAttemptTracker : ILoginAttemptTracker
    {
        public const int MaxFailures = 5;       // Bu kadar hatalı denemeden sonra bekletme başlar
        public const int BlockSeconds = 60;     // Bekletme süresi
        public const int ForgetMinutes = 15;    // Bu kadar süre deneme olmazsa sayaç unutulur

        private const int CleanupThreshold = 1000;

        private readonly object _lock = new object();
        private readonly Dictionary<string, Attempt> _attempts = new();

        // Şimdiki zamanı veren işlev; testlerde zamanı ilerletebilmek için değiştirilebilir.
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

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