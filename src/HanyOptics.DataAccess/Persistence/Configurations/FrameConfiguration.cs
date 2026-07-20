using HanyOptics.Domain.Entities;
using HanyOptics.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HanyOptics.DataAccess.Persistence.Configurations;

public class FrameConfiguration : IEntityTypeConfiguration<Frame>
{
    public void Configure(EntityTypeBuilder<Frame> builder)
    {
        // T5 (AFTER UPDATE) means SQL Server can't use EF's default OUTPUT-clause
        // INSERT/UPDATE - see https://aka.ms/efcore-docs-sqlserver-save-changes-and-output-clause
        builder.ToTable("frames", tb => tb.UseSqlOutputClause(false));
        builder.HasKey(f => f.FrameId);

        builder.Property(f => f.FrameId).HasColumnName("frame_id");
        builder.Property(f => f.BranchId).HasColumnName("branch_id");
        builder.Property(f => f.Barcode).HasColumnName("barcode").HasMaxLength(50).IsRequired();

        builder.Property(f => f.TrackingType)
            .HasColumnName("tracking_type")
            .HasConversion(
                v => v == FrameTrackingType.Bulk ? "bulk" : "individual",
                v => v == "bulk" ? FrameTrackingType.Bulk : FrameTrackingType.Individual)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(f => f.Category)
            .HasColumnName("category")
            .HasConversion(
                v => v == FrameCategory.Sun ? "sun" : "optical",
                v => v == "sun" ? FrameCategory.Sun : FrameCategory.Optical)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(f => f.Brand).HasColumnName("brand").HasMaxLength(100);
        builder.Property(f => f.ModelName).HasColumnName("model_name").HasMaxLength(100);
        builder.Property(f => f.Color).HasColumnName("color").HasMaxLength(50);
        builder.Property(f => f.Size).HasColumnName("size").HasMaxLength(20);
        builder.Property(f => f.CostPrice).HasColumnName("cost_price").HasColumnType("decimal(10,2)");
        builder.Property(f => f.SellPrice).HasColumnName("sell_price").HasColumnType("decimal(10,2)");
        builder.Property(f => f.QtyInitial).HasColumnName("qty_initial");
        builder.Property(f => f.QtyAvailable).HasColumnName("qty_available");

        builder.Property(f => f.Status)
            .HasColumnName("status")
            .HasConversion(
                v => StatusToDb(v),
                v => StatusFromDb(v))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(f => f.SupplierId).HasColumnName("supplier_id");
        builder.Property(f => f.PurchaseInvoiceId).HasColumnName("purchase_invoice_id");
        builder.Property(f => f.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(f => f.Barcode).IsUnique();
    }

    private static string StatusToDb(FrameStatus status) => status switch
    {
        FrameStatus.Available => "available",
        FrameStatus.Reserved => "reserved",
        FrameStatus.Sold => "sold",
        FrameStatus.Damaged => "damaged",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static FrameStatus StatusFromDb(string status) => status switch
    {
        "available" => FrameStatus.Available,
        "reserved" => FrameStatus.Reserved,
        "sold" => FrameStatus.Sold,
        "damaged" => FrameStatus.Damaged,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
