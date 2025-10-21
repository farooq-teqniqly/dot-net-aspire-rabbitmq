using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Producer.Entities;

namespace Producer.Database.Configurations
{
  internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
  {
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
      builder.HasKey(m => m.Id);
      builder.Property(m => m.Id).ValueGeneratedNever();

      builder.Property(m => m.Content).IsRequired().HasColumnType("nvarchar(max)");

      builder.ToTable(t =>
        t.HasCheckConstraint("ck_outbox_messages_content_is_json", "ISJSON([content]) = 1")
      );

      builder.Property(m => m.OccurredOnUtc).IsRequired();

      builder
        .HasIndex(m => new { m.OccurredOnUtc, m.ProcessedOnUtc })
        .HasFilter("[processed_on_utc] IS NULL");
    }
  }
}
