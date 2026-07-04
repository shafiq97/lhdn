using System.Linq;
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
    public async Task Concurrent_same_key_requests_never_surface_5xx()
    {
        var key = Guid.NewGuid().ToString();
        var body = UblMapperTests.Sample();
        body.InvoiceNumber = $"INV-{Guid.NewGuid():N}";

        // Fire several parallel POSTs with the same Idempotency-Key and body. Both requests
        // can pass the check-then-act read before either commits, so LHDN may see the
        // invoice submitted more than once and the two responses may carry different
        // SubmissionUids (known v1 limitation - no distributed lock/outbox). What must hold
        // regardless of who wins the race is that neither request surfaces a raw 500 from
        // the loser's primary-key violation on insert.
        var tasks = Enumerable.Range(0, 5).Select(_ => _client.SendAsync(Post(body, key)));
        var responses = await Task.WhenAll(tasks);

        foreach (var response in responses)
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                $"Expected 200/201 but got {(int)response.StatusCode}");
        }

        // A subsequent request with the same key must replay cleanly (200), proving the
        // idempotency record settled correctly after the race.
        var replayResponse = await _client.SendAsync(Post(body, key));
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
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
