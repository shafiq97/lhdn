namespace MyInvoisGateway.Api.Lhdn;

public class LhdnOptions
{
    public const string Section = "Lhdn";
    public required string BaseUrl { get; set; }
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
}
