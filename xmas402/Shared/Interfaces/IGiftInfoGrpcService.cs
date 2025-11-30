using ProtoBuf.Grpc;
using System.ServiceModel;
using xmas402.Shared.Models;

namespace xmas402.Shared.Interfaces
{
    [ServiceContract]

    public interface IGiftInfoGrpcService
    {
        [OperationContract]
        Task<List<Gift>> GetGifts(CallContext context = default);

        [OperationContract]
        Task<Gift?> GetLastGift(CallContext context = default);
    }
}
