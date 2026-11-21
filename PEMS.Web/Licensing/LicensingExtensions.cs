using PEMS.Licensing;
using PEMS.Licensing.Crypto;
using PEMS.Licensing.Fingerprint;
using PEMS.Licensing.Seal;
using PEMS.Persistence.Licensing;
using PEMS.Web.Configuration;

namespace PEMS.Web.Licensing;

public static class LicensingExtensions
{
    /// <summary>Reads <c>Licensing:*</c> (all Required*, defined in every env/*.env) and registers the single licensing engine.</summary>
    public static IServiceCollection AddPemsLicensing(this IServiceCollection services, IConfiguration configuration)
    {
        var assembly = typeof(LicensingExtensions).Assembly;
        var buildDate = File.GetLastWriteTimeUtc(assembly.Location).ToString("yyyy-MM-dd");
        var version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        var settings = new LicensingSettings(
            configuration.RequiredBool("Licensing:Enforce"),
            configuration.Required("Licensing:SealDirectory"),
            configuration.RequiredInt("Licensing:PeriodicCheckHours"),
            configuration.RequiredInt("Licensing:HeartbeatHours"),
            configuration.RequiredInt("Licensing:ClockToleranceMinutes"),
            configuration.RequiredInt("Licensing:WarningDays"),
            configuration.RequiredInt("Licensing:RestoreGraceHours"),
            configuration.Required("Licensing:HostMachineIdPath"),
            version, buildDate);

        services.AddSingleton(settings);
        services.AddSingleton(TrustedKeySet.LoadEmbedded());
        services.AddSingleton<LicenseStateHolder>();

        var directory = settings.ResolveSealDirectory();
        services.AddSingleton<IInstallKeyStore>(OperatingSystem.IsWindows() ? new CngInstallKeyStore() : new FileInstallKeyStore(directory));
        services.AddSingleton(sp => new SealService(
            new ISealStore[] { new DbSealStore(sp.GetRequiredService<IServiceScopeFactory>()), new FileSealStore(directory) },
            // The file sink is first on purpose: it is authoritative and works before the database exists (design 4.4).
            new ILicenseEventSink[] { new FileEventSink(directory), new DbEventSink(sp.GetRequiredService<IServiceScopeFactory>()) },
            sp.GetRequiredService<IInstallKeyStore>(), TimeProvider.System));
        services.AddSingleton(sp => new LicenseManager(settings, sp.GetRequiredService<SealService>(), sp.GetRequiredService<IInstallKeyStore>(),
            () => OperatingSystem.IsWindows() ? new WindowsSignalSource() : new LinuxSignalSource("/", settings.HostMachineIdPath),
            sp.GetRequiredService<TrustedKeySet>()));
        services.AddSingleton<LicensePeriodicJob>();
        services.AddHostedService<LicenseStartupService>();
        return services;
    }

    /// <summary>Adds V2 and schedules V3 only when enforcement is on, so a developer machine with Enforce=false runs exactly as before.</summary>
    public static WebApplication UsePemsLicensing(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<LicensingSettings>();
        if (!settings.Enforce) return app;

        app.UseMiddleware<LicenseGateMiddleware>();
        Hangfire.RecurringJob.AddOrUpdate<LicensePeriodicJob>("licensing-periodic-check", j => j.RunAsync(),
            Hangfire.Cron.HourInterval(settings.PeriodicCheckHours));
        return app;
    }
}
