using HanyOptics.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HanyOptics.DataAccess.Persistence.Configurations;

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("doctors");
        builder.HasKey(d => d.DoctorId);

        builder.Property(d => d.DoctorId).HasColumnName("doctor_id");
        builder.Property(d => d.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(d => d.Clinic).HasColumnName("clinic").HasMaxLength(150);
        builder.Property(d => d.Phone).HasColumnName("phone").HasMaxLength(20);
    }
}
