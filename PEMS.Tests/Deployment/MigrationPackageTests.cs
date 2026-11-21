using System.Security.Cryptography;
using System.Text;
using PEMS.Application.Migration;

namespace PEMS.Tests.Deployment;

/// <summary>SCRUM-581: a package of each kind is classified by the detector, and an unknown, unsigned, unlisted or tampered package is refused. Keys are made in the test; nothing is read from a file.</summary>
public sealed class MigrationPackageTests : IDisposable
{
    private readonly ECDsa _vendor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private TrustedKeys Trust => new(new Dictionary<string, string> { ["vendor-1"] = Convert.ToBase64String(_vendor.ExportSubjectPublicKeyInfo()) });

    public void Dispose() => _vendor.Dispose();

    private static Dictionary<string, byte[]> Files() => new() { ["data/departments.json"] = Encoding.UTF8.GetBytes("[{\"code\":\"D1\"}]") };
    private static PackageDataSet[] Data() => [new("Department", "append", "data/departments.json", 1)];

    private byte[] Build(PackageKind kind, string? from = null, string to = "1.1.0") =>
        MigrationPackageBuilder.Build(kind, from, to, "20261119190301_Scrum591DefinitionGrants", ["page/core/departments"], Data(), Files(), "vendor-1", _vendor);

    private static DatabaseState Empty() => new(true, null, null, new HashSet<string>());
    private static DatabaseState Installed(string version = "1.0.0", params string[] fed) => new(false, version, "20261119190301_Scrum591DefinitionGrants", fed.ToHashSet());

    private PackagePlan Plan(byte[] package, DatabaseState db)
    {
        var (manifest, _, hash) = MigrationPackage.Read(package, Trust);
        return MigrationPackage.Detect(manifest, hash, db);
    }

    [Fact]
    public void An_install_package_installs_an_empty_database_and_is_refused_on_a_used_one()
    {
        Assert.Equal(PackageAction.Install, Plan(Build(PackageKind.Install), Empty()).Action);
        Assert.Equal(PackageAction.Refused, Plan(Build(PackageKind.Install), Installed()).Action);
    }

    [Fact]
    public void An_update_needs_its_from_version_and_a_newer_target()
    {
        Assert.Equal(PackageAction.Update, Plan(Build(PackageKind.Update, "1.0.0", "1.1.0"), Installed("1.0.0")).Action);
        Assert.Equal(PackageAction.Refused, Plan(Build(PackageKind.Update, "1.0.0", "1.1.0"), Installed("0.9.0")).Action);   // wrong version in the database
        Assert.Equal(PackageAction.Refused, Plan(Build(PackageKind.Update, "1.0.0", "1.0.0"), Installed("1.0.0")).Action);   // not newer
        Assert.Equal(PackageAction.Refused, Plan(Build(PackageKind.Update, "1.0.0", "1.1.0"), Empty()).Action);              // nothing to update
    }

    [Fact]
    public void A_feed_is_a_feed_the_first_time_and_a_refeed_when_its_hash_was_fed_before()
    {
        var package = Build(PackageKind.Feed);
        var hash = MigrationPackage.Read(package, Trust).PackageHash;
        Assert.Equal(PackageAction.Feed, Plan(package, Installed()).Action);
        Assert.Equal(PackageAction.Refeed, Plan(package, Installed("1.0.0", hash)).Action);
        Assert.Equal(PackageAction.Refused, Plan(package, Empty()).Action);
    }

    [Fact]
    public void A_package_signed_by_a_key_nobody_trusts_is_refused()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var package = MigrationPackageBuilder.Build(PackageKind.Feed, null, "1.1.0", null, [], Data(), Files(), "vendor-1", other);   // same key id, wrong key
        var ex = Assert.Throws<PackageRefusedException>(() => MigrationPackage.Read(package, Trust));
        Assert.Contains("signature", ex.Message);
        var unknown = MigrationPackageBuilder.Build(PackageKind.Feed, null, "1.1.0", null, [], Data(), Files(), "someone-else", _vendor);
        Assert.Contains("not trusted", Assert.Throws<PackageRefusedException>(() => MigrationPackage.Read(unknown, Trust)).Message);
    }

    [Fact]
    public void A_changed_file_a_missing_file_and_an_unlisted_file_are_refused()
    {
        var good = Build(PackageKind.Feed);
        Assert.Contains("checksum", Assert.Throws<PackageRefusedException>(() => MigrationPackage.Read(Rewrite(good, z => z["data/departments.json"] = Encoding.UTF8.GetBytes("[{\"code\":\"HACKED\"}]")), Trust)).Message);
        Assert.Contains("missing", Assert.Throws<PackageRefusedException>(() => MigrationPackage.Read(Rewrite(good, z => z.Remove("data/departments.json")), Trust)).Message);
        Assert.Contains("not listed", Assert.Throws<PackageRefusedException>(() => MigrationPackage.Read(Rewrite(good, z => z["extra.sql"] = Encoding.UTF8.GetBytes("drop table x")), Trust)).Message);
    }

    [Fact]
    public void A_changed_manifest_breaks_the_signature_and_a_non_zip_is_refused()
    {
        var good = Build(PackageKind.Update, "1.0.0", "1.1.0");
        var changed = Rewrite(good, z => z["manifest.json"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(z["manifest.json"]).Replace("\"1.1.0\"", "\"9.9.9\"")));
        Assert.Contains("signature", Assert.Throws<PackageRefusedException>(() => MigrationPackage.Read(changed, Trust)).Message);
        Assert.Contains("not a package", Assert.Throws<PackageRefusedException>(() => MigrationPackage.Read(Encoding.UTF8.GetBytes("hello"), Trust)).Message);
    }

    [Theory]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("2.0.0", "10.0.0", -1)]
    public void Versions_compare_by_number(string a, string b, int sign) => Assert.Equal(sign, Math.Sign(MigrationPackage.CompareVersions(a, b)));

    /// <summary>Reads the zip, lets the test change its files, and writes a zip again with the same manifest bytes unless the test replaced them.</summary>
    private static byte[] Rewrite(byte[] zipBytes, Action<Dictionary<string, byte[]>> change)
    {
        var files = new Dictionary<string, byte[]>();
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(zipBytes)))
            foreach (var e in zip.Entries) { using var s = e.Open(); using var ms = new MemoryStream(); s.CopyTo(ms); files[e.FullName] = ms.ToArray(); }
        change(files);
        using var output = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create, true))
            foreach (var f in files) { using var s = zip.CreateEntry(f.Key).Open(); s.Write(f.Value); }
        return output.ToArray();
    }
}
