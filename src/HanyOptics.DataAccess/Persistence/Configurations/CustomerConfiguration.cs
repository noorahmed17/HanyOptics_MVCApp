using HanyOptics.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HanyOptics.DataAccess.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.CustomerId);

        builder.Property(c => c.CustomerId).HasColumnName("customer_id");
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(100);
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.Notes).HasColumnName("notes").HasMaxLength(500);
    }
}
