using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// Local developer secrets (gitignored). Provides ENCRYPTION_KEY and any other
// machine-specific values without committing secrets to the repository.
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);

string encryptionKey = builder.Configuration["ENCRYPTION_KEY"]
    ?? throw new InvalidOperationException(
           "ENCRYPTION_KEY is not set. Add it to src/host/Nova.AppHost/appsettings.local.json.");

bool useRedis    = builder.Configuration.GetValue<bool>("Infrastructure:UseRedis");
bool useRabbitMq = builder.Configuration.GetValue<bool>("Infrastructure:UseRabbitMQ");
bool useSeq      = builder.Configuration.GetValue<bool>("Infrastructure:UseSeq");

// Fail fast if Docker is not running and at least one container service is required.
if ((useRedis || useRabbitMq || useSeq) && !IsDockerAvailable())
{
    Console.Error.WriteLine("[AppHost] Docker is not running or is not reachable.");
    Console.Error.WriteLine("[AppHost] Start Docker Desktop (or the Docker daemon) and retry.");
    Environment.Exit(1);
}

// Redis — only started when Infrastructure:UseRedis = true in AppHost/appsettings.json.
// WithLifetime(Persistent): container survives AppHost restarts; data is preserved between runs.
IResourceBuilder<RedisResource>? redis = useRedis
    ? builder.AddRedis("redis").WithLifetime(ContainerLifetime.Persistent)
    : null;

// RabbitMQ — only started when Infrastructure:UseRabbitMQ = true in AppHost/appsettings.json.
// Set to true when at least one tenant uses BrokerType: RabbitMq in any service's appsettings.json.
// Port 5672 is pinned so appsettings.json (RabbitMq.Host/Port) works without changes.
// WithManagementPlugin adds the browser UI at http://localhost:15672 (guest/guest in dev).
IResourceBuilder<RabbitMQServerResource>? rabbitmq = useRabbitMq
    ? builder.AddRabbitMQ("rabbitmq", port: 5672)
             .WithLifetime(ContainerLifetime.Persistent)
             .WithManagementPlugin()
    : null;

// Seq — unified structured log store for all services.
// UI available at http://localhost:5341. Free for local single-user use.
// Services receive ConnectionStrings__seq via Aspire; SerilogSetupExtensions picks it up automatically.
IResourceBuilder<SeqResource>? seq = useSeq
    ? builder.AddSeq("seq").WithLifetime(ContainerLifetime.Persistent)
    : null;

// Shell.Api — uses Redis (cache + distributed lock + outbox relay) and optionally RabbitMQ (outbox relay).
var shell = builder.AddProject<Projects.Nova_Shell_Api>("shell")
                   .WithEnvironment("ENCRYPTION_KEY", encryptionKey);
if (redis    is not null) shell.WithReference(redis).WaitFor(redis);
if (rabbitmq is not null) shell.WaitFor(rabbitmq);
if (seq      is not null) shell.WithReference(seq).WaitFor(seq);

// ToDo.Api — uses Redis (cache + distributed lock + outbox relay).
var todo = builder.AddProject<Projects.Nova_ToDo_Api>("todo")
                  .WithEnvironment("ENCRYPTION_KEY", encryptionKey);
if (redis is not null) todo.WithReference(redis).WaitFor(redis);
if (seq   is not null) todo.WithReference(seq).WaitFor(seq);

// CommonUX.Api — uses Redis (session store + cache).
var commonux = builder.AddProject<Projects.Nova_CommonUX_Api>("commonux")
                      .WithEnvironment("ENCRYPTION_KEY", encryptionKey);
if (redis is not null) commonux.WithReference(redis).WaitFor(redis);
if (seq   is not null) commonux.WithReference(seq).WaitFor(seq);

// Presets.Api — no Redis or RabbitMQ dependency (caching wired but disabled in this service).
var presets = builder.AddProject<Projects.Nova_Presets_Api>("presets")
                     .WithEnvironment("ENCRYPTION_KEY", encryptionKey);
if (seq is not null) presets.WithReference(seq).WaitFor(seq);

builder.Build().Run();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/// <summary>
/// Returns <c>true</c> if the Docker CLI can reach the Docker daemon.
/// Times out after 5 seconds so startup is not delayed significantly.
/// </summary>
static bool IsDockerAvailable()
{
    try
    {
        using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName               = "docker",
            Arguments              = "info",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        });
        proc?.WaitForExit(5_000);
        return proc?.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}
