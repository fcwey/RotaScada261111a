using Core.Utilities.Results;
using Entities.Enums;
using Entities.Structures;
using System.Threading.Tasks;

namespace Business.Abstract
{
    public interface IPlcService
    {
        Task<IResult> ConnectPlcAsync(string ipAddress);
        IResult DisconnectPlc();

        // DB200–204 arayüzü (şema sürümü 3)
        Task<IResult> VerifyLayoutAsync();
        Task<IDataResult<ScadaStatus>> ReadStatusAsync();
        Task<IResult> WriteHeartbeatAsync();
        Task<IDataResult<ScadaCommandResult>> SendCommandAsync(ScadaCommandCode code, short param1 = 0, short param2 = 0, short param3 = 0);
    }
}