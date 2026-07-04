using System.Net;
using System.Net.Http.Json;
using MyInvoisGateway.Api.Contracts;
using MyInvoisGateway.Tests.Unit;

namespace MyInvoisGateway.Tests.Integration;

public class LifecycleTests : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client;
    public LifecycleTests(ApiFixture fixture) => _client = fixture.Client;

    private async Task<InvoiceResponse> Submit(Action<InvoiceRequest>? mutate = null)
    {
        var body = UblMapperTests.Sample();
        body.InvoiceNumber = $"INV-{Guid.NewGuid():N}";
        mutate?.Invoke(body);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/invoices") { Content = JsonContent.Create(body) };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await _client.SendAsync(req);
        return (await response.Content.ReadFromJsonAsync<InvoiceResponse>())!;
    }

    [Fact]
    public async Task Submitted_invoice_becomes_valid_on_get()
    {
        var created = await Submit();
        var got = await _client.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{created.Id}");
        Assert.Equal("Valid", got!.Status); // fixture delay = 0
    }

    [Fact]
    public async Task Flagged_tin_becomes_invalid_with_errors()
    {
        var created = await Submit(b => b.SellerTin = "C1234567899"); // ends in 9 -> mock invalidates
        var got = await _client.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{created.Id}");
        Assert.Equal("Invalid", got!.Status);
        Assert.NotNull(got.Errors);
        Assert.NotEmpty(got.Errors!);
    }

    [Fact]
    public async Task Valid_invoice_can_be_cancelled()
    {
        var created = await Submit();
        await _client.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{created.Id}"); // -> Valid
        var response = await _client.PostAsJsonAsync($"/api/invoices/{created.Id}/cancel", new { reason = "duplicate" });
        var cancelled = await response.Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cancelled", cancelled!.Status);
    }

    [Fact]
    public async Task Cancelling_submitted_invoice_returns_409()
    {
        var created = await Submit(); // Submitted until first GET; cancel immediately
        var response = await _client.PostAsJsonAsync($"/api/invoices/{created.Id}/cancel", new { reason = "nope" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_invoice_returns_404()
    {
        var response = await _client.GetAsync($"/api/invoices/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
