using Microsoft.EntityFrameworkCore;

namespace GuardrailApi.Audit;

public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<AuditViolationEntity> AuditViolations => Set<AuditViolationEntity>();
    public DbSet<ComplianceSnapshotEntity> ComplianceSnapshots => Set<ComplianceSnapshotEntity>();
    public DbSet<AuditArchiveEntity> AuditArchives => Set<AuditArchiveEntity>();
    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLogEntity>(e =>
        {
            e.ToTable("audit_log");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.AuditId).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Timestamp });
            e.HasIndex(x => x.RecordHash);
            e.Property(x => x.Frameworks).HasColumnType("text[]");
            e.Property(x => x.Metadata).HasColumnType("jsonb");
        });

        modelBuilder.Entity<AuditViolationEntity>(e =>
        {
            e.ToTable("audit_violations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.AuditLogId);
        });

        modelBuilder.Entity<ComplianceSnapshotEntity>(e =>
        {
            e.ToTable("compliance_snapshots");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.SnapshotDate });
            e.Property(x => x.ControlsCovered).HasColumnType("text[]");
            e.Property(x => x.ControlsGap).HasColumnType("text[]");
        });

        modelBuilder.Entity<AuditArchiveEntity>(e =>
        {
            e.ToTable("audit_archive_log");
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<TenantEntity>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
        });
    }
}

public class TenantEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuditLogEntity
{
    public long Id { get; set; }
    public required string AuditId { get; set; }
    public Guid TenantId { get; set; }
    public DateTime Timestamp { get; set; }
    public required string Action { get; set; }
    public required string ResourceType { get; set; }
    public required string Source { get; set; }
    public required string Decision { get; set; }
    public decimal Confidence { get; set; }
    public int ViolationCount { get; set; }
    public string? PolicyVersion { get; set; }
    public string[]? Frameworks { get; set; }
    public int? PipelineMs { get; set; }
    public required string InputHash { get; set; }
    public string? InputBlobRef { get; set; }
    public string? OutputBlobRef { get; set; }
    public string? RemediationAction { get; set; }
    public int RedactionCount { get; set; }
    public bool HumanReviewed { get; set; }
    public string? ReviewerId { get; set; }
    public string? ReviewDecision { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? PreviousHash { get; set; }
    public required string RecordHash { get; set; }
    public string? Signature { get; set; }
    public string? Metadata { get; set; }
}

public class AuditViolationEntity
{
    public long Id { get; set; }
    public long AuditLogId { get; set; }
    public required string RuleId { get; set; }
    public required string ViolationType { get; set; }
    public required string Severity { get; set; }
    public required string Message { get; set; }
    public string? Remediation { get; set; }
    public decimal? Confidence { get; set; }
    public string? Stage { get; set; }
    public string? ControlId { get; set; }
    public string? EvidenceHash { get; set; }
}

public class ComplianceSnapshotEntity
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Framework { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public int TotalChecks { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public decimal? CoveragePct { get; set; }
    public string[]? ControlsCovered { get; set; }
    public string[]? ControlsGap { get; set; }
    public string? ReportBlobRef { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuditArchiveEntity
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public long ArchivedFrom { get; set; }
    public long ArchivedTo { get; set; }
    public int RecordCount { get; set; }
    public required string ArchiveBlobRef { get; set; }
    public required string ArchiveHash { get; set; }
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
}
