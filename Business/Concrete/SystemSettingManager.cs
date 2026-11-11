using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Abstract;
using Entities.Dtos;
using Entities.Structures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Business.Concrete
{
    public class SystemSettingManager : ISystemSettingService
    {
        private readonly IS7DriverDal _s7DriverDal;
        private readonly IPlcService _plcService;

        // Aynı anda tek parametre yazılmasını sağlar.
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        // Sürücü ve PLC servis bağımlılıklarını alır.
        public SystemSettingManager(IS7DriverDal s7DriverDal, IPlcService plcService)
        {
            _s7DriverDal = s7DriverDal;
            _plcService = plcService;
        }

        // DB204'ü tek blok okuyup katalogdaki her parametreyi güncel değeriyle döner.
        public async Task<IDataResult<List<ParamValueDto>>> GetParametersAsync()
        {
            try
            {
                var buffer = await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbParam, 0, ParamCatalog.BlockSize);

                short version = S7Bytes.GetInt(buffer, ScadaLayout.ParamVersionOffset);
                if (version != ScadaLayout.LayoutVersion)
                {
                    return new ErrorDataResult<List<ParamValueDto>>(
                        $"Şema sürümü uyuşmuyor (DB204: {version}, beklenen: {ScadaLayout.LayoutVersion}).");
                }

                var list = ParamCatalog.All.Select(p => new ParamValueDto
                {
                    Address = p.Address,
                    Name = p.Name,
                    Unit = p.Unit,
                    Description = p.Description,
                    Default = p.Default,
                    Min = p.Min,
                    Max = p.Max,
                    IsSafety = p.IsSafety,
                    Value = S7Bytes.GetInt(buffer, p.Address)
                }).ToList();

                return new SuccessDataResult<List<ParamValueDto>>(list);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<List<ParamValueDto>>($"Parametreler okunamadı: {ex.Message}");
            }
        }

        // Parametreyi katalog ve aralık kontrolünden geçirip DB204'e yazar, geri okuyup doğrular.
        public async Task<IDataResult<int>> UpdateParameterAsync(int address, int value)
        {
            var definition = ParamCatalog.Find(address);
            if (definition == null)
                return new ErrorDataResult<int>($"Adres {address} yazılabilir bir parametre değil.");

            if (value < definition.Min || value > definition.Max)
            {
                return new ErrorDataResult<int>(
                    $"'{definition.Name}' değeri {definition.Min}–{definition.Max} {definition.Unit} aralığında olmalı (girilen: {value}).");
            }

            await _writeLock.WaitAsync();
            try
            {
                var layout = await _plcService.VerifyLayoutAsync();
                if (!layout.IsSuccess)
                    return new ErrorDataResult<int>(layout.Message);

                int oldValue = S7Bytes.GetInt(await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbParam, address, 2), 0);

                var data = new byte[2];
                S7Bytes.SetInt(data, 0, (short)value);
                await _s7DriverDal.WriteBytesAsync(ScadaLayout.DbParam, address, data);

                // Yazılan değer geri okunup doğrulanır.
                int readBack = S7Bytes.GetInt(await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbParam, address, 2), 0);
                if (readBack != value)
                {
                    return new ErrorDataResult<int>(oldValue,
                        $"'{definition.Name}' yazıldı ama geri okunan değer farklı (yazılan: {value}, okunan: {readBack}).");
                }

                return new SuccessDataResult<int>(oldValue,
                    $"'{definition.Name}' {oldValue} → {value} {definition.Unit} olarak değiştirildi.");
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<int>($"'{definition.Name}' yazılamadı: {ex.Message}");
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}