using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MockLhdn;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SubmissionStore>();
var app = builder.Build();

var clientId = app.Configuration["Mock:ClientId"] ?? "mock-client";
var clientSecret = app.Configuration["Mock:ClientSecret"] ?? "mock-secret";
var validationDelay = TimeSpan.FromSeconds(app.Configuration.GetValue("Mock:ValidationDelaySeconds", 5));
var cancelWindow = TimeSpan.FromMinutes(app.Configuration.GetValue("Mock:CancelWindowMinutes", 5));

app.MapPost("/connect/token", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();
    if (form["client_id"] != clientId || form["client_secret"] != clientSecret)
        return Results.Json(new { error = "invalid_client" }, statusCode: 401);
    return Results.Ok(new
    {
        access_token = $"mock-{Guid.NewGuid():N}",
        token_type = "Bearer",
        expires_in = 3600,
    });
});

app.MapPost("/api/v1.0/documentsubmissions", async (HttpRequest request, SubmissionStore store) =>
{
    if (!HasBearer(request)) return Results.Unauthorized();
    using var body = await JsonDocument.ParseAsync(request.Body);
    var doc = body.RootElement.GetProperty("documents")[0];
    var base64 = doc.GetProperty("document").GetString()!;
    var declaredHash = doc.GetProperty("documentHash").GetString()!;
    var codeNumber = doc.GetProperty("codeNumber").GetString()!;

    var raw = Convert.FromBase64String(base64);
    var actualHash = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
    if (actualHash != declaredHash)
        return Results.Ok(Rejected(codeNumber, "documentHash mismatch"));

    using var ubl = JsonDocument.Parse(Encoding.UTF8.GetString(raw));
    var invoice = ubl.RootElement.GetProperty("Invoice")[0];
    if (!invoice.TryGetProperty("ID", out _) || !invoice.TryGetProperty("LegalMonetaryTotal", out _))
        return Results.Ok(Rejected(codeNumber, "missing required UBL fields"));

    var sellerTin = invoice.GetProperty("AccountingSupplierParty")[0]
        .GetProperty("Party")[0].GetProperty("PartyIdentification")[0]
        .GetProperty("ID")[0].GetProperty("_").GetString()!;

    var sub = store.Add(codeNumber, willBeInvalid: sellerTin.EndsWith('9'));
    return Results.Ok(new
    {
        submissionUid = sub.SubmissionUid,
        acceptedDocuments = new[] { new { uuid = sub.DocumentUid, invoiceCodeNumber = codeNumber } },
        rejectedDocuments = Array.Empty<object>(),
    });
});

app.MapGet("/api/v1.0/documentsubmissions/{submissionUid}", (string submissionUid, HttpRequest request, SubmissionStore store) =>
{
    if (!HasBearer(request)) return Results.Unauthorized();
    var sub = store.BySubmission(submissionUid);
    if (sub is null) return Results.NotFound(new { error = new { message = "submission not found" } });

    string status = sub.Cancelled ? "Cancelled"
        : DateTimeOffset.UtcNow - sub.SubmittedAt < validationDelay ? "InProgress"
        : sub.WillBeInvalid ? "Invalid" : "Valid";

    return Results.Ok(new
    {
        overallStatus = status,
        documentSummary = new[] { new { uuid = sub.DocumentUid, status } },
        documentErrors = status == "Invalid" ? new[] { "Mock validation failed: seller TIN flagged" } : null,
    });
});

app.MapPut("/api/v1.0/documents/state/{documentUid}/state", (string documentUid, HttpRequest request, SubmissionStore store) =>
{
    if (!HasBearer(request)) return Results.Unauthorized();
    var sub = store.ByDocument(documentUid);
    if (sub is null) return Results.NotFound(new { error = new { message = "document not found" } });
    if (DateTimeOffset.UtcNow - sub.SubmittedAt > cancelWindow)
        return Results.BadRequest(new { error = new { message = "cancellation window elapsed" } });
    sub.Cancelled = true;
    return Results.Ok(new { uuid = documentUid, status = "Cancelled" });
});

app.Run();

static bool HasBearer(HttpRequest request) =>
    request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal);

static object Rejected(string codeNumber, string message) => new
{
    submissionUid = (string?)null,
    acceptedDocuments = Array.Empty<object>(),
    rejectedDocuments = new[] { new { invoiceCodeNumber = codeNumber, error = new { message } } },
};

public partial class Program { } // for WebApplicationFactory
