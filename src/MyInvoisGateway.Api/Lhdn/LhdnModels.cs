namespace MyInvoisGateway.Api.Lhdn;

public record SubmissionResult(string SubmissionUid, string DocumentUid);
public record SubmissionStatusResult(string OverallStatus, string? DocumentUid, string[]? Errors);
