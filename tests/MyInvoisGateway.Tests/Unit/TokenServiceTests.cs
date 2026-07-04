using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MyInvoisGateway.Api.Lhdn;

namespace MyInvoisGateway.Tests.Unit;

public sealed class CountingTokenHandler : HttpMessageHandler
{
    public int Calls;
    public int ExpiresIn = 3600;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        var body = JsonSerializer.Serialize(new
        {
            access_token = $"token-{Calls}",
            token_type = "Bearer",
            expires_in = ExpiresIn,
        });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

public class TokenServiceTests
{
    private static (TokenService svc, CountingTokenHandler handler, FakeTimeProvider time) Make()
    {
        var handler = new CountingTokenHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://mock-lhdn") };
        var opts = Options.Create(new LhdnOptions
        {
            BaseUrl = "http://mock-lhdn",
            ClientId = "id",
            ClientSecret = "secret",
        });
        var time = new FakeTimeProvider();
        return (new TokenService(http, opts, time), handler, time);
    }

    [Fact]
    public async Task Caches_token_until_near_expiry()
    {
        var (svc, handler, _) = Make();
        var t1 = await svc.GetAccessTokenAsync(CancellationToken.None);
        var t2 = await svc.GetAccessTokenAsync(CancellationToken.None);
        Assert.Equal(t1, t2);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Refreshes_within_5_minutes_of_expiry()
    {
        var (svc, handler, time) = Make();
        await svc.GetAccessTokenAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(3600 - 200)); // inside 5-min refresh window
        await svc.GetAccessTokenAsync(CancellationToken.None);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Invalidate_forces_refresh()
    {
        var (svc, handler, _) = Make();
        await svc.GetAccessTokenAsync(CancellationToken.None);
        svc.Invalidate();
        await svc.GetAccessTokenAsync(CancellationToken.None);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Concurrent_calls_fetch_once()
    {
        var (svc, handler, _) = Make();
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => svc.GetAccessTokenAsync(CancellationToken.None));
        await Task.WhenAll(tasks);
        Assert.Equal(1, handler.Calls);
    }
}
