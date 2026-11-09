using Microsoft.EntityFrameworkCore;
using SafeShare.Models;

namespace SafeShare.Data;

public class SecretDbContext(DbContextOptions<SecretDbContext> options) : DbContext(options)
{
    public DbSet<Secret> Secrets => Set<Secret>();
    public DbSet<SecretRequest> Requests => Set<SecretRequest>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Secret>().HasIndex(s => s.ExpiresUtc);
        modelBuilder.Entity<Artifact>().HasIndex(a => a.ExpiresUtc);

        var request = modelBuilder.Entity<SecretRequest>();
        request.HasIndex(r => r.ExpiresUtc);
        request.Ignore(r => r.IsFulfilled);
    }
}
