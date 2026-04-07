using Testcontainers.Redis;

namespace Nova.Shell.Api.Tests.Helpers;

/// <summary>
/// Manages the lifecycle of a Redis container for tests that exercise cache or locking endpoints.
/// Declare one instance per test class via <see cref="IAsyncLifetime"/> — never share across classes.
/// </summary>
/// <example>
/// <code>
/// public class TestCacheEndpointTests : IAsyncLifetime
/// {
///     private readonly RedisFixture _redis = new();
///
///     public async Task InitializeAsync() => await _redis.InitializeAsync();
///     public async Task DisposeAsync()    => await _redis.DisposeAsync();
///
///     [Fact]
///     public async Task Given_RedisAvailable_When_TestCacheGetIsCalled_Then_Returns200()
///     {
///         // Arrange
///         var client = TestHost.CreateWithRedis(_redis.ConnectionString).CreateClient();
///         ...
///     }
/// }
/// </code>
/// </example>
public sealed class RedisFixture : IAsyncLifetime
{
    public RedisContainer Container { get; } = new RedisBuilder().Build();

    /// <summary>Connection string for the running Redis container.</summary>
    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync() => await Container.StartAsync();
    public async Task DisposeAsync()    => await Container.DisposeAsync();
}
