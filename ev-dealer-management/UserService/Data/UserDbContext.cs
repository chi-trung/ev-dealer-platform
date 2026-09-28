using Microsoft.EntityFrameworkCore;
using UserService.Models;

namespace UserService.Data;

// EF Core DbContext and entities
public class UserDbContext : DbContext
{
    public UserDbContext(DbContextOptions<UserDbContext> options) : base(options) { }
    public DbSet<User> Users => Set<User>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    // NO DbSet<Dealer>: VehicleService owns the Dealers table (issue #121).
    // See the comment on the Dealer class -- two contexts with different
    // creation strategies (Migrate vs EnsureCreated) cannot share it.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(eb =>
        {
            eb.HasKey(u => u.Id);
            eb.HasIndex(u => u.Username).IsUnique();
            // Issue #150: this was a plain index, so the database did not
            // stop two accounts sharing one email. Every creation path checks
            // for a duplicate with AnyAsync first, but that is a read followed
            // by a later write with a gap between them — two concurrent
            // requests both pass the check and both insert. Username was
            // already unique and would have caught that race; Email has to be
            // too, because CustomerService derives Username FROM Email, which
            // makes the email the identifier a customer is created under.
            // The check still stays in the service methods: the unique index
            // is the guarantee, the check is the message the user reads.
            eb.HasIndex(u => u.Email).IsUnique();
            // No HasOne<Dealer>() FK: that would emit a constraint against a
            // table this context no longer owns. DealerId is validated in
            // application code (DealerIdValidator) instead.
        });

        modelBuilder.Entity<PasswordResetToken>(eb =>
        {
            eb.HasKey(t => t.Id);
            eb.HasIndex(t => t.Token);
            eb.HasIndex(t => t.UserId);
            eb.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
