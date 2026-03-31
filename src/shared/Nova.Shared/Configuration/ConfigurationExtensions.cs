using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nova.Shared.Security;

namespace Nova.Shared.Configuration;

/// <summary>Extension methods for wiring Nova configuration into the host builder.</summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Adds both configuration sources, binds strongly-typed settings,
    /// registers <see cref="OpsSettingsWatcher"/>, and registers <see cref="ICipherService"/>.
    /// </summary>
    public static IHostApplicationBuilder AddNovaConfiguration(this IHostApplicationBuilder builder)
    {
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddJsonFile("opsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables();

        builder.Services.Configure<AppSettings>(builder.Configuration);
        builder.Services.Configure<OpsSettings>(builder.Configuration);

        builder.Services.AddSingleton<ICipherService, CipherService>();
        builder.Services.AddSingleton<OpsSettingsValidator>();
        builder.Services.AddSingleton<OpsSettingsWatcher>();
        builder.Services.AddSingleton<IOpsSettingsAccessor, OpsSettingsAccessor>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<OpsSettingsWatcher>());

        return builder;
    }
}
