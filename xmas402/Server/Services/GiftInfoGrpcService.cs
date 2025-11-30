using ProtoBuf.Grpc;
using xmas402.Server.Extensions;
using xmas402.Services;
using xmas402.Shared.Interfaces;
using xmas402.Shared.Models;

namespace xmas402.Server.Services
{
    public class GiftInfoGrpcService(GiftService giftService) : IGiftInfoGrpcService
    {
        public async Task<List<Gift>> GetGifts(CallContext context = default)
        {
            var all = await giftService.GetAll(100);

            return all.Select(g => g.ToGift()).ToList();
        }

        public async Task<Gift?> GetLastGift(CallContext context = default)
        {
            var gift = await giftService.GetLastGift();

            return gift?.ToGift();
        }
    }
}
