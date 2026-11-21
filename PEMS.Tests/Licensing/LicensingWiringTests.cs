using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PEMS.Licensing;
using PEMS.Licensing.Evaluation;
using PEMS.Web.Licensing;

namespace PEMS.Tests.Licensing;

/// <summary>The real DI registration (AddPemsLicensing) with the settings the env files provide.</summary>
public class LicensingWiringTests
{
    private static ServiceProvider Build(bool enforce, string sealDirectory)
    {
        var config = new ConfigurationManager();
        config["Licensing:Enforce"] = enforce ? "true" : "false";
        config["Licensing:SealDirectory"] = sealDirectory;
        config["Licensing:PeriodicCheckHours"] = "6";
        config["Licensing:HeartbeatHours"] = "24";
        config["Licensing:ClockToleranceMinutes"] = "5";
        config["Licensing:WarningDays"] = "30";
        config["Licensing:RestoreGraceHours"] = "72";
        config["Licensing:HostMachineIdPath"] = "/host/etc/machine-id";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPemsLicensing(config);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = true });
    }

    [Fact]
    public async Task With_the_flag_off_the_startup_service_does_nothing_and_writes_nothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pems-wire-" + Guid.NewGuid().ToString("N"));
        using var sp = Build(false, dir);
        var startup = sp.GetServices<IHostedService>().OfType<LicenseStartupService>().Single();
        await startup.StartAsync(CancellationToken.None);

        Assert.Equal(LicenseState.NotEnforced, sp.GetRequiredService<LicenseStateHolder>().Current.State);
        Assert.False(Directory.Exists(dir));
        Assert.False(sp.GetRequiredService<LicensingSettings>().Enforce);
    }

    [Fact]
    public async Task With_the_flag_on_and_no_licence_the_startup_service_sets_activation_required()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pems-wire-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var sp = Build(true, dir);
            var startup = sp.GetServices<IHostedService>().OfType<LicenseStartupService>().Single();
            await startup.StartAsync(CancellationToken.None);   // the database store is unreachable here: treated as "not a verdict"
            Assert.Equal(LicenseState.ActivationRequired, sp.GetRequiredService<LicenseStateHolder>().Current.State);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_missing_licensing_key_stops_the_start_like_every_other_required_setting()
    {
        var config = new ConfigurationManager();
        config["Licensing:Enforce"] = "false";
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddPemsLicensing(config));
    }
}
