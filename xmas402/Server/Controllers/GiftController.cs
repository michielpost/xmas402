using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using x402;
using x402.Core.Enums;
using x402.Core.Models;
using x402.Core.Models.v1;
using xmas402.Database;
using xmas402.Database.Models;
using xmas402.Services;
using xmas402.Shared;

namespace xmas402.Server.Controllers;

/// <summary>
/// Give and receive gifts
/// </summary>
[ApiController]
[Route("[controller]")]
public class GiftController(ApplicationDbContext dbContext,
    GiftService giftService,
    X402HandlerV1 x402Handler) : ControllerBase
{
    private static readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Give an xmas gift on xmas402.com.
    /// The payment requirements are dynamic and can change over time.
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [Route("send-gift")]
    public async Task<GiftResponse?> SendGift([FromQuery] string? gift)
    {
        if (gift == null)
            gift = "AI Giftcard";

        gift = LimitTo16(gift);

        await _lock.WaitAsync();
        try
        {
            var lastGift = await giftService.GetLastGift();

            var ip = this.HttpContext.Connection.RemoteIpAddress?.ToString();
            var shortIp = ip is { Length: > 16 } v ? v[..16] : ip;

            var payReq = new PaymentRequirementsBasic
            {
                //Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e", //Testnet
                Asset = "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913", //Mainnet
                Amount = lastGift.NextValue,
                PayTo = lastGift.From,
            };

            var x402Result = await x402Handler.HandleX402Async(
                new PaymentRequiredInfo
                {
                    Accepts = new List<PaymentRequirementsBasic> { payReq },
                    Resource = new ResourceInfoBasic
                    {
                        Description = "Give a gift and join the queue to receive a gift",
                    },
                    Discoverable = true
                },
                SettlementMode.Pessimistic,
                onSetOutputSchema: (context, reqs, schema) =>
                {
                    schema.Input ??= new();

                    schema.Input.Method = "GET";

                    //Manually set the input schema
                    schema.Input.QueryParams = new Dictionary<string, object>
                    {
                    {
                        nameof(gift),
                        new FieldDefenition
                        {
                            Required = false,
                            Description = "Gift type (max length: 16)",
                            Type = "string"
                        }
                    }
                    };

                    return schema;
                });

            if (!x402Result.CanContinueRequest)
            {
                return null;
            }

            try
            {
                //Save message to db
                var dbGift = new Gift
                {
                    GiftType = gift,
                    CreatedDateTime = DateTimeOffset.UtcNow,
                    From = x402Result.VerificationResponse?.Payer ?? "Unknown",
                    To = x402Result.VerificationResponse?.Payer ?? "Unknown",
                    Asset = payReq.Asset,
                    Network = x402Result.SettlementResponse?.Network,
                    Transaction = x402Result.SettlementResponse?.Transaction,
                    Value = payReq.Amount,
                    NextValue = (GetRandomAmount() * 1000000).ToString(),
                    Ip = shortIp
                };

                dbContext.Gifts.Add(dbGift);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                return new GiftResponse { Success = false, Error = ex.Message };
            }

            return new GiftResponse { Success = true };
        }
        finally
        {
            _lock.Release();
        }
    }

    public static double GetRandomAmount()
    {
        var random = new Random();

        // Generate a value between 1.00 and 3.00 with 2 decimals
        double value = random.NextDouble() * (3.0 - 1.0) + 1.0;

        // Round to 2 decimals
        return Math.Round(value, 2);
    }

    public static string LimitTo16(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return input.Length <= 16
            ? input
            : input.Substring(0, 16);
    }
}
