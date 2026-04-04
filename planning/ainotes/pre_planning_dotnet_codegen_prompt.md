# .NET 10+ Microservices Code Generation Prompt

I need you to generate production-ready **.NET 10.0+ code** using **C# 13** for a microservices API architecture.

## Critical Context

This is a **commercial product** with a **10+ year lifecycle** that will be part of a **microservices ecosystem with 10+ API services**.

The API is:
- **Multi-tenant** (mandatory `tenant_id` column on all tables)
- **Multi-database** from day one (SQL Server, PostgreSQL, MySQL)
- **Highly transactional** (bookings, financials, stateful workflows)
- Must follow **Clean Architecture** principles (lean, not ceremonious)

## Non-Negotiables

🚫 **Do NOT use Entity Framework / EF Core**  
✅ **Use explicit SQL** with Dapper or ADO.NET  
✅ **Create interfaces for services** where multiple implementations are plausible (email, SMS, payments, storage, etc.)  
✅ **Use British English** for all identifiers (colour, not color; authorisation, not authorization)  
✅ **Multi-database support** via `ISqlDialect` abstraction from day one  
✅ **Tenant isolation enforcement** in every query  
✅ **Transactional Outbox pattern** for messaging  
✅ **Include namespace declarations** using file-scoped namespaces  
✅ **Target .NET 10** (or latest available)

---

## What I Need You To Generate

Please generate code for: **[SPECIFY YOUR REQUIREMENT HERE]**

### Example Requests:

#### Infrastructure Requests
- "The shared data access infrastructure (ISqlDialect, IDbConnectionFactory, ITenantScopedConnection, dialect implementations for SQL Server and PostgreSQL)"
- "Database migration infrastructure that works across all three database providers with example migrations"
- "Transactional Outbox implementation with publisher background service and RabbitMQ integration"
- "Tenant resolution middleware that extracts tenant_id from JWT claims and populates ITenantContext"
- "OpenTelemetry setup with tenant-aware tracing, metrics, and Datadog OTLP exporter configuration"
- "Configuration hot-reload system with validation and rollback for operational settings"
- "Cache profile system with in-memory, Redis, and HTTP caching support"

#### Feature/Domain Requests
- "Complete Booking aggregate with repository, commands, queries, and API endpoints"
- "Customer aggregate with address value object, repository pattern, CQRS handlers, and RESTful endpoints"
- "Invoice generation service with PDF creation, email delivery, and payment tracking"
- "Audit logging system that captures all entity changes with before/after snapshots"

#### External Service Integrations
- "Email service abstraction with SendGrid and Microsoft Graph implementations"
- "SMS service with Twilio implementation and configuration-based provider selection"
- "File storage abstraction with local filesystem and AWS S3 implementations"
- "Payment gateway abstraction with Stripe implementation including webhook handling"

#### Cross-Cutting Concerns
- "Global exception handling middleware with tenant-aware error logging"
- "Request/response logging middleware with configurable time windows"
- "Health check endpoints for each database provider with detailed status reporting"
- "API versioning setup with URL-based versioning and backward compatibility support"

---

## Architecture Guidelines

### Project Structure (Per Service)

```
ServiceName.Domain/
  ├── Aggregates/           # Aggregate roots (entities with business logic)
  ├── ValueObjects/         # Immutable value objects
  ├── DomainServices/       # Complex domain logic spanning aggregates
  ├── Events/              # Domain events
  └── Exceptions/          # Domain-specific exceptions

ServiceName.Application/
  ├── Commands/            # Write operations (create, update, delete)
  │   ├── CreateBooking/
  │   │   ├── CreateBookingCommand.cs
  │   │   ├── CreateBookingCommandHandler.cs
  │   │   └── CreateBookingCommandValidator.cs
  │   └── UpdateBooking/
  ├── Queries/             # Read operations (projections)
  │   ├── GetBookingDetail/
  │   │   ├── GetBookingDetailQuery.cs
  │   │   └── GetBookingDetailQueryHandler.cs
  │   └── SearchBookings/
  ├── Contracts/           # DTOs, interfaces for external dependencies
  │   ├── Dtos/
  │   └── Interfaces/
  └── Common/              # Shared application logic
      ├── Behaviours/      # Pipeline behaviours (validation, logging, etc.)
      └── Models/          # Pagination, result wrappers, etc.

ServiceName.Infrastructure/
  ├── Data/
  │   ├── Repositories/    # Repository implementations
  │   ├── Queries/         # Query implementations (Dapper)
  │   └── Migrations/      # Database migrations
  ├── ExternalServices/    # Third-party API integrations
  ├── Messaging/           # RabbitMQ publishers/subscribers
  └── Configuration/       # Infrastructure-specific configuration

ServiceName.Api/
  ├── Endpoints/           # Minimal APIs or Controllers
  │   ├── BookingEndpoints.cs
  │   └── HealthEndpoints.cs
  ├── Middleware/          # Custom middleware
  ├── Configuration/       # API-specific setup
  ├── Program.cs           # Application entry point
  └── appsettings.json
```

### Shared Project Structure

```
Acme.Shared.Core/
  ├── ITenantContext.cs
  ├── ICurrentUser.cs
  ├── Result.cs            # Result pattern for operation outcomes
  ├── PagedResult.cs       # Pagination wrapper
  └── Exceptions/          # Shared exception types

Acme.Shared.Data/
  ├── ISqlDialect.cs
  ├── IDbConnectionFactory.cs
  ├── ITenantScopedConnection.cs
  ├── IQueryBuilder.cs
  ├── ITransactionScope.cs
  ├── Dialects/
  │   ├── SqlServerDialect.cs
  │   ├── PostgreSqlDialect.cs
  │   └── MySqlDialect.cs
  ├── Migrations/
  │   ├── IMigration.cs
  │   ├── IMigrationRunner.cs
  │   └── MigrationContext.cs
  └── TenantScoping/
      ├── TenantScopedConnection.cs
      └── TenantQueryValidator.cs

Acme.Shared.Infrastructure/
  ├── Email/
  │   ├── IEmailService.cs
  │   ├── EmailMessage.cs
  │   ├── EmailSendResult.cs
  │   └── EmailSendOptions.cs
  ├── Sms/
  │   └── ISmsService.cs
  ├── Storage/
  │   ├── IFileStorage.cs
  │   └── StorageResult.cs
  ├── Payments/
  │   └── IPaymentGateway.cs
  └── Pdf/
      └── IPdfGenerator.cs

Acme.Shared.Infrastructure.SendGrid/
  ├── SendGridEmailService.cs
  ├── SendGridOptions.cs
  └── ServiceCollectionExtensions.cs

Acme.Shared.Infrastructure.MsGraph/
  ├── MicrosoftGraphEmailService.cs
  ├── MsGraphOptions.cs
  └── ServiceCollectionExtensions.cs

Acme.Shared.Messaging/
  ├── IMessageBus.cs
  ├── IOutboxPublisher.cs
  ├── OutboxMessage.cs
  ├── RabbitMq/
  │   ├── RabbitMqMessageBus.cs
  │   ├── RabbitMqOptions.cs
  │   └── RabbitMqConnection.cs
  └── Outbox/
      ├── OutboxPublisher.cs         # Background service
      ├── OutboxRepository.cs
      └── OutboxPublisherOptions.cs

Acme.Shared.Observability/
  ├── OpenTelemetryExtensions.cs
  ├── TenantTracingMiddleware.cs
  ├── Metrics/
  │   └── ApplicationMetrics.cs
  └── Logging/
      └── LoggingExtensions.cs

Acme.Shared.Web/
  ├── Middleware/
  │   ├── TenantResolutionMiddleware.cs
  │   ├── ExceptionHandlingMiddleware.cs
  │   └── RequestResponseLoggingMiddleware.cs
  ├── Caching/
  │   ├── CacheProfileAttribute.cs
  │   ├── CacheProfileOptions.cs
  │   └── CacheService.cs
  ├── Configuration/
  │   ├── ConfigurationValidator.cs
  │   └── HotReloadService.cs
  └── HealthChecks/
      └── DatabaseHealthCheck.cs
```

---

## Database Access Rules

### Every Query MUST:
- Include `tenant_id` in the WHERE clause
- Use parameterised queries (never string concatenation)
- Handle NULL values safely
- Use `ISqlDialect` for database-specific SQL
- Use `CancellationToken` for async operations
- Return DTOs, not domain entities (for queries)

### Standard Query Pattern:

```csharp
public class BookingQueries : IBookingQueries
{
    private readonly ITenantScopedConnection _connection;
    private readonly ISqlDialect _dialect;
    private readonly ILogger<BookingQueries> _logger;

    public BookingQueries(
        ITenantScopedConnection connection,
        ISqlDialect dialect,
        ILogger<BookingQueries> logger)
    {
        _connection = connection;
        _dialect = dialect;
        _logger = logger;
    }

    public async Task<BookingDetailDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        const string sql = @"
            SELECT 
                booking_id AS BookingId,
                tenant_id AS TenantId,
                customer_name AS CustomerName,
                booking_date AS BookingDate,
                total_amount AS TotalAmount,
                status AS Status,
                created_at AS CreatedAt
            FROM bookings
            WHERE booking_id = @Id 
              AND tenant_id = @TenantId";
        
        try
        {
            return await _connection.QuerySingleOrDefaultAsync<BookingDetailDto>(
                sql, 
                new { Id = id, TenantId = _connection.TenantId },
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, 
                "Failed to retrieve booking {BookingId} for tenant {TenantId}",
                id,
                _connection.TenantId
            );
            throw;
        }
    }
}
```

### Standard Repository Pattern:

```csharp
public class BookingRepository : IBookingRepository
{
    private readonly ITenantScopedConnection _connection;
    private readonly ISqlDialect _dialect;
    private readonly IOutboxPublisher _outbox;
    private readonly ILogger<BookingRepository> _logger;

    public async Task<Booking> CreateAsync(Booking booking, CancellationToken ct)
    {
        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        
        var sql = $@"
            INSERT INTO bookings (
                booking_id, tenant_id, customer_name, booking_date, 
                total_amount, status, created_at
            )
            VALUES (
                @BookingId, @TenantId, @CustomerName, @BookingDate,
                @TotalAmount, @Status, {_dialect.GetUtcNow()}
            )";
        
        await _connection.ExecuteAsync(sql, new
        {
            BookingId = booking.Id,
            TenantId = _connection.TenantId,
            CustomerName = booking.CustomerName,
            BookingDate = booking.BookingDate,
            TotalAmount = booking.TotalAmount,
            Status = booking.Status.ToString()
        }, ct);
        
        // Publish domain event via outbox
        await _outbox.EnqueueAsync(
            new BookingCreatedEvent(booking.Id, _connection.TenantId),
            ct
        );
        
        transaction.Complete();
        
        return booking;
    }
}
```

---

## Multi-Database Support via ISqlDialect

### Core Abstraction:

```csharp
public interface ISqlDialect
{
    string ProviderName { get; }
    
    // Identity handling
    string GetLastInsertedId(string tableName, string idColumnName);
    string GetUuidDefault();
    DbType GetUuidDbType();
    string GetUuidColumnType();
    
    // Date/time
    string GetUtcNow();
    string DateAdd(string dateColumn, int days);
    string DateDiff(string startColumn, string endColumn);
    
    // Pagination
    string ApplyPagination(string sql, int offset, int limit);
    
    // Locking
    string ForUpdate(string sql);
    string SkipLocked(string sql);
    
    // String operations
    string CaseInsensitiveCompare(string column, string parameter);
    string Concat(params string[] values);
    
    // Aggregation
    string Coalesce(params string[] columns);
    
    // Type definitions
    string GetTextColumnType(int? maxLength = null);
    string GetDecimalColumnType(int precision, int scale);
    string GetBooleanColumnType();
    string GetDateTimeColumnType();
    string GetIntColumnType();
    string GetBigIntColumnType();
    
    // JSON support
    string JsonExtract(string column, string path);
    bool SupportsJsonFunctions { get; }
}
```

### Example Implementation (SQL Server):

```csharp
public class SqlServerDialect : ISqlDialect
{
    public string ProviderName => "SqlServer";
    
    public string GetLastInsertedId(string tableName, string idColumnName) 
        => "SELECT CAST(SCOPE_IDENTITY() AS INT)";
    
    public string GetUuidDefault() 
        => "NEWID()";
    
    public DbType GetUuidDbType() 
        => DbType.Guid;
    
    public string GetUuidColumnType() 
        => "UNIQUEIDENTIFIER";
    
    public string GetUtcNow() 
        => "GETUTCDATE()";
    
    public string DateAdd(string dateColumn, int days)
        => $"DATEADD(DAY, {days}, {dateColumn})";
    
    public string ApplyPagination(string sql, int offset, int limit)
        => $"{sql} OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";
    
    public string ForUpdate(string sql)
        => $"{sql} WITH (UPDLOCK, ROWLOCK)";
    
    public string CaseInsensitiveCompare(string column, string parameter)
        => $"{column} COLLATE SQL_Latin1_General_CP1_CI_AS = {parameter}";
    
    public string GetTextColumnType(int? maxLength = null)
        => maxLength.HasValue ? $"NVARCHAR({maxLength})" : "NVARCHAR(MAX)";
    
    public string GetDecimalColumnType(int precision, int scale)
        => $"DECIMAL({precision},{scale})";
    
    public string GetBooleanColumnType() 
        => "BIT";
    
    public string GetDateTimeColumnType() 
        => "DATETIME2";
    
    public bool SupportsJsonFunctions => true;
    
    public string JsonExtract(string column, string path)
        => $"JSON_VALUE({column}, '$.{path}')";
}
```

---

## Tenant Context & Isolation

### Core Interfaces:

```csharp
public interface ITenantContext
{
    Guid TenantId { get; }
    bool IsResolved { get; }
    string? TenantName { get; }
}

public interface ITenantScopedConnection
{
    Guid TenantId { get; }
    
    Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql, 
        object? param = null, 
        CancellationToken ct = default);
    
    Task<IEnumerable<T>> QueryAsync<T>(
        string sql, 
        object? param = null, 
        CancellationToken ct = default);
    
    Task<int> ExecuteAsync(
        string sql, 
        object? param = null, 
        CancellationToken ct = default);
}
```

### Tenant Resolution Middleware:

```csharp
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext)
    {
        // Extract tenant_id from JWT claims
        var tenantIdClaim = context.User.FindFirst("tenant_id")?.Value;
        
        if (string.IsNullOrEmpty(tenantIdClaim))
        {
            _logger.LogWarning("Request missing tenant_id claim");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new 
            { 
                Error = "Tenant context required" 
            });
            return;
        }
        
        if (!Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning(
                "Invalid tenant_id format: {TenantIdClaim}", 
                tenantIdClaim
            );
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new 
            { 
                Error = "Invalid tenant identifier" 
            });
            return;
        }
        
        // Populate tenant context (cast to implementation)
        if (tenantContext is TenantContext mutableContext)
        {
            mutableContext.SetTenant(tenantId);
        }
        
        _logger.LogDebug(
            "Tenant context resolved: {TenantId}", 
            tenantId
        );
        
        await _next(context);
    }
}
```

### Query Validation:

```csharp
public static class TenantQueryValidator
{
    public static void ValidateTenantScoping(string sql)
    {
        if (!sql.Contains("tenant_id", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Query missing tenant_id filter. SQL: {sql}"
            );
        }
    }
}
```

---

## Transactional Outbox Pattern

### Outbox Table Schema (Multi-DB):

```csharp
public class CreateOutboxTable : Migration
{
    public override int Version => 1;
    public override string Description => "Create outbox_messages table";

    public override async Task UpAsync(IMigrationContext context)
    {
        var sql = $@"
            CREATE TABLE outbox_messages (
                id {context.Dialect.GetUuidColumnType()} PRIMARY KEY,
                tenant_id {context.Dialect.GetUuidColumnType()} NOT NULL,
                aggregate_type {context.Dialect.GetTextColumnType(255)} NOT NULL,
                aggregate_id {context.Dialect.GetUuidColumnType()} NOT NULL,
                event_type {context.Dialect.GetTextColumnType(255)} NOT NULL,
                payload {context.Dialect.GetTextColumnType()} NOT NULL,
                created_at {context.Dialect.GetDateTimeColumnType()} NOT NULL DEFAULT {context.Dialect.GetUtcNow()},
                published_at {context.Dialect.GetDateTimeColumnType()} NULL,
                retry_count {context.Dialect.GetIntColumnType()} NOT NULL DEFAULT 0,
                last_error {context.Dialect.GetTextColumnType()} NULL
            )";

        await context.ExecuteAsync(sql);

        // Create indices
        await context.ExecuteAsync(@"
            CREATE INDEX idx_outbox_unpublished 
            ON outbox_messages(created_at) 
            WHERE published_at IS NULL"
        );

        await context.ExecuteAsync(@"
            CREATE INDEX idx_outbox_tenant 
            ON outbox_messages(tenant_id, created_at)"
        );
    }
}
```

### Outbox Publisher (Background Service):

```csharp
public class OutboxPublisher : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxPublisher> _logger;
    private readonly OutboxPublisherOptions _options;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox publisher starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var repository = scope.ServiceProvider
                    .GetRequiredService<IOutboxRepository>();
                var messageBus = scope.ServiceProvider
                    .GetRequiredService<IMessageBus>();

                var batch = await repository.FetchUnpublishedBatchAsync(
                    _options.BatchSize,
                    stoppingToken
                );

                foreach (var message in batch)
                {
                    try
                    {
                        await messageBus.PublishAsync(
                            message.EventType,
                            message.Payload,
                            message.TenantId,
                            stoppingToken
                        );

                        await repository.MarkAsPublishedAsync(
                            message.Id,
                            stoppingToken
                        );

                        _logger.LogDebug(
                            "Published outbox message {MessageId} for tenant {TenantId}",
                            message.Id,
                            message.TenantId
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to publish outbox message {MessageId}",
                            message.Id
                        );

                        await repository.IncrementRetryCountAsync(
                            message.Id,
                            ex.Message,
                            stoppingToken
                        );

                        if (message.RetryCount >= _options.MaxRetries)
                        {
                            await repository.MoveToDeadLetterAsync(
                                message.Id,
                                stoppingToken
                            );
                        }
                    }
                }

                await Task.Delay(
                    _options.PollingInterval,
                    stoppingToken
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Outbox publisher encountered an error"
                );

                await Task.Delay(
                    TimeSpan.FromSeconds(5),
                    stoppingToken
                );
            }
        }

        _logger.LogInformation("Outbox publisher stopped");
    }
}
```

---

## Repository Pattern (Write Model)

### Standard Repository Interface:

```csharp
public interface IRepository<TAggregate> where TAggregate : class
{
    Task<TAggregate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TAggregate> CreateAsync(TAggregate aggregate, CancellationToken ct = default);
    Task UpdateAsync(TAggregate aggregate, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IBookingRepository : IRepository<Booking>
{
    Task<IEnumerable<Booking>> GetByCustomerAsync(
        Guid customerId, 
        CancellationToken ct = default);
    
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}
```

### Base Repository Implementation:

```csharp
public abstract class RepositoryBase<TAggregate> where TAggregate : class
{
    protected readonly ITenantScopedConnection Connection;
    protected readonly ISqlDialect Dialect;
    protected readonly IOutboxPublisher Outbox;
    protected readonly ILogger Logger;

    protected RepositoryBase(
        ITenantScopedConnection connection,
        ISqlDialect dialect,
        IOutboxPublisher outbox,
        ILogger logger)
    {
        Connection = connection;
        Dialect = dialect;
        Outbox = outbox;
        Logger = logger;
    }

    protected async Task PublishDomainEventsAsync(
        TAggregate aggregate,
        CancellationToken ct)
    {
        // Assuming aggregates implement IDomainEventProvider
        if (aggregate is IDomainEventProvider eventProvider)
        {
            var events = eventProvider.GetDomainEvents();
            
            foreach (var domainEvent in events)
            {
                await Outbox.EnqueueAsync(domainEvent, ct);
            }
            
            eventProvider.ClearDomainEvents();
        }
    }
}
```

---

## CQRS Pattern (Commands & Queries)

### Command Pattern:

```csharp
// Command
public record CreateBookingCommand(
    string CustomerName,
    DateTime BookingDate,
    decimal TotalAmount
) : IRequest<Result<Guid>>;

// Handler
public class CreateBookingCommandHandler 
    : IRequestHandler<CreateBookingCommand, Result<Guid>>
{
    private readonly IBookingRepository _repository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CreateBookingCommandHandler> _logger;

    public CreateBookingCommandHandler(
        IBookingRepository repository,
        ITenantContext tenantContext,
        ILogger<CreateBookingCommandHandler> logger)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(
        CreateBookingCommand command,
        CancellationToken ct)
    {
        try
        {
            var booking = Booking.Create(
                command.CustomerName,
                command.BookingDate,
                command.TotalAmount,
                _tenantContext.TenantId
            );

            await _repository.CreateAsync(booking, ct);

            _logger.LogInformation(
                "Created booking {BookingId} for tenant {TenantId}",
                booking.Id,
                _tenantContext.TenantId
            );

            return Result<Guid>.Success(booking.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create booking for tenant {TenantId}",
                _tenantContext.TenantId
            );
            
            return Result<Guid>.Failure("Failed to create booking");
        }
    }
}

// Validator
public class CreateBookingCommandValidator 
    : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingCommandValidator()
    {
        RuleFor(x => x.CustomerName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.BookingDate)
            .GreaterThan(DateTime.UtcNow.AddMinutes(-5))
            .WithMessage("Booking date must be in the future");

        RuleFor(x => x.TotalAmount)
            .GreaterThan(0)
            .WithMessage("Total amount must be positive");
    }
}
```

### Query Pattern:

```csharp
// Query
public record GetBookingDetailQuery(Guid BookingId) 
    : IRequest<Result<BookingDetailDto>>;

// Handler
public class GetBookingDetailQueryHandler 
    : IRequestHandler<GetBookingDetailQuery, Result<BookingDetailDto>>
{
    private readonly IBookingQueries _queries;
    private readonly ILogger<GetBookingDetailQueryHandler> _logger;

    public GetBookingDetailQueryHandler(
        IBookingQueries queries,
        ILogger<GetBookingDetailQueryHandler> logger)
    {
        _queries = queries;
        _logger = logger;
    }

    public async Task<Result<BookingDetailDto>> Handle(
        GetBookingDetailQuery query,
        CancellationToken ct)
    {
        var booking = await _queries.GetByIdAsync(query.BookingId, ct);

        if (booking is null)
        {
            return Result<BookingDetailDto>.Failure("Booking not found");
        }

        return Result<BookingDetailDto>.Success(booking);
    }
}
```

---

## Interface-First Design for External Services

### Email Service Example:

```csharp
// Interface (in Acme.Shared.Infrastructure)
public interface IEmailService
{
    Task<EmailSendResult> SendAsync(
        EmailMessage message,
        EmailSendOptions? options = null,
        CancellationToken ct = default);
    
    Task<IEnumerable<EmailSendResult>> SendBatchAsync(
        IEnumerable<EmailMessage> messages,
        EmailSendOptions? options = null,
        CancellationToken ct = default);
}

public record EmailMessage(
    string To,
    string Subject,
    string Body,
    bool IsHtml = true,
    string? From = null,
    IEnumerable<EmailAttachment>? Attachments = null,
    Dictionary<string, string>? Metadata = null
);

public record EmailAttachment(
    string FileName,
    byte[] Content,
    string ContentType
);

public record EmailSendResult(
    bool Success,
    string? MessageId,
    string? ErrorMessage,
    DateTime SentAt
);

public record EmailSendOptions(
    EmailPriority Priority = EmailPriority.Normal,
    DateTime? ScheduledSendTime = null,
    bool TrackOpens = false,
    bool TrackClicks = false,
    Dictionary<string, string>? Tags = null
);

public enum EmailPriority
{
    Low,
    Normal,
    High
}

// SendGrid Implementation (in Acme.Shared.Infrastructure.SendGrid)
public class SendGridEmailService : IEmailService
{
    private readonly ISendGridClient _client;
    private readonly SendGridOptions _options;
    private readonly ILogger<SendGridEmailService> _logger;

    public SendGridEmailService(
        ISendGridClient client,
        IOptions<SendGridOptions> options,
        ILogger<SendGridEmailService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(
        EmailMessage message,
        EmailSendOptions? options = null,
        CancellationToken ct = default)
    {
        try
        {
            var msg = new SendGridMessage
            {
                From = new EmailAddress(
                    message.From ?? _options.DefaultFromAddress,
                    _options.DefaultFromName
                ),
                Subject = message.Subject,
                PlainTextContent = message.IsHtml ? null : message.Body,
                HtmlContent = message.IsHtml ? message.Body : null
            };

            msg.AddTo(message.To);

            if (message.Attachments?.Any() == true)
            {
                foreach (var attachment in message.Attachments)
                {
                    msg.AddAttachment(
                        attachment.FileName,
                        Convert.ToBase64String(attachment.Content),
                        attachment.ContentType
                    );
                }
            }

            var response = await _client.SendEmailAsync(msg, ct);

            var success = response.IsSuccessStatusCode;
            
            if (!success)
            {
                var body = await response.Body.ReadAsStringAsync(ct);
                _logger.LogError(
                    "SendGrid API error: {StatusCode} - {Body}",
                    response.StatusCode,
                    body
                );
            }

            return new EmailSendResult(
                Success: success,
                MessageId: response.Headers
                    .GetValues("X-Message-Id")
                    .FirstOrDefault(),
                ErrorMessage: success 
                    ? null 
                    : await response.Body.ReadAsStringAsync(ct),
                SentAt: DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send email via SendGrid to {To}",
                message.To
            );
            
            return new EmailSendResult(
                Success: false,
                MessageId: null,
                ErrorMessage: ex.Message,
                SentAt: DateTime.UtcNow
            );
        }
    }

    public async Task<IEnumerable<EmailSendResult>> SendBatchAsync(
        IEnumerable<EmailMessage> messages,
        EmailSendOptions? options = null,
        CancellationToken ct = default)
    {
        var results = new List<EmailSendResult>();

        foreach (var message in messages)
        {
            var result = await SendAsync(message, options, ct);
            results.Add(result);
        }

        return results;
    }
}

// Configuration Options
public class SendGridOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string DefaultFromAddress { get; set; } = string.Empty;
    public string DefaultFromName { get; set; } = string.Empty;
}

// Service Registration Extensions
public static class SendGridServiceExtensions
{
    public static IServiceCollection AddSendGridEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SendGridOptions>(
            configuration.GetSection("Email:SendGrid")
        );

        services.AddSingleton<ISendGridClient>(sp =>
        {
            var options = sp
                .GetRequiredService<IOptions<SendGridOptions>>()
                .Value;
            return new SendGridClient(options.ApiKey);
        });

        services.AddScoped<IEmailService, SendGridEmailService>();

        return services;
    }
}
```

---

## Configuration Pattern

### Configuration Structure:

```json
{
  "Database": {
    "Provider": "SqlServer",
    "SqlServer": {
      "ConnectionString": "ENC:base64encryptedvalue==",
      "CommandTimeout": 30,
      "MaxRetryCount": 3
    },
    "PostgreSql": {
      "ConnectionString": "ENC:base64encryptedvalue==",
      "CommandTimeout": 30,
      "MaxRetryCount": 3
    },
    "MySql": {
      "ConnectionString": "ENC:base64encryptedvalue==",
      "CommandTimeout": 30,
      "MaxRetryCount": 3
    }
  },
  "Email": {
    "Provider": "SendGrid",
    "SendGrid": {
      "ApiKey": "ENC:base64encryptedvalue==",
      "DefaultFromAddress": "noreply@example.com",
      "DefaultFromName": "Example System"
    },
    "MicrosoftGraph": {
      "TenantId": "...",
      "ClientId": "...",
      "ClientSecret": "ENC:base64encryptedvalue=="
    }
  },
  "RabbitMq": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "ENC:base64encryptedvalue==",
    "VirtualHost": "/",
    "Exchange": "events"
  },
  "Jwt": {
    "Issuer": "https://auth.example.com",
    "Audience": "bookings-api",
    "SigningKey": "ENC:base64encryptedvalue==",
    "ExpirationMinutes": 60
  },
  "Caching": {
    "Redis": {
      "ConnectionString": "localhost:6379",
      "InstanceName": "bookings:"
    }
  }
}
```

### Service Registration with Provider Selection:

```csharp
public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddEmailService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Email:Provider"];

        return provider switch
        {
            "SendGrid" => services.AddSendGridEmail(configuration),
            "MicrosoftGraph" => services.AddMicrosoftGraphEmail(configuration),
            _ => throw new InvalidOperationException(
                $"Unknown email provider: {provider}")
        };
    }

    public static IServiceCollection AddDatabaseServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"];

        services.AddSingleton<ISqlDialect>(sp => provider switch
        {
            "SqlServer" => new SqlServerDialect(),
            "PostgreSql" => new PostgreSqlDialect(),
            "MySql" => new MySqlDialect(),
            _ => throw new InvalidOperationException(
                $"Unknown database provider: {provider}")
        });

        services.AddScoped<IDbConnectionFactory, DbConnectionFactory>();
        services.AddScoped<ITenantScopedConnection, TenantScopedConnection>();

        return services;
    }
}
```

---

## OpenTelemetry & Observability

### Setup with Tenant-Aware Tracing:

```csharp
public static class ObservabilityExtensions
{
    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .AddSource(serviceName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            activity.SetTag("http.request.path", 
                                request.Path.Value);
                        };
                    })
                    .AddSqlClientInstrumentation(options =>
                    {
                        options.SetDbStatementForText = true;
                        options.RecordException = true;
                    })
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(
                            configuration["OpenTelemetry:Endpoint"] 
                            ?? "http://localhost:4317"
                        );
                    });
            })
            .WithMetrics(builder =>
            {
                builder
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(serviceName)
                    .AddOtlpExporter();
            });

        // Add custom metrics
        services.AddSingleton<ApplicationMetrics>();

        return services;
    }
}

// Tenant Tracing Middleware
public class TenantTracingMiddleware
{
    private readonly RequestDelegate _next;

    public TenantTracingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext)
    {
        var activity = Activity.Current;
        
        if (activity is not null && tenantContext.IsResolved)
        {
            activity.SetTag("tenant.id", tenantContext.TenantId);
            activity.SetTag("tenant.name", tenantContext.TenantName);
            
            // Add to baggage for downstream propagation
            activity.SetBaggage("tenant.id", 
                tenantContext.TenantId.ToString());
        }

        await _next(context);
    }
}

// Application Metrics
public class ApplicationMetrics
{
    private readonly Meter _meter;
    private readonly Counter<long> _requestCounter;
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _errorCounter;

    public ApplicationMetrics(string serviceName)
    {
        _meter = new Meter(serviceName);
        
        _requestCounter = _meter.CreateCounter<long>(
            "http.server.requests",
            "requests",
            "Number of HTTP requests"
        );
        
        _requestDuration = _meter.CreateHistogram<double>(
            "http.server.request.duration",
            "ms",
            "HTTP request duration"
        );
        
        _errorCounter = _meter.CreateCounter<long>(
            "application.errors",
            "errors",
            "Number of application errors"
        );
    }

    public void RecordRequest(string method, string path, Guid tenantId)
    {
        _requestCounter.Add(1, 
            new KeyValuePair<string, object?>("http.method", method),
            new KeyValuePair<string, object?>("http.path", path),
            new KeyValuePair<string, object?>("tenant.id", tenantId)
        );
    }

    public void RecordDuration(
        double durationMs, 
        string method, 
        int statusCode,
        Guid tenantId)
    {
        _requestDuration.Record(durationMs,
            new KeyValuePair<string, object?>("http.method", method),
            new KeyValuePair<string, object?>("http.status_code", statusCode),
            new KeyValuePair<string, object?>("tenant.id", tenantId)
        );
    }

    public void RecordError(string errorType, Guid tenantId)
    {
        _errorCounter.Add(1,
            new KeyValuePair<string, object?>("error.type", errorType),
            new KeyValuePair<string, object?>("tenant.id", tenantId)
        );
    }
}
```

---

## API Endpoint Pattern (Minimal APIs)

### Standard Endpoint Structure:

```csharp
public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/bookings")
            .WithTags("Bookings")
            .RequireAuthorization();

        group.MapGet("/", GetAllBookings)
            .WithName("GetAllBookings")
            .Produces<PagedResult<BookingListDto>>();

        group.MapGet("/{id:guid}", GetBookingById)
            .WithName("GetBookingById")
            .Produces<BookingDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateBooking)
            .WithName("CreateBooking")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPut("/{id:guid}", UpdateBooking)
            .WithName("UpdateBooking")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteBooking)
            .WithName("DeleteBooking")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetAllBookings(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromServices] IMediator mediator = null!,
        CancellationToken ct = default)
    {
        var query = new GetAllBookingsQuery(pageNumber, pageSize);
        var result = await mediator.Send(query, ct);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> GetBookingById(
        [FromRoute] Guid id,
        [FromServices] IMediator mediator = null!,
        CancellationToken ct = default)
    {
        var query = new GetBookingDetailQuery(id);
        var result = await mediator.Send(query, ct);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.NotFound(result.Error);
    }

    private static async Task<IResult> CreateBooking(
        [FromBody] CreateBookingRequest request,
        [FromServices] IMediator mediator = null!,
        [FromServices] LinkGenerator linkGenerator = null!,
        HttpContext context = null!,
        CancellationToken ct = default)
    {
        var command = new CreateBookingCommand(
            request.CustomerName,
            request.BookingDate,
            request.TotalAmount
        );

        var result = await mediator.Send(command, ct);

        if (!result.IsSuccess)
            return Results.BadRequest(result.Error);

        var location = linkGenerator.GetPathByName(
            context,
            "GetBookingById",
            new { id = result.Value }
        );

        return Results.Created(location, result.Value);
    }

    private static async Task<IResult> UpdateBooking(
        [FromRoute] Guid id,
        [FromBody] UpdateBookingRequest request,
        [FromServices] IMediator mediator = null!,
        CancellationToken ct = default)
    {
        var command = new UpdateBookingCommand(
            id,
            request.CustomerName,
            request.BookingDate,
            request.TotalAmount
        );

        var result = await mediator.Send(command, ct);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.NotFound(result.Error);
    }

    private static async Task<IResult> DeleteBooking(
        [FromRoute] Guid id,
        [FromServices] IMediator mediator = null!,
        CancellationToken ct = default)
    {
        var command = new DeleteBookingCommand(id);
        var result = await mediator.Send(command, ct);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.NotFound(result.Error);
    }
}

// Request DTOs
public record CreateBookingRequest(
    string CustomerName,
    DateTime BookingDate,
    decimal TotalAmount
);

public record UpdateBookingRequest(
    string CustomerName,
    DateTime BookingDate,
    decimal TotalAmount
);
```

---

## Code Quality Requirements

### XML Documentation:

```csharp
/// <summary>
/// Represents a booking in the system.
/// </summary>
public class Booking
{
    /// <summary>
    /// Gets the unique identifier for the booking.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the tenant identifier this booking belongs to.
    /// </summary>
    public Guid TenantId { get; private set; }

    /// <summary>
    /// Creates a new booking with the specified details.
    /// </summary>
    /// <param name="customerName">The name of the customer.</param>
    /// <param name="bookingDate">The date and time of the booking.</param>
    /// <param name="totalAmount">The total amount for the booking.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>A new <see cref="Booking"/> instance.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when customer name is null or empty.
    /// </exception>
    public static Booking Create(
        string customerName,
        DateTime bookingDate,
        decimal totalAmount,
        Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            throw new ArgumentException(
                "Customer name cannot be empty", 
                nameof(customerName)
            );

        if (totalAmount <= 0)
            throw new ArgumentException(
                "Total amount must be positive", 
                nameof(totalAmount)
            );

        return new Booking
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerName = customerName,
            BookingDate = bookingDate,
            TotalAmount = totalAmount,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }
}
```

### Null Safety:

```csharp
#nullable enable

public class BookingService
{
    // Nullable parameter with default
    public async Task<Result> ProcessBookingAsync(
        Guid bookingId,
        string? notes = null,
        CancellationToken ct = default)
    {
        var booking = await _repository.GetByIdAsync(bookingId, ct);
        
        // Explicit null check
        if (booking is null)
        {
            return Result.Failure("Booking not found");
        }

        // Safe null-conditional operator
        var notesLength = notes?.Length ?? 0;

        return Result.Success();
    }
}
```

### Error Handling:

```csharp
public class BookingNotFoundException : Exception
{
    public Guid BookingId { get; }

    public BookingNotFoundException(Guid bookingId)
        : base($"Booking with ID '{bookingId}' was not found.")
    {
        BookingId = bookingId;
    }

    public BookingNotFoundException(Guid bookingId, Exception innerException)
        : base($"Booking with ID '{bookingId}' was not found.", innerException)
    {
        BookingId = bookingId;
    }
}

// Usage
public async Task<Booking> GetBookingAsync(Guid id, CancellationToken ct)
{
    var booking = await _repository.GetByIdAsync(id, ct);
    
    if (booking is null)
    {
        throw new BookingNotFoundException(id);
    }

    return booking;
}
```

### Logging Best Practices:

```csharp
public class BookingService
{
    private readonly ILogger<BookingService> _logger;

    public async Task<Result> CreateBookingAsync(
        CreateBookingCommand command,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Creating booking for customer {CustomerName} on {BookingDate}",
            command.CustomerName,
            command.BookingDate
        );

        try
        {
            var booking = await _repository.CreateAsync(command, ct);

            _logger.LogInformation(
                "Successfully created booking {BookingId} for tenant {TenantId}",
                booking.Id,
                booking.TenantId
            );

            return Result.Success(booking.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create booking for customer {CustomerName}",
                command.CustomerName
            );

            return Result.Failure("Failed to create booking");
        }
    }
}
```

---

## Testing Considerations

### Unit Tests:

```csharp
[TestFixture]
public class BookingTests
{
    [Test]
    public void Create_WithValidData_CreatesBooking()
    {
        // Arrange
        var customerName = "John Smith";
        var bookingDate = DateTime.UtcNow.AddDays(1);
        var totalAmount = 100.00m;
        var tenantId = Guid.NewGuid();

        // Act
        var booking = Booking.Create(
            customerName,
            bookingDate,
            totalAmount,
            tenantId
        );

        // Assert
        Assert.That(booking.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(booking.CustomerName, Is.EqualTo(customerName));
        Assert.That(booking.TenantId, Is.EqualTo(tenantId));
    }

    [Test]
    public void Create_WithEmptyCustomerName_ThrowsException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            Booking.Create(
                string.Empty,
                DateTime.UtcNow,
                100m,
                Guid.NewGuid()
            )
        );
    }
}
```

### Integration Tests (Multi-DB):

```csharp
[TestFixture]
[TestFixtureSource(typeof(DatabaseProviders))]
public class BookingRepositoryIntegrationTests
{
    private readonly string _provider;
    private IContainer? _container;
    private IBookingRepository? _repository;

    public BookingRepositoryIntegrationTests(string provider)
    {
        _provider = provider;
    }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = DatabaseProviders.CreateContainer(_provider);
        await _container.StartAsync();
        
        var connectionString = DatabaseProviders
            .GetConnectionString(_provider, _container);
        
        await RunMigrations(_provider, connectionString);
        
        _repository = CreateRepository(_provider, connectionString);
    }

    [SetUp]
    public async Task SetUp()
    {
        await TruncateBookingsTable();
    }

    [Test]
    public async Task CreateAsync_InsertsBookingSuccessfully()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var booking = Booking.Create(
            "Test Customer",
            DateTime.UtcNow.AddDays(1),
            100m,
            tenantId
        );

        // Act
        var result = await _repository!.CreateAsync(booking, default);

        // Assert
        Assert.That(result.Id, Is.Not.EqualTo(Guid.Empty));
        
        var retrieved = await _repository.GetByIdAsync(result.Id, default);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.CustomerName, Is.EqualTo("Test Customer"));
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }
}

public class DatabaseProviders
{
    public static IEnumerable<string> Providers
    {
        get
        {
            yield return "SqlServer";
            yield return "PostgreSql";
            yield return "MySql";
        }
    }

    public static IContainer CreateContainer(string provider)
    {
        return provider switch
        {
            "SqlServer" => new ContainerBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .WithEnvironment("ACCEPT_EULA", "Y")
                .WithEnvironment("SA_PASSWORD", "StrongP@ssw0rd123")
                .WithPortBinding(1433, true)
                .Build(),

            "PostgreSql" => new ContainerBuilder()
                .WithImage("postgres:16")
                .WithEnvironment("POSTGRES_PASSWORD", "postgres")
                .WithPortBinding(5432, true)
                .Build(),

            "MySql" => new ContainerBuilder()
                .WithImage("mysql:8.0")
                .WithEnvironment("MYSQL_ROOT_PASSWORD", "mysql")
                .WithPortBinding(3306, true)
                .Build(),

            _ => throw new ArgumentException($"Unknown provider: {provider}")
        };
    }
}
```

---

## Output Format Requirements

When generating code, please structure your response as:

### 1. Overview
Brief explanation (2-3 paragraphs) of what you're generating and the architectural decisions made.

### 2. File Structure
Show the complete file/project structure:
```
ProjectName/
  ├── Folder1/
  │   ├── File1.cs
  │   └── File2.cs
  └── Folder2/
```

### 3. Complete Code
Provide full, compilable code for each file. Include:
- Using statements
- Namespace declarations (file-scoped)
- XML documentation
- Complete implementations (no placeholders like `// TODO` or `// Implementation here`)

### 4. Configuration
Show any required configuration files (appsettings.json, etc.)

### 5. Service Registration
Show how to register services in `Program.cs` or dependency injection container

### 6. Usage Examples
Demonstrate how to use the generated code in real scenarios

### 7. Testing Approach
Explain what should be tested and provide test examples

### 8. Trade-offs & Limitations
Document any design decisions, limitations, or areas for future improvement

---

## Example Request Formats

### For Infrastructure Components:
> "Generate the complete shared data access infrastructure including:
> - ISqlDialect interface with all required methods
> - SqlServerDialect, PostgreSqlDialect, and MySqlDialect implementations
> - IDbConnectionFactory and ITenantScopedConnection interfaces
> - Concrete implementations with proper tenant scoping
> - Service registration extensions
> - Example usage in a repository"

### For Complete Features:
> "Generate a complete Booking management feature including:
> - Booking aggregate with business logic
> - BookingRepository with Create, Update, Delete operations
> - BookingQueries for read operations (GetById, Search with pagination)
> - CreateBookingCommand, UpdateBookingCommand, DeleteBookingCommand with handlers
> - FluentValidation validators for each command
> - API endpoints using Minimal APIs
> - Integration with transactional outbox for BookingCreated, BookingUpdated events
> - Multi-database support using ISqlDialect
> - Complete test examples"

### For External Service Integrations:
> "Generate a complete email service integration including:
> - IEmailService interface with Send and SendBatch methods
> - SendGridEmailService implementation
> - MicrosoftGraphEmailService implementation
> - Configuration options classes for both providers
> - Service registration extensions with provider selection
> - Retry logic with Polly
> - Example usage in an application service
> - Unit tests with mocked dependencies"

### For Cross-Cutting Concerns:
> "Generate a comprehensive caching system including:
> - Three-tier caching (HTTP, In-Memory, Redis)
> - CacheProfile attribute for endpoints
> - Configuration-driven cache profiles
> - Runtime cache enable/disable per profile
> - Dry-run mode for testing cache effectiveness
> - Cache invalidation strategies
> - Service registration
> - Example endpoint usage"

---

## Common Patterns & Helpers

### Result Pattern:

```csharp
public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }

    protected Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
}

public class Result<T> : Result
{
    public T? Value { get; }

    protected Result(bool isSuccess, T? value, string? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public new static Result<T> Failure(string error) => new(false, default, error);
}
```

### Paged Result:

```csharp
public record PagedResult<T>(
    IEnumerable<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount
)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
```

### Domain Event:

```csharp
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
}

public record BookingCreatedEvent(
    Guid BookingId,
    Guid TenantId
) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

---

**Now please generate production-ready code for: [YOUR SPECIFIC REQUIREMENT]**