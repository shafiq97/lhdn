namespace MyInvoisGateway.Api.Lhdn;

public interface IMyInvoisClient
{
    Task<SubmissionResult> SubmitDocumentAsync(UblDocument doc, CancellationToken ct);
    Task<SubmissionStatusResult> GetSubmissionAsync(string submissionUid, CancellationToken ct);
    Task CancelDocumentAsync(string documentUid, string reason, CancellationToken ct);
}
