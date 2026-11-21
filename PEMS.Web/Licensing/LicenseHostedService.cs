using PEMS.Licensing;

namespace PEMS.Web.Licensing;

/// <summary>Verification point V1: the full check at startup, before the application serves requests. No effect when Enforce is false.</summary>
public sealed class LicenseStartupService : IHostedService
{
    private readonly LicenseManager _manager;
    private readonly LicenseStateHolder _state;
    private readonly ILogger<LicenseStartupService> _log;

    public LicenseStartupService(LicenseManager manager, LicenseStateHolder state, ILogger<LicenseStartupService> log)
    {
        _manager = manager;
        _state = state;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_manager.Settings.Enforce)
        {
            _log.LogInformation("Licensing: enforcement is off (Licensing:Enforce=false); nothing is checked or blocked.");
            return;
        }
        try
        {
            var status = await _manager.CheckAsync(CheckReason.Startup, ct);
            _state.Set(status);
            _log.LogInformation("Licensing: startup check finished, state {State}, reasons {Reasons}.", status.State, string.Join(",", status.Reasons));
        }
        catch (Exception ex)
        {
            // Fail restricted, not dead (design 7): an unreadable seal must not crash the host.
            _state.Set(new PEMS.Licensing.Evaluation.LicenseStatus(PEMS.Licensing.Evaluation.LicenseState.ActivationRequired,
                new[] { PEMS.Licensing.Evaluation.LicenseReasons.SealInvalid }, null, null, null, null, null, null, null, null));
            _log.LogError(ex, "Licensing: startup check failed; the application starts in activation-required mode.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Verification point V3: re-runs the full check on a Hangfire schedule (registered only when Enforce is true).</summary>
public sealed class LicensePeriodicJob
{
    private readonly LicenseManager _manager;
    private readonly LicenseStateHolder _state;

    public LicensePeriodicJob(LicenseManager manager, LicenseStateHolder state)
    {
        _manager = manager;
        _state = state;
    }

    public async Task RunAsync()
        => _state.Set(await _manager.CheckAsync(CheckReason.Periodic));
}
