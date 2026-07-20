using HanyOptics.Domain.Entities;
using HanyOptics.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HanyOptics.DataAccess.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        // T4 (AFTER UPDATE) means SQL Server can't use EF's default OUTPUT-clause
        // INSERT/UPDATE - see https://aka.ms/efcore-docs-sqlserver-save-changes-and-output-clause
        builder.ToTable("orders", tb => tb.UseSqlOutputClause(false));
        builder.HasKey(o => o.OrderId);

        builder.Property(o => o.OrderId).HasColumnName("order_id");
        builder.Property(o => o.BranchId).HasColumnName("branch_id");
        builder.Property(o => o.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(50).IsRequired();
        builder.Property(o => o.CustomerId).HasColumnName("customer_id");
        builder.Property(o => o.CustomerName).HasColumnName("customer_name").HasMaxLength(100);
        builder.Property(o => o.CreatedBy).HasColumnName("created_by");
        builder.Property(o => o.DoctorId).HasColumnName("doctor_id");
        builder.Property(o => o.OrderDate).HasColumnName("order_date");

        builder.Property(o => o.DeliveryType)
            .HasColumnName("delivery_type")
            .HasConversion(
                v => v == DeliveryType.Immediate ? "immediate" : "normal",
                v => v == "immediate" ? DeliveryType.Immediate : DeliveryType.Normal)
            .HasMaxLength(10);

        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasConversion(
                v => StatusToDb(v),
                v => StatusFromDb(v))
            .HasMaxLength(15);

        builder.Property(o => o.TotalAmount).HasColumnName("total_amount").HasColumnType("decimal(10,2)");
        builder.Property(o => o.PaidAmount).HasColumnName("paid_amount").HasColumnType("decimal(10,2)");

        // Persisted computed column: total_amount - paid_amount. Never written by EF.
        builder.Property(o => o.RemainingAmount)
            .HasColumnName("remaining_amount")
            .HasColumnType("decimal(10,2)")
            .ValueGeneratedOnAddOrUpdate();
        builder.Metadata.FindProperty(nameof(Order.RemainingAmount))!
            .SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        builder.Metadata.FindProperty(nameof(Order.RemainingAmount))!
            .SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);

        builder.Property(o => o.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(o => o.DeliveredAt).HasColumnName("delivered_at");

        builder.HasMany(o => o.OrderItems)
            .WithOne(oi => oi.Order)
            .HasForeignKey(oi => oi.OrderId);
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
