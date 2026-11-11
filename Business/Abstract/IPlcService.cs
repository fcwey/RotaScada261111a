using Core.Utilities.Results;
using Entities.Enums;
using Entities.Structures;
using S7.Net;
using System;
using System.Collections.Generic;
using System.Text;

namespace Business.Abstract
{
    public interface IPlcService
    {
        Task<IResult> ConnectPlcAsync(string ipAddress);
        IResult DisconnectPlc();

        // Yeni arayüz (DB200–204, şema sürümü 3)
        Task<IResult> VerifyLayoutAsync();
        Task<IDataResult<ScadaStatus>> ReadStatusAsync();
        Task<IResult> WriteHeartbeatAsync();
        Task<IDataResult<ScadaCommandResult>> SendCommandAsync(ScadaCommandCode code, short param1 = 0, short param2 = 0, short param3 = 0);

        // Eski arayüz (yeni ekranlar bağlanınca kaldırılacak)
        Task<IDataResult<float>> ReadTemperatureAsync(int dbNumber, int startByte);
        Task<IResult> WriteSetpointAsync(int dbNumber, int startByte, float value);
        Task<IResult> PulseBitAsync(DataType dataType, int dbNumber, int startByte, int bitNumber, int durationMs = 500);
    }
}