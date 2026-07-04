using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MyInvoisGateway.Api.Lhdn;

public interface ITokenService
{
    Task<string> GetAccessTokenAsync(CancellationToken ct);
    void Invalidate();
}

public class TokenService(HttpClient http, IOptions<LhdnOptions> options, TimeProvider time) : ITokenService
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (IsFresh()) return _token!;
        await _lock.WaitAsync(ct);
        try
        {
            if (IsFresh()) return _token!; // single-flight: winner fetched while we waited
            var response = await http.PostAsync("/connect/token", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = options.Value.ClientId,
                    ["client_secret"] = options.Value.ClientSecret,
                    ["scope"] = "InvoicingAPI",
                }), ct);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new InvalidOperationException("Empty token response.");
            _token = payload.AccessToken;
            _expiresAt = time.GetUtcNow().AddSeconds(payload.ExpiresIn);
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => _expiresAt = DateTimeOffset.MinValue;

    private bool IsFresh() => _token is not null && time.GetUtcNow() + RefreshMargin < _expiresAt;

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
