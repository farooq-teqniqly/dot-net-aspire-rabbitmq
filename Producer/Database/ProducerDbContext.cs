using Microsoft.EntityFrameworkCore;
using Producer.Entities;

namespace Producer.Database
{
  internal sealed class ProducerDbContext : DbContext
  {
    public ProducerDbContext(DbContextOptions<ProducerDbContext> options)
      : base(options) { }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      ArgumentNullException.ThrowIfNull(modelBuilder);

      modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProducerDbContext).Assembly);
    }
  }
}
