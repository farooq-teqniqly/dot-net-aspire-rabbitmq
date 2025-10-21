using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Producer
{
  internal sealed class UnitOfWork : IUnitOfWork
  {
    private readonly SqlConnection _sqlConnection;
    private bool _disposed;
    private DbTransaction? _transaction;

    public UnitOfWork(SqlConnection sqlConnection)
    {
      ArgumentNullException.ThrowIfNull(sqlConnection);
      _sqlConnection = sqlConnection;
    }

    public DbTransaction Transaction =>
      _transaction ?? throw new InvalidOperationException("Transaction has not been started.");

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
      if (_transaction != null)
      {
        throw new InvalidOperationException("Transaction has already been started.");
      }

      if (_sqlConnection.State != System.Data.ConnectionState.Open)
      {
        await _sqlConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
      }

      _transaction = await _sqlConnection
        .BeginTransactionAsync(cancellationToken)
        .ConfigureAwait(false);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
      if (_transaction == null)
      {
        throw new InvalidOperationException("Transaction has not been started.");
      }

      await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
      if (_disposed)
      {
        return;
      }

      if (_transaction != null)
      {
        await _transaction.DisposeAsync().ConfigureAwait(false);
      }

      _disposed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
      if (_transaction == null)
      {
        throw new InvalidOperationException("Transaction has not been started.");
      }

      await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
    }
  }
}
