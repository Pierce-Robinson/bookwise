using System.Net;
using System.Net.Http.Json;
using Bookwise.Tests.Support;

namespace Bookwise.Tests;

public class LowRateLimitFactory : BookwiseFactory
{
    protected override int RateLimitPerMinute => 3;
}

public class RateLimitTests(LowRateLimitFactory factory) : IClassFixture<LowRateLimitFactory>
{
    [Fact]
    public async Task Public_booking_endpoint_is_rate_limited()
    {
        var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var response = await client.PostAsJsonAsync("/api/public/bookings", new { });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, statuses[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[3]);
    }
}
