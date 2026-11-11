using Core.Utilities.Results;
using System.Threading.Tasks;

namespace Business.Abstract
{
    public interface IBatchService
    {
        // PLC sekans durumunu izler; program bittiğinde ya da durduğunda açık şarj kaydını kapatır
        Task<IResult> TrackSequenceAsync(short seqState);
    }
}