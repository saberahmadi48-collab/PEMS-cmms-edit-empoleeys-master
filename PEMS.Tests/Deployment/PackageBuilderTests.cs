using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PEMS.Application.Migration;
using PEMS.Web.Services;

namespace PEMS.Tests.Deployment;

/// <summary>SCRUM-584: the vendor panel builds a signed feed package the migrator accepts, refuses without a signing key, and a changed copy is refused. The key is made in the test.</summary>
public sealed class PackageBuilderTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose() => _key.Dispose();

    private PackageBuilder Builder(bool withKey)
    {
        var settings = new Dictionary<string, string?>();
        if (withKey)
        {
            settings["Migrator:SigningKey"] = Convert.ToBase64String(_key.ExportPkcs8PrivateKey());
            settings["Migrator:SignerKeyId"] = "vendor-1";
        }
        return new PackageBuilder(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), null!, NullLogger<PackageBuilder>.Instance);
    }

    private TrustedKeys Trust => new(new Dictionary<string, string> { ["vendor-1"] = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()) });

    [Fact]
    public async Task A_feed_package_built_in_the_panel_is_read_by_the_migrator()
    {
        var result = await Builder(true).BuildFeedAsync("1.1.0", "Department", "append", "[{\"Code\":\"D1\",\"Name\":\"One\"}]");
        Assert.True(result.Success, result.Error);
        Assert.EndsWith(".zip", result.FileName);
        var (manifest, files, hash) = MigrationPackage.Read(result.Bytes!, Trust);
        Assert.Equal(PackageKind.Feed, manifest.Kind);
        Assert.Equal(1, manifest.DataSets[0].Rows);
        Assert.Single(files);
        Assert.Equal(PackageAction.Feed, MigrationPackage.Detect(manifest, hash, new DatabaseState(false, "1.0.0", null, new HashSet<string>())).Action);
    }

    [Fact]
    public async Task Without_a_signing_key_every_build_is_refused()
    {
        var result = await Builder(false).BuildFeedAsync("1.1.0", "Department", "append", "[{\"Code\":\"D1\"}]");
        Assert.False(result.Success);
        Assert.Contains("no vendor signing key", result.Error);
    }

    [Theory]
    [InlineData("delete", "[{\"Code\":\"D1\"}]")]
    [InlineData("append", "not json")]
    [InlineData("append", "[]")]
    [InlineData("append", "[1,2]")]
    public async Task A_bad_mode_or_bad_rows_are_refused_before_anything_is_signed(string mode, string rows)
    {
        var result = await Builder(true).BuildFeedAsync("1.1.0", "Department", mode, rows);
        Assert.False(result.Success);
        Assert.Null(result.Bytes);
    }

    [Fact]
    public async Task A_package_with_a_changed_row_is_refused_by_the_migrator()
    {
        var built = (await Builder(true).BuildFeedAsync("1.1.0", "Department", "append", "[{\"Code\":\"D1\"}]")).Bytes!;
        using var input = new MemoryStream(built);
        using var output = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(input))
        using (var changed = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create, true))
            foreach (var e in zip.Entries)
            {
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                var bytes = e.FullName.StartsWith("data/") ? System.Text.Encoding.UTF8.GetBytes("[{\"Code\":\"HACKED\"}]") : ms.ToArray();
                using var w = changed.CreateEntry(e.FullName).Open();
                w.Write(bytes);
            }
        Assert.Throws<PackageRefusedException>(() => MigrationPackage.Read(output.ToArray(), Trust));
    }
}
