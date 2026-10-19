using HanyOptics.Domain.Entities;
using HanyOptics.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HanyOptics.DataAccess.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        // T1 (AFTER INSERT/UPDATE/DELETE) means SQL Server can't use EF's default
        // OUTPUT-clause INSERT/UPDATE - see https://aka.ms/efcore-docs-sqlserver-save-changes-and-output-clause
        builder.ToTable("payments", tb => tb.UseSqlOutputClause(false));
        builder.HasKey(p => p.PaymentId);

        builder.Property(p => p.PaymentId).HasColumnName("payment_id");
        builder.Property(p => p.OrderId).HasColumnName("order_id");
        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("decimal(10,2)");

        builder.Property(p => p.PaymentType)
            .HasColumnName("payment_type")
            .HasConversion(
                v => TypeToDb(v),
                v => TypeFromDb(v))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(p => p.PaymentMethod)
            .HasColumnName("payment_method")
            .HasConversion(
                v => v == PaymentMethod.Visa ? "visa" : "cash",
                v => v == "visa" ? PaymentMethod.Visa : PaymentMethod.Cash)
            .HasMaxLength(10);

        builder.Property(p => p.PaidAt).HasColumnName("paid_at");
        builder.Property(p => p.ReceivedBy).HasColumnName("received_by");
        builder.Property(p => p.Notes).HasColumnName("notes").HasMaxLength(500);

        builder.HasOne(p => p.Order)
            .WithMany(o => o.Payments)
            .HasForeignKey(p => p.OrderId);
    }

    private static string TypeToDb(PaymentType type) => type switch
    {
        PaymentType.Deposit => "deposit",
        PaymentType.Final => "final",
        PaymentType.Full => "full",
        PaymentType.Refund => "refund",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static PaymentType TypeFromDb(string type) => type switch
    {
        "deposit" => PaymentType.Deposit,
        "final" => PaymentType.Final,
        "full" => PaymentType.Full,
        "refund" => PaymentType.Refund,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
