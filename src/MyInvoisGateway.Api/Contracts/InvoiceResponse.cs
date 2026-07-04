namespace MyInvoisGateway.Api.Contracts;

public record InvoiceResponse(
    Guid Id,
    string InvoiceNumber,
    string Status,
    string SubmissionUid,
    string DocumentUid,
    string[]? Errors);
