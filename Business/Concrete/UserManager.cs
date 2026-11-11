using Business.Abstract;
using Core.Constants;
using Core.Entities.Concrete;
using Core.Utilities.Results;
using Core.Utilities.Security.Hashing;
using DataAccess.Abstract;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Entities.Dtos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Business.Concrete
{
    public class UserManager : IUserService
    {
        private const int AccessCacheSeconds = 30;   // Güncel rol bilgisinin önbellekte tutulma süresi
        private const int MinPasswordLength = 6;

        // Her istekte veritabanına gitmemek için kullanıcı durumu ve rolleri kısa süre önbellekte tutulur.
        private static readonly ConcurrentDictionary<int, (DateTime ReadAt, UserAccessDto Access)> AccessCache = new();

        IUserDal _userDal;

        // Kullanıcı veri erişim bağımlılığını alır.
        public UserManager(IUserDal userDal)
        {
            _userDal = userDal;
        }

        // Kullanıcıyı veritabanına ekler.
        public void Add(User user)
        {
            _userDal.Add(user);
        }

        // E-posta adresine göre tek kullanıcı döner.
        public User GetByMail(string email)
        {
            return _userDal.Get(u => u.Email == email);
        }

        // Kullanıcının rollerini döner.
        public List<OperationClaim> GetClaims(User user)
        {
            return _userDal.GetClaims(user);
        }

        // Bütün kullanıcıları rolleriyle birlikte listeler.
        public IDataResult<List<UserListDto>> GetUsers()
        {
            using var context = new NorthwindContext();

            var users = context.Users.AsNoTracking().OrderBy(u => u.Email).ToList();

            var roleRows = (from userClaim in context.UserOperationClaims
                            join claim in context.OperationClaims on userClaim.OperationClaimId equals claim.Id
                            select new { userClaim.UserId, claim.Name }).ToList();

            var list = users.Select(u => new UserListDto
            {
                Id = u.Id,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                IsActive = u.Status,
                Roles = roleRows.Where(r => r.UserId == u.Id).Select(r => r.Name).OrderBy(n => n).ToList()
            }).ToList();

            return new SuccessDataResult<List<UserListDto>>(list);
        }

        // Atanabilecek rol adlarını listeler.
        public IDataResult<List<string>> GetRoleNames()
        {
            using var context = new NorthwindContext();

            var names = context.OperationClaims.AsNoTracking().Select(c => c.Name).OrderBy(n => n).ToList();
            return new SuccessDataResult<List<string>>(names);
        }

        // Yeni kullanıcı açar ve verilen rolleri atar; oluşan kullanıcının id'sini döner.
        public IDataResult<int> CreateUser(UserCreateDto dto)
        {
            string email = (dto.Email ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(email))
                return new ErrorDataResult<int>("E-posta boş olamaz.");

            if (string.IsNullOrEmpty(dto.Password) || dto.Password.Length < MinPasswordLength)
                return new ErrorDataResult<int>($"Şifre en az {MinPasswordLength} karakter olmalıdır.");

            using var context = new NorthwindContext();

            if (context.Users.Any(u => u.Email == email))
                return new ErrorDataResult<int>("Bu e-posta ile kayıtlı bir kullanıcı zaten var.");

            var claims = ResolveRoles(context, dto.Roles, out string? roleError);
            if (claims == null)
                return new ErrorDataResult<int>(roleError ?? "Rol listesi geçersiz.");

            HashingHelper.CreatePasswordHash(dto.Password, out byte[] passwordHash, out byte[] passwordSalt);

            var user = new User
            {
                Email = email,
                FirstName = (dto.FirstName ?? string.Empty).Trim(),
                LastName = (dto.LastName ?? string.Empty).Trim(),
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                Status = true
            };
            context.Users.Add(user);
            context.SaveChanges();

            foreach (var claim in claims)
            {
                context.UserOperationClaims.Add(new UserOperationClaim { UserId = user.Id, OperationClaimId = claim.Id });
            }
            context.SaveChanges();

            return new SuccessDataResult<int>(user.Id, $"'{email}' kullanıcısı oluşturuldu.");
        }

        // Kullanıcının rollerini verilen listeyle değiştirir (eski roller silinir).
        public IResult SetRoles(int userId, List<string> roles)
        {
            using var context = new NorthwindContext();

            var user = context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
                return new ErrorResult("Kullanıcı bulunamadı.");

            var claims = ResolveRoles(context, roles, out string? roleError);
            if (claims == null)
                return new ErrorResult(roleError ?? "Rol listesi geçersiz.");

            bool keepsAdmin = claims.Any(c => c.Name == PlcPermissions.Admin);
            if (!keepsAdmin && IsLastActiveAdmin(context, userId))
                return new ErrorResult("Bu kullanıcı son yönetici. Önce başka bir kullanıcıya Admin rolü verin.");

            var oldRows = context.UserOperationClaims.Where(c => c.UserId == userId).ToList();
            context.UserOperationClaims.RemoveRange(oldRows);

            foreach (var claim in claims)
            {
                context.UserOperationClaims.Add(new UserOperationClaim { UserId = userId, OperationClaimId = claim.Id });
            }
            context.SaveChanges();

            // Değişiklik beklemeden geçerli olsun diye önbellek silinir.
            AccessCache.TryRemove(userId, out _);

            return new SuccessResult($"'{user.Email}' kullanıcısının rolleri güncellendi.");
        }

        // Kullanıcı hesabını açar ya da kapatır; kapatılan hesap hemen yetkisiz kalır.
        public IResult SetActive(int userId, bool isActive)
        {
            using var context = new NorthwindContext();

            var user = context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
                return new ErrorResult("Kullanıcı bulunamadı.");

            if (!isActive && IsLastActiveAdmin(context, userId))
                return new ErrorResult("Bu kullanıcı son yönetici, kapatılamaz.");

            user.Status = isActive;
            context.SaveChanges();

            AccessCache.TryRemove(userId, out _);

            return new SuccessResult($"'{user.Email}' kullanıcısı {(isActive ? "açıldı" : "kapatıldı")}.");
        }

        // Kullanıcının şifresini yenisiyle değiştirir.
        public IResult ResetPassword(int userId, string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
                return new ErrorResult($"Şifre en az {MinPasswordLength} karakter olmalıdır.");

            using var context = new NorthwindContext();

            var user = context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
                return new ErrorResult("Kullanıcı bulunamadı.");

            HashingHelper.CreatePasswordHash(newPassword, out byte[] passwordHash, out byte[] passwordSalt);
            user.PasswordHash = passwordHash;
            user.PasswordSalt = passwordSalt;
            context.SaveChanges();

            return new SuccessResult($"'{user.Email}' kullanıcısının şifresi değiştirildi.");
        }

        // Kullanıcının güncel durumunu ve rollerini döner; veritabanına ulaşılamazsa eldeki son bilgiyi ya da null döner.
        public UserAccessDto? GetCurrentAccess(int userId)
        {
            bool hasCached = AccessCache.TryGetValue(userId, out var cached);
            if (hasCached && (DateTime.UtcNow - cached.ReadAt).TotalSeconds < AccessCacheSeconds)
                return cached.Access;

            try
            {
                using var context = new NorthwindContext();

                var user = context.Users.AsNoTracking().FirstOrDefault(u => u.Id == userId);

                var access = new UserAccessDto { Exists = user != null, IsActive = user != null && user.Status };

                if (user != null)
                {
                    access.Roles = (from userClaim in context.UserOperationClaims
                                    join claim in context.OperationClaims on userClaim.OperationClaimId equals claim.Id
                                    where userClaim.UserId == userId
                                    select claim.Name).ToList();
                }

                AccessCache[userId] = (DateTime.UtcNow, access);
                return access;
            }
            catch
            {
                // Veritabanı yoksa operatör kilitlenmesin diye son bilinen durum kullanılır.
                return hasCached ? cached.Access : null;
            }
        }

        // Rol adlarını OperationClaims kayıtlarına çevirir; bilinmeyen rol varsa null döner ve hatayı bildirir.
        private static List<OperationClaim>? ResolveRoles(NorthwindContext context, List<string>? roleNames, out string? error)
        {
            error = null;

            var wanted = (roleNames ?? new List<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct()
                .ToList();

            var allClaims = context.OperationClaims.AsNoTracking().ToList();

            var unknown = wanted.Where(n => !allClaims.Any(c => c.Name == n)).ToList();
            if (unknown.Count > 0)
            {
                error = $"Bilinmeyen rol: {string.Join(", ", unknown)}";
                return null;
            }

            return allClaims.Where(c => wanted.Contains(c.Name)).ToList();
        }

        // Verilen kullanıcının sistemdeki tek aktif yönetici olup olmadığını döner.
        private static bool IsLastActiveAdmin(NorthwindContext context, int userId)
        {
            int adminRoleId = context.OperationClaims
                .Where(c => c.Name == PlcPermissions.Admin)
                .Select(c => c.Id)
                .FirstOrDefault();

            var activeAdminIds = (from userClaim in context.UserOperationClaims
                                  join user in context.Users on userClaim.UserId equals user.Id
                                  where userClaim.OperationClaimId == adminRoleId && user.Status
                                  select user.Id).Distinct().ToList();

            return activeAdminIds.Count == 1 && activeAdminIds.Contains(userId);
        }
    }
}