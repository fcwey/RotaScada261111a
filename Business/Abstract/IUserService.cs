using Core.Entities.Concrete;
using Core.Utilities.Results;
using Entities.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Business.Abstract
{
    public interface IUserService
    {
        List<OperationClaim> GetClaims(User user);
        void Add(User user);
        User GetByMail(string email);

        // Kullanıcı yönetimi
        IDataResult<List<UserListDto>> GetUsers();
        IDataResult<List<string>> GetRoleNames();
        IDataResult<int> CreateUser(UserCreateDto dto);
        IResult SetRoles(int userId, List<string> roles);
        IResult SetActive(int userId, bool isActive);
        IResult ResetPassword(int userId, string newPassword);

        // Kullanıcının güncel durumunu ve rollerini döner; veritabanına ulaşılamazsa null döner
        UserAccessDto? GetCurrentAccess(int userId);
    }
}