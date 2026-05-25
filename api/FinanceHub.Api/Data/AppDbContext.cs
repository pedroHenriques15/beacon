using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<MonthlyStatement>  MonthlyStatements  => Set<MonthlyStatement>();
    public DbSet<Transaction>       Transactions       => Set<Transaction>();
    public DbSet<Category>          Categories         => Set<Category>();
    public DbSet<CategoryRule>      CategoryRules      => Set<CategoryRule>();
    public DbSet<SalaryProfile>     SalaryProfiles     => Set<SalaryProfile>();
    public DbSet<SalarySlip>        SalarySlips        => Set<SalarySlip>();
    public DbSet<SalaryLineItem>    SalaryLineItems    => Set<SalaryLineItem>();
    public DbSet<SalaryItemCategory> SalaryItemCategories => Set<SalaryItemCategory>();
    public DbSet<GroceryReceipt>                GroceryReceipts                => Set<GroceryReceipt>();
    public DbSet<GroceryItem>                   GroceryItems                   => Set<GroceryItem>();
    public DbSet<GroceryCategory>               GroceryCategories              => Set<GroceryCategory>();
    public DbSet<GroceryCategoryRule>           GroceryCategoryRules           => Set<GroceryCategoryRule>();
    public DbSet<GroceryReceiptCategoryMapping> GroceryReceiptCategoryMappings => Set<GroceryReceiptCategoryMapping>();
    public DbSet<GoogleOAuthToken>              GoogleOAuthTokens              => Set<GoogleOAuthToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MonthlyStatement>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Bank).HasMaxLength(50).IsRequired();
            e.Property(s => s.Account).HasMaxLength(100).IsRequired();
            e.Property(s => s.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(s => s.OpeningBalance).HasColumnType("decimal(18,2)");
            e.Property(s => s.ClosingBalance).HasColumnType("decimal(18,2)");
            e.Property(s => s.PprBalance).HasColumnType("decimal(18,2)");
            e.Property(s => s.SourceFile).HasMaxLength(500);
            e.HasIndex(s => new { s.Bank, s.PeriodFrom }).IsUnique();
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Description).HasMaxLength(500).IsRequired();
            e.Property(t => t.Amount).HasColumnType("decimal(18,2)");
            e.Property(t => t.Balance).HasColumnType("decimal(18,2)");
            e.Property(t => t.Type).HasMaxLength(20);
            e.HasOne(t => t.Statement)
             .WithMany(s => s.Transactions)
             .HasForeignKey(t => t.StatementId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Category)
             .WithMany()
             .HasForeignKey(t => t.CategoryId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(100).IsRequired();
            e.Property(c => c.Color).HasMaxLength(20);
            e.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<CategoryRule>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Pattern).HasMaxLength(200).IsRequired(false);
            e.Property(r => r.Value).HasColumnType("decimal(18,2)");
            e.HasOne(r => r.Category)
             .WithMany(c => c.Rules)
             .HasForeignKey(r => r.CategoryId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SalaryProfile>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(100).IsRequired();
            e.Property(p => p.Description).HasMaxLength(500);
            e.HasIndex(p => p.Name).IsUnique();
        });

        modelBuilder.Entity<SalaryItemCategory>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(100).IsRequired();
            e.Property(c => c.Color).HasMaxLength(20);
            e.Property(c => c.ItemType).HasMaxLength(20);
            e.HasIndex(c => new { c.SalaryProfileId, c.Name }).IsUnique();
            e.HasOne(c => c.SalaryProfile)
             .WithMany(p => p.SalaryItemCategories)
             .HasForeignKey(c => c.SalaryProfileId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SalarySlip>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.GrossAmount).HasColumnType("decimal(18,2)");
            e.Property(s => s.NetAmount).HasColumnType("decimal(18,2)");
            e.Property(s => s.Notes).HasMaxLength(1000);
            e.Property(s => s.SourceFile).HasMaxLength(500);
            e.Property(s => s.BaseAmount).HasColumnType("decimal(18,2)");
            e.Property(s => s.HoursWorked).HasColumnType("decimal(18,2)");
            e.Property(s => s.HourlyRate).HasColumnType("decimal(18,2)");
            e.Property(s => s.TotalEspecie).HasColumnType("decimal(18,2)");
            e.HasOne(s => s.SalaryProfile)
             .WithMany(p => p.SalarySlips)
             .HasForeignKey(s => s.SalaryProfileId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.SalaryProfileId, s.Period }).IsUnique();
        });

        modelBuilder.Entity<SalaryLineItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Amount).HasColumnType("decimal(18,2)");
            e.Property(i => i.Quantity).HasColumnType("decimal(18,2)");
            e.Property(i => i.UnitValue).HasColumnType("decimal(18,2)");
            e.Property(i => i.Percentage).HasColumnType("decimal(18,2)");
            e.Property(i => i.IncidenciaBase).HasColumnType("decimal(18,2)");
            e.HasOne(i => i.SalarySlip)
             .WithMany(s => s.LineItems)
             .HasForeignKey(i => i.SalarySlipId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.SalaryItemCategory)
             .WithMany(c => c.LineItems)
             .HasForeignKey(i => i.SalaryItemCategoryId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GroceryReceipt>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.StoreName).HasMaxLength(200).IsRequired();
            e.Property(r => r.Total).HasColumnType("decimal(18,2)");
            e.Property(r => r.Notes).HasMaxLength(1000);
            e.Property(r => r.SourceFile).HasMaxLength(500);
            e.Property(r => r.PdfPath).HasMaxLength(500);
            e.Property(r => r.FileHash).HasMaxLength(64);
            e.HasMany(r => r.Items)
             .WithOne(i => i.Receipt)
             .HasForeignKey(i => i.ReceiptId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GroceryItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Description).HasMaxLength(500).IsRequired();
            e.Property(i => i.Amount).HasColumnType("decimal(18,2)");
            e.Property(i => i.Quantity).HasColumnType("decimal(18,4)");
            e.Property(i => i.ReceiptCategory).HasMaxLength(200);
            e.HasOne(i => i.Category)
             .WithMany()
             .HasForeignKey(i => i.CategoryId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);
        });

        modelBuilder.Entity<GroceryCategory>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(100).IsRequired();
            e.Property(c => c.Color).HasMaxLength(20);
            e.HasIndex(c => c.Name).IsUnique();
            e.HasMany(c => c.Rules)
             .WithOne(r => r.Category)
             .HasForeignKey(r => r.CategoryId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GroceryCategoryRule>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Pattern).HasMaxLength(200).IsRequired(false);
            e.Property(r => r.Value).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<GroceryReceiptCategoryMapping>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.ReceiptCategoryName).HasMaxLength(200).IsRequired();
            e.HasIndex(m => m.ReceiptCategoryName).IsUnique();
            e.HasOne(m => m.Category)
             .WithMany()
             .HasForeignKey(m => m.GroceryCategoryId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GoogleOAuthToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.AccessToken).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(t => t.RefreshToken).HasMaxLength(512).IsRequired();
            e.Property(t => t.Scopes).HasMaxLength(500);
            e.ToTable(t => t.HasCheckConstraint("CK_SingleToken", "Id = 1"));
        });
    }
}
