using MyInvoisGateway.Api.Domain;

namespace MyInvoisGateway.Tests.Unit;

public class InvoiceStateMachineTests
{
    private static Invoice NewInvoice() => new()
    {
        Id = Guid.NewGuid(),
        InvoiceNumber = "INV-001",
        SubmissionUid = "SUB1",
        DocumentUid = "DOC1",
    };

    [Fact]
    public void Submitted_can_become_valid()
    {
        var inv = NewInvoice();
        inv.MarkValid();
        Assert.Equal(InvoiceStatus.Valid, inv.Status);
    }

    [Fact]
    public void Submitted_can_become_invalid_with_errors()
    {
        var inv = NewInvoice();
        inv.MarkInvalid("[\"bad TIN\"]");
        Assert.Equal(InvoiceStatus.Invalid, inv.Status);
        Assert.Equal("[\"bad TIN\"]", inv.ErrorsJson);
    }

    [Fact]
    public void Valid_can_be_cancelled()
    {
        var inv = NewInvoice();
        inv.MarkValid();
        inv.MarkCancelled();
        Assert.Equal(InvoiceStatus.Cancelled, inv.Status);
    }

    [Fact]
    public void Submitted_cannot_be_cancelled()
    {
        var inv = NewInvoice();
        Assert.Throws<InvalidOperationException>(inv.MarkCancelled);
    }

    [Fact]
    public void Invalid_cannot_become_valid()
    {
        var inv = NewInvoice();
        inv.MarkInvalid("[]");
        Assert.Throws<InvalidOperationException>(inv.MarkValid);
    }
}
