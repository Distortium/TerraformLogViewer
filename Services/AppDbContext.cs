using Microsoft.EntityFrameworkCore;
using TerraformLogViewer.Models;

namespace TerraformLogViewer.Services
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<LogFile> LogFiles { get; set; }
        public DbSet<LogEntry> LogEntries { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(e => e.Email).IsUnique();
                entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
                entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(255);
            });

            // LogFile configuration
            modelBuilder.Entity<LogFile>(entity =>
            {
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.UploadedAt);

                entity.HasMany(e => e.LogEntries)
                    .WithOne(e => e.LogFile)
                    .HasForeignKey(e => e.LogFileId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // LogEntry configuration
            modelBuilder.Entity<LogEntry>(entity =>
            {
                entity.HasIndex(e => e.LogFileId);
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.Level);
                entity.HasIndex(e => e.Phase);
                entity.HasIndex(e => e.TfReqId);
                entity.HasIndex(e => e.TfResourceType);
                entity.HasIndex(e => e.Status);

                entity.Property(e => e.HttpReqBody).HasColumnType("jsonb");
                entity.Property(e => e.HttpResBody).HasColumnType("jsonb");
            });
        }
    }
}