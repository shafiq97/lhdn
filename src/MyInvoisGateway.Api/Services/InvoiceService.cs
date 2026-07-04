using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyInvoisGateway.Api.Contracts;
using MyInvoisGateway.Api.Data;
using MyInvoisGateway.Api.Domain;
using MyInvoisGateway.Api.Lhdn;

namespace MyInvoisGateway.Api.Services;

public class IdempotencyConflictException : Exception;

public interface IInvoiceService
{
    Task<(InvoiceResponse Response, bool Replayed)> SubmitAsync(InvoiceRequest request, string idempotencyKey, CancellationToken ct);
    Task<InvoiceResponse?> GetAsync(Guid id, CancellationToken ct);
    Task<InvoiceResponse?> CancelAsync(Guid id, string reason, CancellationToken ct);
}

public class InvoiceService(AppDbContext db, IMyInvoisClient lhdn, ILogger<InvoiceService> log) : IInvoiceService
{
    private static readonly TimeSpan PollStaleness = TimeSpan.FromSeconds(30);

    public async Task<(InvoiceResponse, bool)> SubmitAsync(InvoiceRequest request, string idempotencyKey, CancellationToken ct)
    {
        var requestHash = Hash(JsonSerializer.Serialize(request));
        var existing = await db.IdempotencyRecords.FindAsync([idempotencyKey], ct);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash) throw new IdempotencyConflictException();
            return (JsonSerializer.Deserialize<InvoiceResponse>(existing.ResponseJson)!, true);
        }

        var doc = UblMapper.Map(request);
        var result = await lhdn.SubmitDocumentAsync(doc, ct);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = request.InvoiceNumber,
            SubmissionUid = result.SubmissionUid,
            DocumentUid = result.DocumentUid,
        };
        db.Invoices.Add(invoice);

        var response = ToResponse(invoice);
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = idempotencyKey,
            RequestHash = requestHash,
            ResponseJson = JsonSerializer.Serialize(response),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two concurrent requests with the same Idempotency-Key both pass the initial
            // FindAsync check (check-then-act race), both submit to LHDN, and then race to
            // insert the IdempotencyRecord row. The loser hits a primary-key violation here.
            // Known v1 limitation: without a distributed lock or an outbox, the LHDN-side
            // duplicate submission in this race window is not prevented - we only ensure the
            // HTTP response for the losing request is a clean replay/conflict instead of a 500.
            db.ChangeTracker.Clear();
            var winning = await db.IdempotencyRecords.FindAsync([idempotencyKey], ct);
            if (winning is null) throw;
            if (winning.RequestHash != requestHash) throw new IdempotencyConflictException();
            return (JsonSerializer.Deserialize<InvoiceResponse>(winning.ResponseJson)!, true);
        }

        log.LogInformation("Invoice {InvoiceNumber} submitted as {SubmissionUid}", invoice.InvoiceNumber, result.SubmissionUid);
        return (response, false);
    }

    public async Task<InvoiceResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return null;

        var stale = invoice.LastPolledUtc is null || DateTime.UtcNow - invoice.LastPolledUtc > PollStaleness;
        if (invoice.Status == InvoiceStatus.Submitted && stale)
        {
            var status = await lhdn.GetSubmissionAsync(invoice.SubmissionUid, ct);
            invoice.LastPolledUtc = DateTime.UtcNow;
            switch (status.OverallStatus)
            {
                case "Valid": invoice.MarkValid(); break;
                case "Invalid": invoice.MarkInvalid(JsonSerializer.Serialize(status.Errors ?? [])); break;
                // InProgress: stays Submitted
            }
            await db.SaveChangesAsync(ct);
        }
        return ToResponse(invoice);
    }

    public async Task<InvoiceResponse?> CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return null;
        invoice.MarkCancelled(); // throws InvalidOperationException unless Valid
        await lhdn.CancelDocumentAsync(invoice.DocumentUid, reason, ct);
        await db.SaveChangesAsync(ct);
        return ToResponse(invoice);
    }

    private static InvoiceResponse ToResponse(Invoice i) => new(
        i.Id, i.InvoiceNumber, i.Status.ToString(), i.SubmissionUid, i.DocumentUid,
        i.ErrorsJson is null ? null : JsonSerializer.Deserialize<string[]>(i.ErrorsJson));

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
