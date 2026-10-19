using HanyOptics.Domain.Entities;
using HanyOptics.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HanyOptics.DataAccess.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        // T2/T3 (AFTER INSERT/UPDATE/DELETE) mean SQL Server can't use EF's default
        // OUTPUT-clause INSERT/UPDATE - see https://aka.ms/efcore-docs-sqlserver-save-changes-and-output-clause
        builder.ToTable("order_items", tb => tb.UseSqlOutputClause(false));
        builder.HasKey(oi => oi.ItemId);

        builder.Property(oi => oi.ItemId).HasColumnName("item_id");
        builder.Property(oi => oi.OrderId).HasColumnName("order_id");

        builder.Property(oi => oi.ItemType)
            .HasColumnName("item_type")
            .HasConversion(
                v => TypeToDb(v),
                v => TypeFromDb(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(oi => oi.FrameId).HasColumnName("frame_id");
        builder.Property(oi => oi.FrameAgreedPrice).HasColumnName("frame_agreed_price").HasColumnType("decimal(10,2)");
        builder.Property(oi => oi.ExternalFrameNotes).HasColumnName("external_frame_notes").HasMaxLength(200);

        builder.Property(oi => oi.LensDescription).HasColumnName("lens_description").HasMaxLength(200);
        builder.Property(oi => oi.RightLensStockId).HasColumnName("right_lens_stock_id");
        builder.Property(oi => oi.LeftLensStockId).HasColumnName("left_lens_stock_id");
        builder.Property(oi => oi.LensSellPrice).HasColumnName("lens_sell_price").HasColumnType("decimal(10,2)");

        builder.Property(oi => oi.LensCostPrice).HasColumnName("lens_cost_price").HasColumnType("decimal(10,2)");
        builder.Property(oi => oi.DiscountReason).HasColumnName("discount_reason").HasMaxLength(200);
        builder.Property(oi => oi.WasFrameSwapped).HasColumnName("was_frame_swapped");

        builder.Property(oi => oi.Status)
            .HasColumnName("status")
            .HasConversion(
                v => v == OrderItemStatus.Cancelled ? "cancelled" : "active",
                v => v == "cancelled" ? OrderItemStatus.Cancelled : OrderItemStatus.Active)
            .HasMaxLength(10);

        builder.Property(oi => oi.CompensationFrameId).HasColumnName("compensation_frame_id");
        builder.Property(oi => oi.CompensationNotes).HasColumnName("compensation_notes").HasMaxLength(200);

        // Persisted computed column: frame_agreed_price + lens_sell_price. Never written by EF.
        builder.Property(oi => oi.ItemTotal)
            .HasColumnName("item_total")
            .HasColumnType("decimal(10,2)")
            .ValueGeneratedOnAddOrUpdate();
        builder.Metadata.FindProperty(nameof(OrderItem.ItemTotal))!
            .SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        builder.Metadata.FindProperty(nameof(OrderItem.ItemTotal))!
            .SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
    }

    private static string TypeToDb(OrderItemType type) => type switch
    {
        OrderItemType.FrameLenses => "frame_lenses",
        OrderItemType.FrameOnly => "frame_only",
        OrderItemType.LensesReplace => "lenses_replace",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static OrderItemType TypeFromDb(string type) => type switch
    {
        "frame_lenses" => OrderItemType.FrameLenses,
        "frame_only" => OrderItemType.FrameOnly,
        "lenses_replace" => OrderItemType.LensesReplace,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
