using Microsoft.AspNetCore.Mvc;
using MyInvoisGateway.Api.Contracts;
using MyInvoisGateway.Api.Lhdn;
using MyInvoisGateway.Api.Services;

namespace MyInvoisGateway.Api.Controllers;

[ApiController]
[Route("api/invoices")]
public class InvoicesController(IInvoiceService invoices) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] InvoiceRequest request, CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var key) || string.IsNullOrWhiteSpace(key))
            return Problem(statusCode: 400, title: "Missing Idempotency-Key header.");
        try
        {
            var (response, replayed) = await invoices.SubmitAsync(request, key.ToString(), ct);
            return replayed
                ? Ok(response)
                : CreatedAtAction(nameof(Get), new { id = response.Id }, response);
        }
        catch (IdempotencyConflictException)
        {
            return Problem(statusCode: 422, title: "Idempotency-Key was already used with a different request body.");
        }
        catch (LhdnApiException ex)
        {
            return Problem(statusCode: 422, title: "LHDN rejected the document.", detail: string.Join("; ", ex.Errors));
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: 502, title: "LHDN is unreachable.", detail: $"CorrelationId: {HttpContext.TraceIdentifier}");
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        try
        {
            var response = await invoices.GetAsync(id, ct);
            return response is null ? NotFound() : Ok(response);
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: 502, title: "LHDN is unreachable.", detail: $"CorrelationId: {HttpContext.TraceIdentifier}");
        }
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelRequest body, CancellationToken ct)
    {
        try
        {
            var response = await invoices.CancelAsync(id, body.Reason, ct);
            return response is null ? NotFound() : Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: 409, title: ex.Message);
        }
        catch (LhdnApiException ex)
        {
            return Problem(statusCode: 422, title: "LHDN refused cancellation.", detail: string.Join("; ", ex.Errors));
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: 502, title: "LHDN is unreachable.", detail: $"CorrelationId: {HttpContext.TraceIdentifier}");
        }
    }
}

public record CancelRequest(string Reason);
