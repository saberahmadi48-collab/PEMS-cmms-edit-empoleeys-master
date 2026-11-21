using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.WorkflowPlatform.Services;

/// <summary>SCRUM-103: in-memory registry of module contracts, filled once at startup (singleton). One contract per (Module, Category).</summary>
public class WorkflowModuleContractRegistry : IWorkflowModuleContractRegistry
{
    private readonly Dictionary<(string Module, string Category), WorkflowModuleContract> _contracts = new();

    public void Register(WorkflowModuleContract contract)
    {
        var key = (contract.Module, contract.Category);
        if (_contracts.ContainsKey(key))
            throw new InvalidOperationException($"A workflow contract for ({contract.Module}, {contract.Category}) is already registered.");
        _contracts[key] = contract;
    }

    public WorkflowModuleContract? Find(string module, string category)
        => _contracts.GetValueOrDefault((module ?? "", category ?? ""));

    public IReadOnlyList<WorkflowModuleContract> All()
        => _contracts.Values.OrderBy(c => c.Module, StringComparer.Ordinal).ThenBy(c => c.Category, StringComparer.Ordinal).ToList();
}
