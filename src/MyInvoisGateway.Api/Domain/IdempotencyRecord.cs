namespace MyInvoisGateway.Api.Domain;

public class IdempotencyRecord
{
    public required string Key { get; set; }
    public required string RequestHash { get; set; }
    public required string ResponseJson { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
