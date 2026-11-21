using PEMS.Shared.Security;

namespace PEMS.Tests.Security;

/// <summary>SCRUM-17: accounts created on someone's behalf never start with a known or hard-coded password.</summary>
public class TemporaryPasswordGeneratorTests
{
    [Fact]
    public void Generate_returns_a_policy_proof_password()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = TemporaryPasswordGenerator.Generate();
            Assert.Equal(TemporaryPasswordGenerator.Length, password.Length);
            Assert.True(password.Any(c => char.IsUpper(c)));
            Assert.True(password.Any(c => char.IsLower(c)));
            Assert.True(password.Any(c => char.IsDigit(c)));
            Assert.True(password.Any(c => !char.IsLetterOrDigit(c)));
        }
    }

    [Fact]
    public void Generate_never_repeats_a_value()
    {
        var values = Enumerable.Range(0, 500).Select(_ => TemporaryPasswordGenerator.Generate()).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }
}
