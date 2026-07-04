using System.Net;
using System.Net.Http.Json;
using MyInvoisGateway.Api.Contracts;
using MyInvoisGateway.Tests.Unit;

namespace MyInvoisGateway.Tests.Integration;

public class SubmitInvoiceTests : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client;
    public SubmitInvoiceTests(ApiFixture fixture) => _client = fixture.Client;

    private static HttpRequestMessage Post(InvoiceRequest body, string? key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/invoices") { Content = JsonContent.Create(body) };
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return req;
    }

    [Fact]
    public async Task Submit_returns_201_with_submission_uids()
    {
        var body = UblMapperTests.Sample();
        body.InvoiceNumber = $"INV-{Guid.NewGuid():N}";
        var response = await _client.SendAsync(Post(body, Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.NotNull(result);
        Assert.StartsWith("SUB-", result.SubmissionUid);
        Assert.Equal("Submitted", result.Status);
    }

    [Fact]
    public async Task Missing_idempotency_key_returns_400()
    {
        var response = await _client.SendAsync(Post(UblMapperTests.Sample(), null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Same_key_same_body_replays_response_without_resubmitting()
    {
        var key = Guid.NewGuid().ToString();
        var body = UblMapperTests.Sample();
        body.InvoiceNumber = $"INV-{Guid.NewGuid():N}";

        var first = await (await _client.SendAsync(Post(body, key))).Content.ReadFromJsonAsync<InvoiceResponse>();
        var replayResponse = await _client.SendAsync(Post(body, key));
        var replay = await replayResponse.Content.ReadFromJsonAsync<InvoiceResponse>();

        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(first!.SubmissionUid, replay!.SubmissionUid);
        Assert.Equal(first.Id, replay.Id);
    }

    [Fact]
    public async Task Same_key_different_body_returns_422()
    {
        var key = Guid.NewGuid().ToString();
        var body = UblMapperTests.Sample();
        body.InvoiceNumber = $"INV-{Guid.NewGuid():N}";
        await _client.SendAsync(Post(body, key));

        body.BuyerName = "Different Buyer";
        var response = await _client.SendAsync(Post(body, key));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_dto_returns_400()
    {
        var body = UblMapperTests.Sample();
        body.Lines = [];
        var response = await _client.SendAsync(Post(body, Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
