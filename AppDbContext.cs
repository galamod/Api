using Microsoft.EntityFrameworkCore;
using Api.Models;

namespace Api
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<License> Licenses { get; set; }
        public DbSet<Payment> Payments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().ToTable("Users");
            modelBuilder.Entity<License>().ToTable("Licenses");
            modelBuilder.Entity<Payment>().ToTable("Payments");

            modelBuilder.Entity<License>()
               .HasOne(l => l.User)
               .WithMany(u => u.Licenses)
               .HasForeignKey(l => l.UserId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<License>()
                .HasIndex(l => l.Key)
                .IsUnique();

            // Payment relationships
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.OrderId)
                .IsUnique();

            base.OnModelCreating(modelBuilder);
        }
    }
}