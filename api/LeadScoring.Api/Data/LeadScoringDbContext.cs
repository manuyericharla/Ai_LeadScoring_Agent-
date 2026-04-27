using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Data;

public class LeadScoringDbContext(DbContextOptions<LeadScoringDbContext> options) : DbContext(options)
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadEvent> Events => Set<LeadEvent>();
    public DbSet<CompanyProductConfig> CompanyProductConfigs => Set<CompanyProductConfig>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lead>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<LeadEvent>().HasIndex(x => new { x.LeadId, x.Type, x.TimestampUtc });
        modelBuilder.Entity<CompanyProductConfig>().HasIndex(x => new { x.CompanyName, x.ProductName, x.ProductId }).IsUnique();
        modelBuilder.Entity<EmailTemplate>().HasKey(x => x.TemplateId);
        modelBuilder.Entity<EmailTemplate>()
            .HasIndex(x => x.Stage)
            .HasFilter("\"IsActive\" = true")
            .IsUnique();
    }
}
