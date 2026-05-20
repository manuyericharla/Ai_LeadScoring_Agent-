using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Data;

public class MasterDbContext(DbContextOptions<MasterDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>().HasIndex(x => x.CompanyName).IsUnique();
        modelBuilder.Entity<Tenant>().HasIndex(x => x.DatabaseName).IsUnique();
        modelBuilder.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<AppUser>()
            .HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
