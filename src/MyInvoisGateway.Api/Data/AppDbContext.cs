using Microsoft.EntityFrameworkCore;
using MyInvoisGateway.Api.Domain;

namespace MyInvoisGateway.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Invoice>().HasIndex(i => i.InvoiceNumber);
        b.Entity<IdempotencyRecord>().HasKey(r => r.Key);
    }
}
