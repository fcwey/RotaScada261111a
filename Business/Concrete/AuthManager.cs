using Business.Abstract;
using Business.Constants;
using Core.Entities.Concrete;
using Core.Utilities.Results;
using Core.Utilities.Security.Hashing;
using Core.Utilities.Security.Jwt;
using Entities.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Business.Concrete
{
    public class AuthManager : IAuthService
    {
        // Hangi hesapların var olduğu anlaşılmasın diye iki hata için aynı mesaj kullanılır.
        public const string InvalidLoginMessage = "E-posta ya da şifre hatalı.";

        IUserService _userService;
        ITokenHelper _tokenHelper;

        // Kullanıcı ve token servis bağımlılıklarını alır.
        public AuthManager(IUserService userService, ITokenHelper tokenHelper)
        {
            _userService = userService;
            _tokenHelper = tokenHelper;
        }

        // Kullanıcının rolleriyle birlikte giriş token'ı üretir.
        public IDataResult<AccessToken> CreateAccessToken(User user)
        {
            var claims = _userService.GetClaims(user);
            var accessToken = _tokenHelper.CreateToken(user, claims);
            return new SuccessDataResult<AccessToken>(accessToken, Messages.AccessTokenCreated);
        }

        // E-posta ve şifreyi doğrular; hesap kapalıysa girişe izin vermez, eski biçimli şifreyi yeniler.
        public IDataResult<User> Login(UserForLoginDto userForLoginDto)
        {
            var userToCheck = _userService.GetByMail(userForLoginDto.Email);
            if (userToCheck == null)
            {
                return new ErrorDataResult<User>(InvalidLoginMessage);
            }
            if (!HashingHelper.VerifyPasswordHash(userForLoginDto.Password, userToCheck.PasswordHash, userToCheck.PasswordSalt))
            {
                return new ErrorDataResult<User>(InvalidLoginMessage);
            }
            if (!userToCheck.Status)
            {
                return new ErrorDataResult<User>("Bu kullanıcı hesabı kapatılmış.");
            }

            UpgradePasswordHashIfNeeded(userToCheck, userForLoginDto.Password);

            return new SuccessDataResult<User>(userToCheck, Messages.SuccessfulLogin);
        }

        // Şifre eski biçimde saklanıyorsa doğrulanmış şifreyle yeni biçime çevirir; hata girişi engellemez.
        private void UpgradePasswordHashIfNeeded(User user, string password)
        {
            if (!HashingHelper.NeedsUpgrade(user.PasswordSalt))
                return;

            try
            {
                _userService.ResetPassword(user.Id, password);
            }
            catch
            {
                // Yenileme başarısız olursa kullanıcı eski biçimle giriş yapmaya devam eder.
            }
        }

        // Şifreyi hash'leyip yeni kullanıcı kaydı oluşturur.
        public IDataResult<User> Register(UserForRegisterDto userForRegisterDto, string password)
        {
            byte[] passwordHash, passwordSalt;
            HashingHelper.CreatePasswordHash(password, out passwordHash, out passwordSalt);
            var user = new User
            {
                Email = userForRegisterDto.Email,
                FirstName = userForRegisterDto.FirstName,
                LastName = userForRegisterDto.LastName,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                Status = true
            };
            _userService.Add(user);
            return new SuccessDataResult<User>(user, Messages.UserRegistered);
        }

        // Verilen e-posta ile kayıtlı kullanıcı varsa hata döner.
        public IResult UserExist(string email)
        {
            if (_userService.GetByMail(email) != null)
            {
                return new ErrorResult(Messages.UserAlreadyExists);
            }
            return new SuccessResult();
        }
    }
}