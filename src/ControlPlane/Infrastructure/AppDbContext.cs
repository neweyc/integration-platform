using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Secret> Secrets => Set<Secret>();
    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<IntegrationTrigger> IntegrationTriggers => Set<IntegrationTrigger>();
    public DbSet<AgentToken> AgentTokens => Set<AgentToken>();
    public DbSet<ExecutionRecord> ExecutionRecords => Set<ExecutionRecord>();
    public DbSet<ExecutionLog> ExecutionLogs => Set<ExecutionLog>();
    public DbSet<AssemblyPackage> AssemblyPackages => Set<AssemblyPackage>();
    public DbSet<IntegrationScheduleState> IntegrationScheduleStates => Set<IntegrationScheduleState>();
    public DbSet<ManualRunRequest> ManualRunRequests => Set<ManualRunRequest>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<TriggerEvent> TriggerEvents => Set<TriggerEvent>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowNode> WorkflowNodes => Set<WorkflowNode>();
    public DbSet<WorkflowEdge> WorkflowEdges => Set<WorkflowEdge>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<WorkflowNodeRun> WorkflowNodeRuns => Set<WorkflowNodeRun>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<AgentHeartbeat> AgentHeartbeats => Set<AgentHeartbeat>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();

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
            b.Property(t => t.MaxExecutionsPerMonth).HasDefaultValue(1000);
            b.Property(t => t.StripeCustomerId).HasMaxLength(100);
            b.Property(t => t.StripeSubscriptionId).HasMaxLength(100);
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
            b.Property(i => i.ClassName).IsRequired().HasMaxLength(500);
            b.Property(i => i.RetryMaxAttempts);
            b.Property(i => i.RetryBackoffSeconds);
            b.Property(i => i.PackageId);

            // Slug must be unique within a tenant
            b.HasIndex(i => new { i.TenantId, i.Slug }).IsUnique();

            b.HasOne<AssemblyPackage>()
             .WithMany()
             .HasForeignKey(i => i.PackageId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);

            b.HasOne(i => i.Tenant)
             .WithMany()
             .HasForeignKey(i => i.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IntegrationTrigger>(b =>
        {
            b.ToTable("integration_triggers");
            b.HasKey(t => t.Id);
            b.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
            b.Property(t => t.Name).IsRequired().HasMaxLength(200);
            b.Property(t => t.Slug).IsRequired().HasMaxLength(100);
            b.Property(t => t.CronExpression).HasMaxLength(100);
            b.Property(t => t.EncryptedWebhookSecret);
            b.HasIndex(t => new { t.TenantId, t.IntegrationId, t.Slug }).IsUnique();
            b.HasIndex(t => new { t.TenantId, t.Type, t.Enabled });

            b.HasOne(t => t.Integration)
             .WithMany(i => i.Triggers)
             .HasForeignKey(t => t.IntegrationId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(t => t.Tenant)
             .WithMany()
             .HasForeignKey(t => t.TenantId)
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
            b.Property(e => e.WorkItemId);
            b.Property(e => e.PackageId);
            b.Property(e => e.PackageName).HasMaxLength(200);
            b.Property(e => e.PackageVersion).HasMaxLength(50);
            b.Property(e => e.AttemptNumber);
            b.Property(e => e.ParentExecutionId);
            b.Property(e => e.RootExecutionId);

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

            b.HasIndex(s => s.IntegrationTriggerId).IsUnique();
            b.HasIndex(s => new { s.TenantId, s.NextRunAt });

            b.HasOne(s => s.IntegrationTrigger)
             .WithMany()
             .HasForeignKey(s => s.IntegrationTriggerId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(s => s.Integration)
             .WithMany()
             .HasForeignKey(s => s.IntegrationId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(s => s.Tenant)
             .WithMany()
             .HasForeignKey(s => s.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkItem>(b =>
        {
            b.ToTable("work_items");
            b.HasKey(w => w.Id);
            b.Property(w => w.Environment).IsRequired().HasMaxLength(50);
            b.Property(w => w.TriggerSource).HasConversion<string>().HasMaxLength(20);
            b.Property(w => w.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(w => w.Payload);
            b.Property(w => w.DeliveryId).HasMaxLength(200);
            b.Property(w => w.AttemptNumber);
            b.Property(w => w.ParentExecutionId);
            b.Property(w => w.RootExecutionId);
            b.Property(w => w.WorkflowRunId);
            b.Property(w => w.WorkflowNodeId);
            b.Property(w => w.IntegrationTriggerId);

            b.HasIndex(w => new { w.TenantId, w.IntegrationId, w.Status });
            b.HasIndex(w => new { w.TenantId, w.IntegrationTriggerId, w.Status });
            b.HasIndex(w => new { w.TenantId, w.Environment, w.Status, w.AvailableAt });
            b.HasIndex(w => new { w.TenantId, w.WorkflowRunId, w.WorkflowNodeId });

            // Idempotent webhook delivery — unique per webhook trigger where a delivery ID is present.
            b.HasIndex(w => new { w.TenantId, w.IntegrationTriggerId, w.DeliveryId })
             .IsUnique()
             .HasFilter("\"IntegrationTriggerId\" IS NOT NULL AND \"DeliveryId\" IS NOT NULL");

            b.HasOne(w => w.IntegrationTrigger)
             .WithMany()
             .HasForeignKey(w => w.IntegrationTriggerId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);

            b.HasOne(w => w.Integration)
             .WithMany()
             .HasForeignKey(w => w.IntegrationId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(w => w.Tenant)
             .WithMany()
             .HasForeignKey(w => w.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TriggerEvent>(b =>
        {
            b.ToTable("trigger_events");
            b.HasKey(e => e.Id);
            b.Property(e => e.AdapterKey).IsRequired().HasMaxLength(100);
            b.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
            b.Property(e => e.EventKey).HasMaxLength(200);
            b.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(30);
            b.Property(e => e.MetadataJson);
            b.Property(e => e.ErrorMessage).HasMaxLength(4000);

            b.HasIndex(e => new { e.TenantId, e.IntegrationId, e.ReceivedAt });
            b.HasIndex(e => new { e.TenantId, e.IntegrationTriggerId, e.ReceivedAt });
            b.HasIndex(e => new { e.TenantId, e.AdapterKey, e.Outcome, e.ReceivedAt });
            b.HasIndex(e => new { e.TenantId, e.Source, e.EventKey });

            b.HasOne(e => e.IntegrationTrigger)
             .WithMany()
             .HasForeignKey(e => e.IntegrationTriggerId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);

            b.HasOne(e => e.Integration)
             .WithMany()
             .HasForeignKey(e => e.IntegrationId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(e => e.Tenant)
             .WithMany()
             .HasForeignKey(e => e.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowDefinition>(b =>
        {
            b.ToTable("workflow_definitions");
            b.HasKey(w => w.Id);
            b.Property(w => w.Name).IsRequired().HasMaxLength(200);
            b.Property(w => w.Slug).IsRequired().HasMaxLength(100);
            b.Property(w => w.Environment).IsRequired().HasMaxLength(50);
            b.Property(w => w.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(w => new { w.TenantId, w.Slug }).IsUnique();
            b.HasOne(w => w.Tenant)
             .WithMany()
             .HasForeignKey(w => w.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowNode>(b =>
        {
            b.ToTable("workflow_nodes");
            b.HasKey(n => n.Id);
            b.Property(n => n.Key).IsRequired().HasMaxLength(100);
            b.Property(n => n.Name).IsRequired().HasMaxLength(200);
            b.HasIndex(n => new { n.TenantId, n.WorkflowDefinitionId, n.Key }).IsUnique();
            b.HasOne(n => n.WorkflowDefinition)
             .WithMany(w => w.Nodes)
             .HasForeignKey(n => n.WorkflowDefinitionId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(n => n.Integration)
             .WithMany()
             .HasForeignKey(n => n.IntegrationId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkflowEdge>(b =>
        {
            b.ToTable("workflow_edges");
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.TenantId, e.WorkflowDefinitionId, e.FromNodeId, e.ToNodeId }).IsUnique();
            b.HasOne(e => e.WorkflowDefinition)
             .WithMany(w => w.Edges)
             .HasForeignKey(e => e.WorkflowDefinitionId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(e => e.FromNode)
             .WithMany()
             .HasForeignKey(e => e.FromNodeId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(e => e.ToNode)
             .WithMany()
             .HasForeignKey(e => e.ToNodeId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowRun>(b =>
        {
            b.ToTable("workflow_runs");
            b.HasKey(r => r.Id);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(r => new { r.TenantId, r.WorkflowDefinitionId, r.StartedAt });
            b.HasOne(r => r.WorkflowDefinition)
             .WithMany()
             .HasForeignKey(r => r.WorkflowDefinitionId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.Tenant)
             .WithMany()
             .HasForeignKey(r => r.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowNodeRun>(b =>
        {
            b.ToTable("workflow_node_runs");
            b.HasKey(r => r.Id);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(r => new { r.TenantId, r.WorkflowRunId, r.WorkflowNodeId }).IsUnique();
            b.HasOne(r => r.WorkflowRun)
             .WithMany(w => w.NodeRuns)
             .HasForeignKey(r => r.WorkflowRunId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.WorkflowNode)
             .WithMany()
             .HasForeignKey(r => r.WorkflowNodeId)
             .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(r => r.WorkItem)
             .WithMany()
             .HasForeignKey(r => r.WorkItemId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);
        });

        modelBuilder.Entity<AuditLogEntry>(b =>
        {
            b.ToTable("audit_log");
            b.HasKey(a => a.Id);
            b.Property(a => a.ActorEmail).IsRequired().HasMaxLength(300);
            b.Property(a => a.Action).HasConversion<string>().HasMaxLength(40);
            b.Property(a => a.TargetType).IsRequired().HasMaxLength(40);
            b.Property(a => a.TargetId).HasMaxLength(200);
            b.Property(a => a.Summary).HasMaxLength(1000);

            b.HasIndex(a => new { a.TenantId, a.OccurredAt });

            b.HasOne(a => a.Tenant)
             .WithMany()
             .HasForeignKey(a => a.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookDelivery>(b =>
        {
            b.ToTable("webhook_deliveries");
            b.HasKey(d => d.Id);
            b.Property(d => d.DeliveryId).HasMaxLength(200);
            b.Property(d => d.Outcome).HasConversion<string>().HasMaxLength(20);
            b.Property(d => d.IntegrationTriggerId);

            b.HasIndex(d => new { d.TenantId, d.IntegrationId, d.ReceivedAt });
            b.HasIndex(d => new { d.TenantId, d.IntegrationTriggerId, d.ReceivedAt });

            b.HasOne(d => d.IntegrationTrigger)
             .WithMany()
             .HasForeignKey(d => d.IntegrationTriggerId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(d => d.Integration)
             .WithMany()
             .HasForeignKey(d => d.IntegrationId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(d => d.Tenant)
             .WithMany()
             .HasForeignKey(d => d.TenantId)
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

        modelBuilder.Entity<AgentHeartbeat>(b =>
        {
            b.ToTable("agent_heartbeats");
            b.HasKey(h => h.Id);
            b.Property(h => h.Environment).IsRequired().HasMaxLength(50);
            b.Property(h => h.Version).HasMaxLength(100);
            b.Property(h => h.Hostname).HasMaxLength(200);

            b.HasIndex(h => new { h.TenantId, h.AgentTokenId }).IsUnique();
            b.HasIndex(h => new { h.TenantId, h.Environment, h.LastSeenAt });

            b.HasOne(h => h.Tenant)
             .WithMany()
             .HasForeignKey(h => h.TenantId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(h => h.AgentToken)
             .WithMany()
             .HasForeignKey(h => h.AgentTokenId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invitation>(b =>
        {
            b.ToTable("invitations");
            b.HasKey(i => i.Id);
            b.Property(i => i.Email).IsRequired().HasMaxLength(300);
            b.Property(i => i.Token).IsRequired().HasMaxLength(100);
            b.Property(i => i.Role).HasConversion<string>();
            b.Property(i => i.ExpiresAt).IsRequired();
            b.Property(i => i.AcceptedAt);

            b.HasIndex(i => i.Token).IsUnique();
            b.HasIndex(i => new { i.TenantId, i.Email });

            b.HasOne(i => i.Tenant)
             .WithMany()
             .HasForeignKey(i => i.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserToken>(b =>
        {
            b.ToTable("user_tokens");
            b.HasKey(t => t.Id);
            b.Property(t => t.Name).IsRequired().HasMaxLength(200);
            b.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);

            b.HasIndex(t => t.TokenHash).IsUnique();
            b.HasIndex(t => new { t.TenantId, t.UserId });

            b.HasOne(t => t.Tenant)
             .WithMany()
             .HasForeignKey(t => t.TenantId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(t => t.User)
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
