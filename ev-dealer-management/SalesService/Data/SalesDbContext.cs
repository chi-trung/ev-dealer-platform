using Microsoft.EntityFrameworkCore;
using SalesService.Models;

namespace SalesService.Data
{
    public class SalesDbContext : DbContext
    {
        public SalesDbContext(DbContextOptions<SalesDbContext> options) : base(options)
        {
        }

        public DbSet<Quote> Quotes { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<Contract> Contracts { get; set; }
        
        // Keep other DbSets to prevent breaking other parts of the application
        public DbSet<Promotion> Promotions { get; set; }
        public DbSet<Delivery> Deliveries { get; set; }
        public DbSet<Payment> Payments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Configurations for the refactored models
            modelBuilder.Entity<Quote>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            });

            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasKey(e => e.OrderId);
                entity.HasIndex(e => e.OrderNumber).IsUnique();
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);

                // Define the one-to-one relationship with Contract
                entity.HasOne(o => o.Contract)
                      .WithOne(c => c.Order)
                      .HasForeignKey<Contract>(c => c.OrderId);
            });

            modelBuilder.Entity<Contract>(entity =>
            {
                entity.HasKey(e => e.ContractId);
                entity.HasIndex(e => e.ContractNumber).IsUnique();
                entity.Property(e => e.Status).IsRequired().HasMaxLength(30);
                entity.Property(e => e.PaymentStatus).IsRequired().HasMaxLength(30);
            });

            // Issue #37: Payment.OrderId is now the real int FK. Previously the
            // model carried a Guid OrderId against an int-PK Order, so EF built
            // the relationship on a nullable shadow column (OrderId1) and the
            // declared Guid "FK" never constrained anything — every payment was
            // link-orphaned by construction. Explicit mapping removes the
            // shadow column and makes OrderId itself the constraint.
            modelBuilder.Entity<Payment>(entity =>
            {
                entity.HasKey(e => e.PaymentId);
                entity.HasOne(p => p.Order)
                      .WithMany()
                      .HasForeignKey(p => p.OrderId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
