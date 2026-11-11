using Core.Utilities.Results;
using Entities.Dtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace Business.Abstract
{
    public interface IInterlockService
    {
        // Bir işlem için PLC'nin izin bitini okuyup iznin olup olmadığını döner
        Task<IResult> ValidateOperationSafetyAsync(string operationCode);

        // Mimik ekrandaki bileşenlerin anlık durumunu paket halinde getirir
        Task<IDataResult<MimicStateDto>> GetMimicStateAsync();
    }
}