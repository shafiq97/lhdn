namespace MyInvoisGateway.Api.Domain;

public class Invoice
{
    public Guid Id { get; set; }
    public required string InvoiceNumber { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Submitted;
    public required string SubmissionUid { get; set; }
    public required string DocumentUid { get; set; }
    public string? ErrorsJson { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastPolledUtc { get; set; }

    public void MarkValid()
    {
        if (Status != InvoiceStatus.Submitted)
            throw new InvalidOperationException($"Cannot mark {Status} invoice as Valid.");
        Status = InvoiceStatus.Valid;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkInvalid(string errorsJson)
    {
        if (Status != InvoiceStatus.Submitted)
            throw new InvalidOperationException($"Cannot mark {Status} invoice as Invalid.");
        Status = InvoiceStatus.Invalid;
        ErrorsJson = errorsJson;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkCancelled()
    {
        if (Status != InvoiceStatus.Valid)
            throw new InvalidOperationException($"Cannot cancel {Status} invoice; only Valid invoices can be cancelled.");
        Status = InvoiceStatus.Cancelled;
        UpdatedUtc = DateTime.UtcNow;
    }
}
