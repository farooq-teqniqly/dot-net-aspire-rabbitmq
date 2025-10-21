using System.Collections.Concurrent;

namespace Producer
{
  internal sealed class PublisherConfirmationTracker
  {
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<bool>> _pendingConfirmations;
    private readonly ILogger<PublisherConfirmationTracker> _logger;

    public PublisherConfirmationTracker(ILogger<PublisherConfirmationTracker> logger)
    {
      ArgumentNullException.ThrowIfNull(logger);

      _pendingConfirmations = new ConcurrentDictionary<ulong, TaskCompletionSource<bool>>();
      _logger = logger;
    }

    public Task<bool> TrackPublishAsync(ulong sequenceNumber)
    {
      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

      if (!_pendingConfirmations.TryAdd(sequenceNumber, tcs))
      {
        _logger.LogWarning(
          "Sequence number {SequenceNumber} already being tracked",
          sequenceNumber
        );
        throw new InvalidOperationException(
          $"Sequence number {sequenceNumber} is already being tracked"
        );
      }

      _logger.LogDebug("Started tracking sequence number {SequenceNumber}", sequenceNumber);

      return tcs.Task;
    }

    public void HandleAck(ulong deliveryTag, bool multiple)
    {
      if (multiple)
      {
        _logger.LogDebug(
          "Received multiple ACK for delivery tags up to and including {DeliveryTag}",
          deliveryTag
        );

        var toRemove = _pendingConfirmations.Keys.Where(k => k <= deliveryTag).ToList();

        foreach (var tag in toRemove)
        {
          if (!_pendingConfirmations.TryRemove(tag, out var tcs))
          {
            continue;
          }

          tcs.TrySetResult(true);
          _logger.LogDebug("ACK confirmed for sequence number {SequenceNumber}", tag);
        }
      }
      else
      {
        if (_pendingConfirmations.TryRemove(deliveryTag, out var tcs))
        {
          tcs.TrySetResult(true);
          _logger.LogDebug("ACK confirmed for sequence number {SequenceNumber}", deliveryTag);
        }
        else
        {
          _logger.LogWarning(
            "Received ACK for unknown sequence number {SequenceNumber}",
            deliveryTag
          );
        }
      }
    }

    public void HandleNack(ulong deliveryTag, bool multiple)
    {
      if (multiple)
      {
        _logger.LogWarning(
          "Received multiple NACK for delivery tags up to and including {DeliveryTag}",
          deliveryTag
        );

        var toRemove = _pendingConfirmations.Keys.Where(k => k <= deliveryTag).ToList();

        foreach (var tag in toRemove)
        {
          if (!_pendingConfirmations.TryRemove(tag, out var tcs))
          {
            continue;
          }

          tcs.TrySetResult(false);
          _logger.LogWarning("NACK received for sequence number {SequenceNumber}", tag);
        }
      }
      else
      {
        if (_pendingConfirmations.TryRemove(deliveryTag, out var tcs))
        {
          tcs.TrySetResult(false);
          _logger.LogWarning("NACK received for sequence number {SequenceNumber}", deliveryTag);
        }
        else
        {
          _logger.LogWarning(
            "Received NACK for unknown sequence number {SequenceNumber}",
            deliveryTag
          );
        }
      }
    }

    public void Timeout(ulong sequenceNumber)
    {
      if (!_pendingConfirmations.TryRemove(sequenceNumber, out var tcs))
      {
        return;
      }

      tcs.TrySetException(
        new TimeoutException($"Confirmation timeout for sequence number {sequenceNumber}")
      );
      _logger.LogError("Confirmation timeout for sequence number {SequenceNumber}", sequenceNumber);
    }

    public int PendingCount => _pendingConfirmations.Count;
  }
}
