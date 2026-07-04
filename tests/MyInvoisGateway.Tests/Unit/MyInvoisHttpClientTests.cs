using System.Net;
using System.Text;
using System.Text.Json;
using MyInvoisGateway.Api.Lhdn;

namespace MyInvoisGateway.Tests.Unit;

public sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> script) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(script(request));
    }
}

public sealed class FakeTokenService : ITokenService
{
    public int InvalidateCalls;
    public Task<string> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult("fake-token");
    public void Invalidate() => InvalidateCalls++;
}

public class MyInvoisHttpClientTests
{
    private static HttpResponseMessage Json(HttpStatusCode code, object body) => new(code)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    private static (MyInvoisHttpClient client, ScriptedHandler handler, FakeTokenService tokens) Make(
        Func<HttpRequestMessage, HttpResponseMessage> script)
    {
        var handler = new ScriptedHandler(script);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://mock-lhdn") };
        var tokens = new FakeTokenService();
        return (new MyInvoisHttpClient(http, tokens), handler, tokens);
    }

    private static UblDocument Doc() => UblMapper.Map(UblMapperTests.Sample());

    [Fact]
    public async Task Submit_posts_document_and_returns_uids()
    {
        var (client, handler, _) = Make(_ => Json(HttpStatusCode.OK, new
        {
            submissionUid = "SUB123",
            acceptedDocuments = new[] { new { uuid = "DOC456", invoiceCodeNumber = "INV-2026-001" } },
            rejectedDocuments = Array.Empty<object>(),
        }));

        var result = await client.SubmitDocumentAsync(Doc(), CancellationToken.None);

        Assert.Equal("SUB123", result.SubmissionUid);
        Assert.Equal("DOC456", result.DocumentUid);
        var req = handler.Requests.Single();
        Assert.Equal("/api/v1.0/documentsubmissions", req.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer fake-token", req.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Submit_rejected_document_throws_LhdnApiException()
    {
        var (client, _, _) = Make(_ => Json(HttpStatusCode.OK, new
        {
            submissionUid = "SUB123",
            acceptedDocuments = Array.Empty<object>(),
            rejectedDocuments = new[]
            {
                new { invoiceCodeNumber = "INV-2026-001", error = new { message = "invalid TIN" } },
            },
        }));

        var ex = await Assert.ThrowsAsync<LhdnApiException>(
            () => client.SubmitDocumentAsync(Doc(), CancellationToken.None));
        Assert.Contains("invalid TIN", ex.Errors[0]);
    }

    [Fact]
    public async Task Submit_400_throws_LhdnApiException_with_status()
    {
        var (client, _, _) = Make(_ => Json(HttpStatusCode.BadRequest, new
        {
            error = new { message = "bad structure" },
        }));

        var ex = await Assert.ThrowsAsync<LhdnApiException>(
            () => client.SubmitDocumentAsync(Doc(), CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_401_invalidates_token_and_retries_once()
    {
        var calls = 0;
        var (client, _, tokens) = Make(_ => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : Json(HttpStatusCode.OK, new
            {
                submissionUid = "SUB123",
                acceptedDocuments = new[] { new { uuid = "DOC456", invoiceCodeNumber = "INV-2026-001" } },
                rejectedDocuments = Array.Empty<object>(),
            }));

        var result = await client.SubmitDocumentAsync(Doc(), CancellationToken.None);

        Assert.Equal("SUB123", result.SubmissionUid);
        Assert.Equal(1, tokens.InvalidateCalls);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetSubmission_returns_status_and_errors()
    {
        var (client, handler, _) = Make(_ => Json(HttpStatusCode.OK, new
        {
            overallStatus = "Invalid",
            documentSummary = new[]
            {
                new { uuid = "DOC456", status = "Invalid" },
            },
            documentErrors = new[] { "TIN mismatch" },
        }));

        var status = await client.GetSubmissionAsync("SUB123", CancellationToken.None);

        Assert.Equal("Invalid", status.OverallStatus);
        Assert.Equal("/api/v1.0/documentsubmissions/SUB123", handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Cancel_puts_state_change()
    {
        var (client, handler, _) = Make(_ => Json(HttpStatusCode.OK, new { uuid = "DOC456", status = "Cancelled" }));

        await client.CancelDocumentAsync("DOC456", "duplicate", CancellationToken.None);

        var req = handler.Requests.Single();
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.Equal("/api/v1.0/documents/state/DOC456/state", req.RequestUri!.AbsolutePath);
    }
}
