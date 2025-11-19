using Microsoft.EntityFrameworkCore;
using Nexa.DrmLicenseServer.Data.Entities;

namespace Nexa.DrmLicenseServer.Data;

public class NexaDbContext : DbContext
{
    public NexaDbContext(DbContextOptions<NexaDbContext> options) : base(options)
    {
    }

    public DbSet<UserEntity> Users { get; set; }
    public DbSet<RefreshTokenEntity> RefreshTokens { get; set; }
    public DbSet<IssuedLicenseEntity> IssuedLicenses { get; set; }
    public DbSet<UserDeviceKeyEntity> UserDeviceKeys { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Konfiguracja UserEntity
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // Konfiguracja RefreshTokenEntity
        modelBuilder.Entity<RefreshTokenEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Konfiguracja IssuedLicenseEntity
        modelBuilder.Entity<IssuedLicenseEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.ContentId, e.Quality });
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => new { e.UserId, e.LastHeartbeat });
            entity.Property(e => e.IssuedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithMany(u => u.IssuedLicenses)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Konfiguracja UserDeviceKeyEntity
        modelBuilder.Entity<UserDeviceKeyEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.DeviceId }).IsUnique();
            entity.HasIndex(e => e.DeviceId);
            entity.Property(e => e.RegisteredAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithMany(u => u.DeviceKeys)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
