using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Data;

public class LeadScoringDbContext(DbContextOptions<LeadScoringDbContext> options) : DbContext(options)
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadEvent> Events => Set<LeadEvent>();
    public DbSet<CompanyProductConfig> CompanyProductConfigs => Set<CompanyProductConfig>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<BatchConfig> BatchConfigs => Set<BatchConfig>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<BatchLead> BatchLeads => Set<BatchLead>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lead>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<LeadEvent>().HasIndex(x => new { x.LeadId, x.Type, x.TimestampUtc });
        modelBuilder.Entity<CompanyProductConfig>().HasIndex(x => new { x.CompanyName, x.ProductName, x.ProductId }).IsUnique();
        modelBuilder.Entity<EmailTemplate>().HasKey(x => x.TemplateId);
        modelBuilder.Entity<EmailTemplate>()
            .HasIndex(x => new { x.Stage, x.ProductId, x.IsFollowUp })
            .HasFilter("\"IsActive\" = true")
            .IsUnique();

        modelBuilder.Entity<BatchConfig>().HasKey(x => x.ConfigId);
        modelBuilder.Entity<BatchConfig>().HasIndex(x => new { x.ProductId, x.Stage, x.IsActive });

        modelBuilder.Entity<Batch>().HasKey(x => x.BatchId);
        modelBuilder.Entity<Batch>().HasIndex(x => x.ProductId);
        modelBuilder.Entity<Batch>().HasIndex(x => new { x.ProductId, x.BatchType, x.Status, x.EndTime });

        modelBuilder.Entity<BatchLead>().HasKey(x => x.BatchLeadId);
        modelBuilder.Entity<BatchLead>().HasIndex(x => x.BatchId);
        modelBuilder.Entity<BatchLead>().HasIndex(x => x.LeadId);
        modelBuilder.Entity<BatchLead>()
            .HasOne(x => x.Batch)
            .WithMany(x => x.BatchLeads)
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BatchLead>()
            .HasOne(x => x.Lead)
            .WithMany(x => x.BatchLeads)
            .HasForeignKey(x => x.LeadId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
