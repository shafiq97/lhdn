using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MyInvoisGateway.Tests.Integration;

/// <summary>Runs MockLhdn in-process and routes the Api's outbound LHDN HTTP through it.</summary>
public sealed class ApiFixture : IDisposable
{
    private readonly WebApplicationFactory<Program> _mock; // MockLhdn's Program (partial class)
    private readonly WebApplicationFactory<MyInvoisGateway.Api.ApiMarker> _api;

    public HttpClient Client { get; }

    public ApiFixture()
    {
        _mock = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("Mock:ValidationDelaySeconds", "0"));
        var mockHandler = _mock.Server.CreateHandler();

        var dbPath = Path.Combine(Path.GetTempPath(), $"gateway-test-{Guid.NewGuid():N}.db");
        _api = new WebApplicationFactory<MyInvoisGateway.Api.ApiMarker>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Lhdn:BaseUrl", "http://mock-lhdn");
                b.UseSetting("Lhdn:ClientId", "mock-client");
                b.UseSetting("Lhdn:ClientSecret", "mock-secret");
                b.UseSetting("ConnectionStrings:Default", $"Data Source={dbPath}");
                b.ConfigureTestServices(services =>
                {
                    services.AddHttpClient("lhdn")
                        .ConfigurePrimaryHttpMessageHandler(() => mockHandler);
                    services.AddHttpClient("lhdn-token")
                        .ConfigurePrimaryHttpMessageHandler(() => mockHandler);
                });
            });
        Client = _api.CreateClient();
    }

    public void Dispose()
    {
        _api.Dispose();
        _mock.Dispose();
    }
}
