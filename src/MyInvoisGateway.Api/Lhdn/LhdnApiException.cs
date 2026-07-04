namespace MyInvoisGateway.Api.Lhdn;

public class LhdnApiException(int statusCode, string[] errors)
    : Exception($"LHDN returned {statusCode}: {string.Join("; ", errors)}")
{
    public int StatusCode { get; } = statusCode;
    public string[] Errors { get; } = errors;
}
