using System.Data.Common;

namespace Producer
{
  public interface IUnitOfWork : IAsyncDisposable
  {
    DbTransaction Transaction { get; }
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
  }
}
