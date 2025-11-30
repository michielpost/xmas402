namespace xmas402.Server.Extensions
{
    public static class GiftExtensions
    {
        public static Shared.Models.Gift ToGift(this Database.Models.Gift gift)
        {
            return new Shared.Models.Gift
            {
                CreatedDateTime = gift.CreatedDateTime.DateTime,
                From = gift.From,
                To = gift.To,
                GiftType = gift.GiftType,
                Asset = gift.Asset,
                Network = gift.Network,
                Transaction = gift.Transaction,
                Value = gift.Value,
                NextValue = gift.NextValue,
            };
        }
    }
}
