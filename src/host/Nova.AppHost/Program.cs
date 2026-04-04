var builder = DistributedApplication.CreateBuilder(args);

// Redis — Aspire pulls the container image automatically (requires Docker).
// Connection string is injected into services that call AddRedisClient("redis").
var redis = builder.AddRedis("redis");

builder.AddProject<Projects.Nova_Shell_Api>("shell")
       .WithReference(redis)
       .WaitFor(redis);

builder.Build().Run();
