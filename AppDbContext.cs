using Microsoft.EntityFrameworkCore;

namespace Api
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<License> Licenses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().ToTable("Users");
            modelBuilder.Entity<License>().ToTable("Licenses");

            modelBuilder.Entity<License>()
               .HasOne(l => l.User)
               .WithMany(u => u.Licenses)
               .HasForeignKey(l => l.UserId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<License>()
                .HasIndex(l => l.Key)
                .IsUnique();

            base.OnModelCreating(modelBuilder);
        }
    }
}