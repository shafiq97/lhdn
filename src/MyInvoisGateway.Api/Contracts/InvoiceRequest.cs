using System.ComponentModel.DataAnnotations;

namespace MyInvoisGateway.Api.Contracts;

public class InvoiceRequest
{
    [Required, MaxLength(50)] public required string InvoiceNumber { get; set; }
    [Required] public required DateTime IssueDateUtc { get; set; }
    [Required, MaxLength(3)] public required string CurrencyCode { get; set; }
    [Required, MaxLength(20)] public required string SellerTin { get; set; }
    [Required, MaxLength(200)] public required string SellerName { get; set; }
    [Required, MaxLength(20)] public required string BuyerTin { get; set; }
    [Required, MaxLength(200)] public required string BuyerName { get; set; }
    [Required, MinLength(1)] public required List<InvoiceLine> Lines { get; set; }
}

public class InvoiceLine
{
    [Required, MaxLength(300)] public required string Description { get; set; }
    [Range(0.0001, double.MaxValue)] public required decimal Quantity { get; set; }
    [Range(0, double.MaxValue)] public required decimal UnitPrice { get; set; }
}
