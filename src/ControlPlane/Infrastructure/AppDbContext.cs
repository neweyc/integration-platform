using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Secret> Secrets => Set<Secret>();
    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<AgentToken> AgentTokens => Set<AgentToken>();
    public DbSet<ExecutionRecord> ExecutionRecords => Set<ExecutionRecord>();
    public DbSet<ExecutionLog> ExecutionLogs => Set<ExecutionLog>();
    public DbSet<AssemblyPackage> AssemblyPackages => Set<AssemblyPackage>();
    public DbSet<IntegrationScheduleState> IntegrationScheduleStates => Set<IntegrationScheduleState>();
    public DbSet<ManualRunRequest> ManualRunRequests => Set<ManualRunRequest>();

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
            b.Property(i => i.ClassName).IsRequired().HasMaxLength(500);

            // Slug must be unique within a tenant
            b.HasIndex(i => new { i.TenantId, i.Slug }).IsUnique();

            b.HasOne(i => i.Tenant)
             .WithMany()
             .HasForeignKey(i => i.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentToken>(b =>
        {
            b.ToTable("agent_tokens");
            b.HasKey(t => t.Id);
            b.Property(t => t.Name).IsRequired().HasMaxLength(200);
            b.Property(t => t.Environment).IsRequired().HasMaxLength(50);
            b.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);

            // Hash must be unique — no two tokens can have the same hash
            b.HasIndex(t => t.TokenHash).IsUnique();

            b.HasOne(t => t.Tenant)
             .WithMany()
             .HasForeignKey(t => t.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExecutionRecord>(b =>
        {
            b.ToTable("execution_records");
            b.HasKey(e => e.Id);
            b.Property(e => e.Environment).IsRequired().HasMaxLength(50);
            b.Property(e => e.Status).HasConversion<string>();
            b.Property(e => e.TriggerSource).HasConversion<string>().HasMaxLength(20);
            b.Property(e => e.ErrorMessage).HasMaxLength(4000);

            b.HasOne(e => e.Integration)
             .WithMany()
             .HasForeignKey(e => e.IntegrationId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(e => e.Tenant)
             .WithMany()
             .HasForeignKey(e => e.TenantId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(e => new { e.TenantId, e.IntegrationId, e.StartedAt });
        });

        modelBuilder.Entity<ExecutionLog>(b =>
        {
            b.ToTable("execution_logs");
            b.HasKey(l => l.Id);
            b.Property(l => l.Level).IsRequired().HasMaxLength(20);
            b.Property(l => l.Message).IsRequired().HasMaxLength(4000);
            b.Property(l => l.Exception).HasMaxLength(8000);
            b.Property(l => l.PropertiesJson);

            b.HasOne(l => l.ExecutionRecord)
             .WithMany()
             .HasForeignKey(l => l.ExecutionRecordId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(l => l.Tenant)
             .WithMany()
             .HasForeignKey(l => l.TenantId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(l => new { l.TenantId, l.ExecutionRecordId, l.Timestamp });
        });

        modelBuilder.Entity<AssemblyPackage>(b =>
        {
            b.ToTable("assembly_packages");
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).IsRequired().HasMaxLength(200);
            b.Property(p => p.Version).IsRequired().HasMaxLength(50);
            b.Property(p => p.FileName).IsRequired().HasMaxLength(255);
            b.Property(p => p.Data).IsRequired();
            b.Property(p => p.Sha256Hash).IsRequired().HasMaxLength(64);

            // Package name + version must be unique within a tenant
            b.HasIndex(p => new { p.TenantId, p.Name, p.Version }).IsUnique();

            b.HasOne(p => p.Tenant)
             .WithMany()
             .HasForeignKey(p => p.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IntegrationScheduleState>(b =>
        {
            b.ToTable("integration_schedule_states");
            b.HasKey(s => s.Id);

            b.HasIndex(s => s.IntegrationId).IsUnique();
            b.HasIndex(s => new { s.TenantId, s.NextRunAt });
            b.HasIndex(s => new { s.TenantId, s.LeaseExpiresAt });

            b.HasOne(s => s.Integration)
             .WithMany()
             .HasForeignKey(s => s.IntegrationId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(s => s.Tenant)
             .WithMany()
             .HasForeignKey(s => s.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ManualRunRequest>(b =>
        {
            b.ToTable("manual_run_requests");
            b.HasKey(r => r.Id);
            b.Property(r => r.Environment).IsRequired().HasMaxLength(50);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

            // Index for agent polling: find pending requests for an environment
            b.HasIndex(r => new { r.TenantId, r.Environment, r.Status });

            b.HasOne(r => r.Integration)
             .WithMany()
             .HasForeignKey(r => r.IntegrationId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(r => r.Tenant)
             .WithMany()
             .HasForeignKey(r => r.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
