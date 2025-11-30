using ProtoBuf;

namespace xmas402.Shared.Models
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class Gift
    {
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Transaction { get; set; }
        public string? Network { get; set; }
        public string? Asset { get; set; }
        public string? Value { get; set; }
        public string NextValue { get; set; } = "1000000";

        public string? GiftType { get; set; }
        public required DateTime CreatedDateTime { get; set; }
    }
}
