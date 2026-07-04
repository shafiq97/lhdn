using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyInvoisGateway.Api.Contracts;
using MyInvoisGateway.Api.Lhdn;

namespace MyInvoisGateway.Tests.Unit;

public class UblMapperTests
{
    public static InvoiceRequest Sample() => new()
    {
        InvoiceNumber = "INV-2026-001",
        IssueDateUtc = new DateTime(2026, 7, 4, 0, 0, 0, DateTimeKind.Utc),
        CurrencyCode = "MYR",
        SellerTin = "C1234567890",
        SellerName = "Acme Sdn Bhd",
        BuyerTin = "C0987654321",
        BuyerName = "Beta Sdn Bhd",
        Lines =
        [
            new InvoiceLine { Description = "Widget", Quantity = 2, UnitPrice = 50.00m },
            new InvoiceLine { Description = "Service fee", Quantity = 1, UnitPrice = 25.50m },
        ],
    };

    [Fact]
    public void Maps_core_fields_into_ubl_json()
    {
        var doc = UblMapper.Map(Sample());
        using var json = JsonDocument.Parse(doc.DocumentJson);
        var inv = json.RootElement.GetProperty("Invoice")[0];
        Assert.Equal("INV-2026-001", inv.GetProperty("ID")[0].GetProperty("_").GetString());
        Assert.Equal("01", inv.GetProperty("InvoiceTypeCode")[0].GetProperty("_").GetString());
        Assert.Equal("2026-07-04", inv.GetProperty("IssueDate")[0].GetProperty("_").GetString());
        Assert.Equal(2, inv.GetProperty("InvoiceLine").GetArrayLength());
    }

    [Fact]
    public void Total_is_sum_of_lines()
    {
        var doc = UblMapper.Map(Sample());
        using var json = JsonDocument.Parse(doc.DocumentJson);
        var total = json.RootElement.GetProperty("Invoice")[0]
            .GetProperty("LegalMonetaryTotal")[0]
            .GetProperty("PayableAmount")[0].GetProperty("_").GetDecimal();
        Assert.Equal(125.50m, total);
    }

    [Fact]
    public void Hash_is_sha256_hex_of_document_json()
    {
        var doc = UblMapper.Map(Sample());
        var expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(doc.DocumentJson))).ToLowerInvariant();
        Assert.Equal(expected, doc.HashHex);
    }

    [Fact]
    public void Base64_decodes_back_to_document_json()
    {
        var doc = UblMapper.Map(Sample());
        Assert.Equal(doc.DocumentJson, Encoding.UTF8.GetString(Convert.FromBase64String(doc.Base64)));
    }

    [Fact]
    public void CodeNumber_is_invoice_number()
    {
        Assert.Equal("INV-2026-001", UblMapper.Map(Sample()).CodeNumber);
    }
}
