using Microsoft.EntityFrameworkCore;
using xmas402.Database;
using xmas402.Database.Models;

namespace xmas402.Services
{
    public class GiftService(ApplicationDbContext dbContext)
    {
        public async Task<Gift> GetLastGift()
        {
            var lastGift = await dbContext.Gifts
                .OrderByDescending(g => g.CreatedDateTime)
                .FirstOrDefaultAsync();

            if (lastGift == null)
            {
                //Setup for the first gift
                lastGift = new Gift
                {
                    CreatedDateTime = DateTimeOffset.UtcNow.AddMinutes(-10),
                    From = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
                    To = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
                    GiftType = "none",
                    NextValue = "1000000",
                };
            }

            return lastGift;
        }

        public Task<List<Gift>> GetAll(int max)
        {
            return dbContext.Gifts
                .OrderByDescending(g => g.CreatedDateTime)
                .Take(max)
                .ToListAsync();
        }

    }
}
