using PEMS.Licensing.Evaluation;

namespace PEMS.Web.Licensing;

/// <summary>Cached result of the last full check, read by the request middleware in O(1) (verification point V2).</summary>
public sealed class LicenseStateHolder
{
    private volatile LicenseStatus _current = LicenseStatus.NotEnforced;

    public LicenseStatus Current => _current;

    public void Set(LicenseStatus status) => _current = status;
}
