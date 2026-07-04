using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MyInvoisGateway.Api.Lhdn;

public class MyInvoisHttpClient(HttpClient http, ITokenService tokens) : IMyInvoisClient
{
    public async Task<SubmissionResult> SubmitDocumentAsync(UblDocument doc, CancellationToken ct)
    {
        var payload = new
        {
            documents = new[]
            {
                new
                {
                    format = "JSON",
                    documentHash = doc.HashHex,
                    codeNumber = doc.CodeNumber,
                    document = doc.Base64,
                },
            },
        };
        using var response = await SendWithAuthRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "/api/v1.0/documentsubmissions")
            {
                Content = JsonContent.Create(payload),
            }, ct);
        await EnsureLhdnSuccessAsync(response, ct);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var rejected = body.RootElement.GetProperty("rejectedDocuments");
        if (rejected.GetArrayLength() > 0)
        {
            var errors = rejected.EnumerateArray()
                .Select(d => d.GetProperty("error").GetProperty("message").GetString() ?? "rejected")
                .ToArray();
            throw new LhdnApiException(422, errors);
        }
        var accepted = body.RootElement.GetProperty("acceptedDocuments")[0];
        return new SubmissionResult(
            body.RootElement.GetProperty("submissionUid").GetString()!,
            accepted.GetProperty("uuid").GetString()!);
    }

    public async Task<SubmissionStatusResult> GetSubmissionAsync(string submissionUid, CancellationToken ct)
    {
        using var response = await SendWithAuthRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"/api/v1.0/documentsubmissions/{submissionUid}"), ct);
        await EnsureLhdnSuccessAsync(response, ct);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = body.RootElement;
        string? documentUid = root.TryGetProperty("documentSummary", out var summary) && summary.GetArrayLength() > 0
            ? summary[0].GetProperty("uuid").GetString()
            : null;
        string[]? errors = root.TryGetProperty("documentErrors", out var errs) && errs.ValueKind == JsonValueKind.Array
            ? errs.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
            : null;
        return new SubmissionStatusResult(root.GetProperty("overallStatus").GetString()!, documentUid, errors);
    }

    public async Task CancelDocumentAsync(string documentUid, string reason, CancellationToken ct)
    {
        using var response = await SendWithAuthRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Put, $"/api/v1.0/documents/state/{documentUid}/state")
            {
                Content = JsonContent.Create(new { status = "cancelled", reason }),
            }, ct);
        await EnsureLhdnSuccessAsync(response, ct);
    }

    private async Task<HttpResponseMessage> SendWithAuthRetryAsync(
        Func<HttpRequestMessage> makeRequest, CancellationToken ct)
    {
        var response = await SendAsync(makeRequest(), ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            tokens.Invalidate();
            response = await SendAsync(makeRequest(), ct);
        }
        return response;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await tokens.GetAccessTokenAsync(ct));
        return await http.SendAsync(request, ct);
    }

    private static async Task EnsureLhdnSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var status = (int)response.StatusCode;
        string[] errors;
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            errors = body.RootElement.TryGetProperty("error", out var err)
                ? [err.GetProperty("message").GetString() ?? response.ReasonPhrase ?? "error"]
                : [response.ReasonPhrase ?? "error"];
        }
        catch (JsonException)
        {
            errors = [response.ReasonPhrase ?? "error"];
        }
        if (status is >= 400 and < 500) throw new LhdnApiException(status, errors);
        response.EnsureSuccessStatusCode(); // 5xx -> HttpRequestException (resilience handler retries upstream)
    }
}
