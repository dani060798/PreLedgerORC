using Microsoft.EntityFrameworkCore;
using PreLedgerORC.Models;

namespace PreLedgerORC.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<DocumentItem> DocumentItems => Set<DocumentItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Customer>(b =>
        {
            b.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<DocumentItem>(b =>
        {
            b.HasIndex(x => x.CustomerId);
            b.HasIndex(x => x.CreatedAtUtc);
            b.Property(x => x.Status).HasConversion<int>();
        });
    }
}
