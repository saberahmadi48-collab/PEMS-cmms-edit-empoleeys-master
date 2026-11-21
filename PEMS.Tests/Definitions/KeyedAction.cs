using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>A handler that only has a key: tests that load the shipped package with their own recording handler register the other shipped handler keys with this, so the package validates.</summary>
internal sealed class KeyedAction(string key) : IActionHandler
{
    public string Key => key;
    public IReadOnlyCollection<string>? AllowedRoles => null;
    public Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct) => Task.FromResult(ActionOutcome.Done());
}
