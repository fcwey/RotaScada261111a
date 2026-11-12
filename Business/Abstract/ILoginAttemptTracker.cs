namespace Business.Abstract
{
    public interface ILoginAttemptTracker
    {
        // Anahtar bekletiliyorsa kalan saniyeyi, bekletilmiyorsa 0 döner
        int GetBlockedSeconds(string key);

        // Hatalı giriş denemesini sayar; sınır aşılırsa bekletmeyi başlatır
        void RegisterFailure(string key);

        // Başarılı girişten sonra sayacı sıfırlar
        void Reset(string key);
    }
}