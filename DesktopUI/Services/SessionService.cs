using Core.Constants;
using Entities.Dtos;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DesktopUI.Services
{
    // Giriş yapmış kullanıcıyı ve token'ı tutar; token'ı bilgisayarda şifreli saklar.
    public class SessionService
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RotaScada", "session.bin");

        public string? Token { get; private set; }
        public CurrentUserDto? User { get; private set; }
        public bool IsLoggedIn => Token != null && User != null;

        // Oturumu başlatır; istenirse token'ı sonraki açılış için diske yazar.
        public void Begin(string token, CurrentUserDto user, bool remember)
        {
            Token = token;
            User = user;

            if (remember)
                SaveToken(token);
            else
                DeleteSavedToken();
        }

        // Oturumu kapatır ve saklanan token'ı siler.
        public void End()
        {
            Token = null;
            User = null;
            DeleteSavedToken();
        }

        // Kullanıcının verilen rollerden en az birine sahip olup olmadığını döner; Admin her şeye yetkilidir.
        public bool HasRole(params string[] roles)
        {
            if (User == null)
                return false;

            return User.Roles.Contains(PlcPermissions.Admin) || roles.Any(role => User.Roles.Contains(role));
        }

        // Diskte saklanan token'ı okur; yoksa ya da çözülemiyorsa null döner.
        public string? LoadSavedToken()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return null;

                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return null;
            }
        }

        // Token'ı Windows kullanıcısına özel şifreleyip diske yazar.
        private static void SaveToken(string token)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(FilePath, cipher);
            }
            catch
            {
                // Token saklanamazsa oturum yine çalışır; yalnızca sonraki açılışta giriş istenir.
            }
        }

        // Diskte saklanan token'ı siler.
        private static void DeleteSavedToken()
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch
            {
                // Dosya silinemese de oturum kapanmış sayılır.
            }
        }
    }
}