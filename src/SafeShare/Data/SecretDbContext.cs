using Microsoft.EntityFrameworkCore;
using SafeShare.Models;

namespace SafeShare.Data;

public class SecretDbContext(DbContextOptions<SecretDbContext> options) : DbContext(options)
{
    public DbSet<Secret> Secrets => Set<Secret>();
    public DbSet<SecretRequest> Requests => Set<SecretRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Secret>().HasIndex(s => s.ExpiresUtc);

        var request = modelBuilder.Entity<SecretRequest>();
        request.HasIndex(r => r.ExpiresUtc);
        request.Ignore(r => r.IsFulfilled);
    }
}
