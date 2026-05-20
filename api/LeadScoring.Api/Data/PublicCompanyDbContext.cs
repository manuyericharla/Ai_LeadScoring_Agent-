using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Data;

/// <summary>
/// Company-wide lead data in <c>public</c> schema (not per-tenant schema).
/// Separate DbContext type so EF does not reuse the tenant model cache.
/// </summary>
public class PublicCompanyDbContext(DbContextOptions<PublicCompanyDbContext> options) : DbContext(options)
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadEvent> Events => Set<LeadEvent>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lead>(entity =>
        {
            entity.ToTable("Leads", "public");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CompanyId).HasColumnName("CompanyId");
            entity.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<LeadEvent>(entity =>
        {
            entity.ToTable("Events", "public");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.LeadId, x.Type, x.TimestampUtc });
            entity
                .HasOne(x => x.Lead)
                .WithMany()
                .HasForeignKey(x => x.LeadId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EmailTemplate>(entity =>
        {
            entity.ToTable("EmailTemplates", "public");
            entity.HasKey(x => x.TemplateId);
        });
    }
}
