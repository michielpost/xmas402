using System.ComponentModel.DataAnnotations;

namespace xmas402.Database.Models
{
    public class Gift
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(128)]
        public required string From { get; set; }

        [MaxLength(128)]
        public required string To { get; set; }

        [MaxLength(128)]
        public string? Transaction { get; set; }
        [MaxLength(128)]
        public string? Network { get; set; }
        [MaxLength(128)]
        public string? Asset { get; set; }
        [MaxLength(64)]
        public string? Value { get; set; }

        [MaxLength(64)]
        public required string NextValue { get; set; }

        [MaxLength(32)]
        public string? GiftType { get; set; }

        public required DateTimeOffset CreatedDateTime { get; set; }

        [MaxLength(16)]
        public string? Ip { get; set; }
    }
}
