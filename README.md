# .NET Aspire RabbitMQ with Transactional Outbox Pattern

A demonstration project showcasing reliable message publishing using .NET Aspire, RabbitMQ, and the Transactional Outbox pattern with publisher confirmations.

## 🎯 Overview

This project implements a weather forecast API that reliably publishes messages to RabbitMQ using:

- **Transactional Outbox Pattern**: Ensures messages are never lost by storing them in a database first
- **Publisher Confirmations**: Waits for RabbitMQ broker acknowledgment before marking messages as sent
- **Unit of Work Pattern**: Manages database transactions consistently
- **.NET Aspire**: Orchestrates distributed application with service discovery

## 🏗️ Architecture

```text
┌─────────────┐      ┌──────────────┐      ┌─────────────┐      ┌──────────────┐
│   Client    │─────▶│   Producer   │─────▶│  SQL Server │      │   RabbitMQ   │
│  (HTTP)     │      │   Web API    │      │   Outbox    │      │    Broker    │
└─────────────┘      └──────────────┘      └─────────────┘      └──────────────┘
                            │                      │                     │
                            │                      │                     │
                            ▼                      ▼                     ▼
                     ┌──────────────┐      ┌─────────────┐      ┌──────────────┐
                     │  Background  │─────▶│   Outbox    │─────▶│  Publisher   │
                     │   Service    │      │  Messages   │      │ Confirmation │
                     └──────────────┘      └─────────────┘      └──────────────┘
                                                                        │
                                                                        ▼
                                                                 ┌──────────────┐
                                                                 │   Consumer   │
                                                                 │   Service    │
                                                                 └──────────────┘
```

### Message Flow

1. **HTTP Request** → Controller creates weather forecast
2. **Database Transaction** → Forecast saved to outbox table
3. **Background Worker** → Polls outbox every 10 seconds (configurable)
4. **Get Sequence Number** → Obtains next RabbitMQ publish sequence
5. **Publish Message** → Sends to RabbitMQ with mandatory flag
6. **Await Confirmation** → Waits up to 5 seconds for ACK/NACK (configurable)
7. **Mark Processed** → Only after broker confirms receipt
8. **Consumer Receives** → Processes the message

## 🚀 Quick Start

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
- [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling)
- Visual Studio 2022 17.9+ or VS Code with C# Dev Kit

### Installation

1. **Clone the repository**

   ```bash
   git clone https://github.com/farooq-teqniqly/dot-net-aspire-rabbitmq.git
   cd dot-net-aspire-rabbitmq
   ```

2. **Install .NET Aspire workload** (if not already installed)

   ```bash
   dotnet workload install aspire
   ```

3. **Start the application**

   ```bash
   dotnet run --project DotNetAspireRabbitMq.AppHost
   ```

4. **Access the services**
   - Aspire Dashboard: `http://localhost:15888`
   - Producer API: `http://localhost:5000/weatherforecast`
   - RabbitMQ Management: `http://localhost:15672` (guest/guest)

## 📁 Project Structure

```text
├── Producer/                           # Weather forecast API
│   ├── Controllers/
│   │   └── WeatherForecastController.cs    # REST endpoint
│   ├── Database/
│   │   ├── ProducerDbContext.cs            # EF Core context
│   │   └── Migrations/                     # Database migrations
│   ├── Entities/
│   │   └── OutboxMessage.cs                # Outbox table entity
│   ├── WeatherPublisher.cs                 # Background service
│   ├── PublisherConfirmationTracker.cs     # Tracks ACKs/NACKs
│   ├── OutboxRepository.cs                 # Outbox data access
│   ├── UnitOfWork.cs                       # Transaction management
│   └── Program.cs                          # DI configuration
│
├── Consumer/                           # Message consumer service
│   ├── WeatherConsumer.cs                  # RabbitMQ consumer
│   └── Program.cs
│
├── DotNetAspireRabbitMq.AppHost/      # Orchestration
│   └── AppHost.cs                          # Service configuration
│
└── DotNetAspireRabbitMq.ServiceDefaults/  # Shared settings
    └── AspireExtensions.cs
```

## 🔑 Key Components

### WeatherPublisher (Background Service)

Processes outbox messages and publishes to RabbitMQ with confirmations.

**Key Features:**

- Polls outbox every 10 seconds (configurable)
- Processes up to 100 messages per batch (configurable)
- Tracks publisher confirmations via sequence numbers
- Handles ACK, NACK, and timeout scenarios
- Marks messages as processed only after broker confirms

**Flow:**

```csharp
1. GetUnprocessedMessagesAsync()        // Query outbox
2. GetNextPublishSequenceNumberAsync()  // Get RabbitMQ sequence
3. TrackPublishAsync(sequenceNumber)    // Register for confirmation
4. BasicPublishAsync(...)               // Send to RabbitMQ
5. await confirmationTask               // Wait for ACK/NACK
6. MarkAsProcessedAsync()               // Update outbox on success
```

### PublisherConfirmationTracker

Thread-safe service that correlates RabbitMQ confirmations with sequence numbers.

**Key Features:**

- `ConcurrentDictionary<ulong, TaskCompletionSource<bool>>` for tracking
- Handles single and multiple ACKs/NACKs
- 30-second timeout per message
- Comprehensive logging for observability

**Event Handlers:**

- `BasicAcksAsync`: Broker confirms message delivery
- `BasicNacksAsync`: Broker rejects message
- `BasicReturnAsync`: Message is unroutable

### OutboxRepository

Encapsulates all database operations for outbox messages.

**Methods:**

- `AddToOutboxAsync<T>()`: Insert new message
- `GetUnprocessedMessagesAsync()`: Query pending messages
- `MarkAsProcessedAsync()`: Update after successful publish
- `MarkAsErrorAsync()`: Record failures with error details

### UnitOfWork

Manages database transactions for the outbox pattern.

**Features:**

- Opens connection automatically
- Provides transaction to repositories
- Ensures atomic operations
- Implements `IAsyncDisposable` for cleanup

## ⚙️ Configuration

### Connection Strings (appsettings.json)

```json
{
  "ConnectionStrings": {
    "producerdb": "Server=localhost;Database=ProducerDb;Trusted_Connection=True;",
    "rabbitmq": "amqp://guest:guest@localhost:5672"
  }
}
```

### RabbitMQ Channel Options (Program.cs)

```csharp
var channelOpts = new CreateChannelOptions(
    publisherConfirmationsEnabled: true,           // Enable confirmations
    publisherConfirmationTrackingEnabled: true,    // Track sequence numbers
    outstandingPublisherConfirmationsRateLimiter: new ThrottlingRateLimiter(10)
);
```

### Outbox Polling (appsettings.json)

```json
{
  "Outbox": {
    "BatchSize": 100 // Process up to 100 messages per poll
  }
}
```

### Publisher Settings (appsettings.json)

```json
{
  "Publisher": {
    "Period": "00:00:10", // Poll every 10 seconds
    "PublisherConfirmsTimeout": "00:00:05", // Wait up to 5 seconds for confirmation
    "QueueName": "weather" // Target RabbitMQ queue
  }
}
```

## 🧪 Testing

### Manual Testing

1. **Send a weather forecast request**

   ```bash
   curl http://localhost:5120/weatherforecast
   ```

2. **Check outbox table**

   ```sql
   SELECT * FROM dbo.outbox_messages
   WHERE processed_on_utc IS NULL
   ORDER BY occurred_on_utc DESC
   ```

3. **Monitor RabbitMQ**

   - Open RabbitMQ Management UI
   - Navigate to Queues → `weather`
   - Observe message delivery

4. **Check consumer logs**
   - View Aspire Dashboard
   - Check Consumer service logs
   - Verify message processing

### Testing Publisher Confirmations

1. **Stop RabbitMQ** to simulate NACK/timeout

   ```bash
   docker stop rabbitmq
   ```

2. **Send request** - message stays in outbox with error

3. **Restart RabbitMQ**

   ```bash
   docker start rabbitmq
   ```

4. **Message reprocessed** on next poll

## 🐛 Troubleshooting

### Messages not being processed

**Check:**

1. Background service is running (Aspire Dashboard)
2. RabbitMQ is accessible
3. Database connection is valid
4. Check `outbox_messages.error` column for details

**Common errors:**

- `Connection is closed` → Database connection issue
- `Timeout waiting for confirmation` → RabbitMQ overloaded or unreachable
- `Message was not confirmed by broker (NACK received)` → RabbitMQ rejected message

### Confirmation timeouts

**Possible causes:**

- RabbitMQ broker under heavy load
- Network latency
- Confirmation timeout too short (increase from 30s)

**Solution:**

Increase timeout in appsettings.json

```csharp
{
  "Publisher": {
    "PublisherConfirmsTimeout": "00:00:10" // Increase to 10 seconds
  }
}
```

### Database migration errors

**Reset migrations:**

```bash
dotnet ef database drop --project Producer
dotnet ef database update --project Producer
```

### RabbitMQ connection issues

**Verify Docker container:**

```bash
docker ps | grep rabbitmq
docker logs <container-id>
```

**Check connection string:**

- Default: `amqp://guest:guest@localhost:5672`
- Ensure firewall allows port 5672

## 📚 Key Patterns Explained

### Transactional Outbox Pattern

**Problem:** How do you ensure a database update and message publish happen atomically?

**Solution:** Store the message in the database as part of the same transaction, then publish it asynchronously.

**Benefits:**

- ✅ Prevents message loss
- ✅ Ensures consistency between database and message broker
- ✅ Enables reliable distributed transactions

**Tradeoffs:**

- ❌ Eventual consistency (slight delay)
- ❌ Additional database operations
- ❌ Requires background worker

### Publisher Confirmations

**Problem:** How do you know RabbitMQ actually received and persisted your message?

**Solution:** Use RabbitMQ publisher confirms to wait for broker acknowledgment.

**Implementation:**

1. Enable confirmations on channel
2. Get sequence number before each publish
3. Track confirmation with `TaskCompletionSource`
4. Subscribe to ACK/NACK events
5. Await confirmation before marking as processed

**Benefits:**

- ✅ Guaranteed delivery to broker
- ✅ No false positives in outbox
- ✅ Proper error handling for broker issues

### Unit of Work Pattern

**Problem:** How do you manage database transactions consistently across repositories?

**Solution:** Encapsulate connection and transaction management in a single component.

**Benefits:**

- ✅ Centralized transaction logic
- ✅ Easier to test
- ✅ Prevents transaction leaks

## 🔗 Additional Resources

- [.NET Aspire Documentation](https://learn.microsoft.com/dotnet/aspire/)
- [RabbitMQ Publisher Confirms](https://www.rabbitmq.com/confirms.html)
- [Transactional Outbox Pattern](https://microservices.io/patterns/data/transactional-outbox.html)
- [RabbitMQ .NET Client Guide](https://www.rabbitmq.com/dotnet-api-guide.html)

## 📝 Development Guide

### Adding a New Message Type

1. **Create entity class**

   ```csharp
   public class OrderCreatedEvent
   {
       public Guid OrderId { get; set; }
       public decimal Amount { get; set; }
   }
   ```

2. **Add to outbox in controller**

   ```csharp
   await _unitOfWork.BeginTransactionAsync();
   await using (_unitOfWork)
   {
       await _outboxRepository.AddToOutboxAsync(orderEvent);
       await _unitOfWork.CommitAsync();
   }
   ```

3. **Consumer automatically processes** from outbox

### Debugging Tips

1. **Enable debug logging**

   ```json
   {
     "Logging": {
       "LogLevel": {
         "Producer": "Debug",
         "RabbitMQ.Client": "Information"
       }
     }
   }
   ```

2. **Monitor pending confirmations**

   ```csharp
   _logger.LogInformation("Pending confirmations: {Count}",
       _confirmationTracker.PendingCount);
   ```

3. **Query stuck messages**

   ```sql
   SELECT * FROM dbo.outbox_messages
   WHERE processed_on_utc IS NULL
   AND occurred_on_utc < DATEADD(MINUTE, -5, GETUTCDATE())
   ```

## 🤝 Contributing

Contributions are welcome! Please ensure:

- Code follows existing patterns
- Tests are included
- Documentation is updated
- Commits are descriptive

## 📄 License

This project is for demonstration purposes.

---

**Built with** ❤️ **using .NET Aspire, RabbitMQ, and Entity Framework Core**
