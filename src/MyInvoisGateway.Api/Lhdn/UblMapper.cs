using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyInvoisGateway.Api.Contracts;

namespace MyInvoisGateway.Api.Lhdn;

public static class UblMapper
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static UblDocument Map(InvoiceRequest r)
    {
        var total = r.Lines.Sum(l => l.Quantity * l.UnitPrice);
        var doc = new Dictionary<string, object>
        {
            ["_D"] = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2",
            ["_A"] = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2",
            ["_B"] = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2",
            ["Invoice"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["ID"] = Txt(r.InvoiceNumber),
                    ["IssueDate"] = Txt(r.IssueDateUtc.ToString("yyyy-MM-dd")),
                    ["IssueTime"] = Txt(r.IssueDateUtc.ToString("HH:mm:ss'Z'")),
                    ["InvoiceTypeCode"] = new object[]
                    {
                        new Dictionary<string, object> { ["_"] = "01", ["listVersionID"] = "1.0" },
                    },
                    ["DocumentCurrencyCode"] = Txt(r.CurrencyCode),
                    ["AccountingSupplierParty"] = Party(r.SellerTin, r.SellerName),
                    ["AccountingCustomerParty"] = Party(r.BuyerTin, r.BuyerName),
                    ["InvoiceLine"] = r.Lines.Select((l, i) => (object)new Dictionary<string, object>
                    {
                        ["ID"] = Txt((i + 1).ToString()),
                        ["InvoicedQuantity"] = new object[]
                        {
                            new Dictionary<string, object> { ["_"] = l.Quantity, ["unitCode"] = "C62" },
                        },
                        ["LineExtensionAmount"] = Amt(l.Quantity * l.UnitPrice, r.CurrencyCode),
                        ["Item"] = new object[]
                        {
                            new Dictionary<string, object> { ["Description"] = Txt(l.Description) },
                        },
                        ["Price"] = new object[]
                        {
                            new Dictionary<string, object> { ["PriceAmount"] = Amt(l.UnitPrice, r.CurrencyCode) },
                        },
                    }).ToArray(),
                    ["LegalMonetaryTotal"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["TaxExclusiveAmount"] = Amt(total, r.CurrencyCode),
                            ["TaxInclusiveAmount"] = Amt(total, r.CurrencyCode),
                            ["PayableAmount"] = Amt(total, r.CurrencyCode),
                        },
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(doc, Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new UblDocument(r.InvoiceNumber, json, hash, Convert.ToBase64String(bytes));
    }

    private static object[] Txt(string value) =>
        [new Dictionary<string, object> { ["_"] = value }];

    private static object[] Amt(decimal value, string currency) =>
        [new Dictionary<string, object> { ["_"] = decimal.Round(value, 2), ["currencyID"] = currency }];

    private static object[] Party(string tin, string name) =>
        [new Dictionary<string, object>
        {
            ["Party"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["PartyIdentification"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["ID"] = new object[]
                            {
                                new Dictionary<string, object> { ["_"] = tin, ["schemeID"] = "TIN" },
                            },
                        },
                    },
                    ["PartyLegalEntity"] = new object[]
                    {
                        new Dictionary<string, object> { ["RegistrationName"] = Txt(name) },
                    },
                },
            },
        }];
}
