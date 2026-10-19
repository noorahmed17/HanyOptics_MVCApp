using HanyOptics.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HanyOptics.DataAccess.Persistence;
public class HanyOpticsDbContext : DbContext
{
    public HanyOpticsDbContext(DbContextOptions<HanyOpticsDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusLog> OrderStatusLogs => Set<OrderStatusLog>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<Frame> Frames => Set<Frame>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HanyOpticsDbContext).Assembly);
    }
}
