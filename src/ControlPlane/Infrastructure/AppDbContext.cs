using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Secret> Secrets => Set<Secret>();
    public DbSet<Integration> Integrations => Set<Integration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.HasKey(t => t.Id);
            b.Property(t => t.Name).IsRequired().HasMaxLength(200);
            b.Property(t => t.Slug).IsRequired().HasMaxLength(100);
            b.HasIndex(t => t.Slug).IsUnique();
            b.Property(t => t.Status).HasConversion<string>();
        });

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(u => u.Id);
            b.Property(u => u.Email).IsRequired().HasMaxLength(300);
            b.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
            b.Property(u => u.PasswordHash).IsRequired();
            b.Property(u => u.Role).HasConversion<string>();
            b.HasOne(u => u.Tenant)
             .WithMany()
             .HasForeignKey(u => u.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Secret>(b =>
        {
            b.ToTable("secrets");
            b.HasKey(s => s.Id);
            b.Property(s => s.Environment).IsRequired().HasMaxLength(50);
            b.Property(s => s.Key).IsRequired().HasMaxLength(200);
            b.Property(s => s.EncryptedValue).IsRequired();

            // Each secret key must be unique within a tenant + environment combination
            b.HasIndex(s => new { s.TenantId, s.Environment, s.Key }).IsUnique();

            b.HasOne(s => s.Tenant)
             .WithMany()
             .HasForeignKey(s => s.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Integration>(b =>
        {
            b.ToTable("integrations");
            b.HasKey(i => i.Id);
            b.Property(i => i.Name).IsRequired().HasMaxLength(200);
            b.Property(i => i.Slug).IsRequired().HasMaxLength(100);
            b.Property(i => i.Description).HasMaxLength(1000);
            b.Property(i => i.Environment).IsRequired().HasMaxLength(50);
            b.Property(i => i.Status).HasConversion<string>();
            b.Property(i => i.TriggerType).HasConversion<string>();
            b.Property(i => i.CronExpression).HasMaxLength(100);

            // Slug must be unique within a tenant
            b.HasIndex(i => new { i.TenantId, i.Slug }).IsUnique();

            b.HasOne(i => i.Tenant)
             .WithMany()
             .HasForeignKey(i => i.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
