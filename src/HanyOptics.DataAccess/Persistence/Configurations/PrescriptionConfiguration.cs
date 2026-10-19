using HanyOptics.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HanyOptics.DataAccess.Persistence.Configurations;

public class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> builder)
    {
        builder.ToTable("prescriptions");
        builder.HasKey(p => p.PrescriptionId);

        builder.Property(p => p.PrescriptionId).HasColumnName("prescription_id");
        builder.Property(p => p.ItemId).HasColumnName("item_id");

        builder.Property(p => p.RightSphere).HasColumnName("right_sphere").HasColumnType("decimal(5,2)");
        builder.Property(p => p.RightCylinder).HasColumnName("right_cylinder").HasColumnType("decimal(5,2)");
        builder.Property(p => p.RightAxis).HasColumnName("right_axis");

        builder.Property(p => p.LeftSphere).HasColumnName("left_sphere").HasColumnType("decimal(5,2)");
        builder.Property(p => p.LeftCylinder).HasColumnName("left_cylinder").HasColumnType("decimal(5,2)");
        builder.Property(p => p.LeftAxis).HasColumnName("left_axis");

        builder.Property(p => p.Pd).HasColumnName("pd").HasColumnType("decimal(5,1)");
        builder.Property(p => p.AddPower).HasColumnName("add_power").HasColumnType("decimal(5,2)");
        builder.Property(p => p.Notes).HasColumnName("notes").HasMaxLength(500);

        builder.HasIndex(p => p.ItemId).IsUnique();

        builder.HasOne(p => p.OrderItem)
            .WithOne(oi => oi.Prescription)
            .HasForeignKey<Prescription>(p => p.ItemId);
    }
}
