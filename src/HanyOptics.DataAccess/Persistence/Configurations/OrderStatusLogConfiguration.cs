using HanyOptics.Domain.Entities;
using HanyOptics.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HanyOptics.DataAccess.Persistence.Configurations;

public class OrderStatusLogConfiguration : IEntityTypeConfiguration<OrderStatusLog>
{
    public void Configure(EntityTypeBuilder<OrderStatusLog> builder)
    {
        builder.ToTable("order_status_log");
        builder.HasKey(l => l.LogId);

        builder.Property(l => l.LogId).HasColumnName("log_id");
        builder.Property(l => l.OrderId).HasColumnName("order_id");

        builder.Property(l => l.OldStatus)
            .HasColumnName("old_status")
            .HasConversion(
                v => v.HasValue ? StatusToDb(v.Value) : null,
                v => v != null ? StatusFromDb(v) : null)
            .HasMaxLength(15);

        builder.Property(l => l.NewStatus)
            .HasColumnName("new_status")
            .HasConversion(
                v => StatusToDb(v),
                v => StatusFromDb(v))
            .HasMaxLength(15)
            .IsRequired();

        builder.Property(l => l.ChangedAt).HasColumnName("changed_at");
        builder.Property(l => l.ChangedBy).HasColumnName("changed_by");
        builder.Property(l => l.Notes).HasColumnName("notes").HasMaxLength(500);

        builder.HasOne(l => l.Order)
            .WithMany(o => o.StatusLogs)
            .HasForeignKey(l => l.OrderId);
    }

    private static string StatusToDb(OrderStatus status) => status switch
    {
        OrderStatus.Sold => "sold",
        OrderStatus.Ready => "ready",
        OrderStatus.Delivered => "delivered",
        OrderStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static OrderStatus StatusFromDb(string status) => status switch
    {
        "sold" => OrderStatus.Sold,
        "ready" => OrderStatus.Ready,
        "delivered" => OrderStatus.Delivered,
        "cancelled" => OrderStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
