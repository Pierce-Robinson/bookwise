using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Bookwise.Api.Infrastructure;

public static class RateLimits
{
    public const string PublicWrites = "public-writes";

    /// <summary>
    /// Limits booking and cancelling per client IP, so the public endpoints can't be used to fill
    /// up a calendar with junk bookings.
    /// </summary>
    public static IServiceCollection AddBookwiseRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(PublicWrites, context =>
            {
                var limit = context.RequestServices.GetRequiredService<IOptions<BusinessOptions>>().Value
                    .PublicRateLimitPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
            });
        });
}
