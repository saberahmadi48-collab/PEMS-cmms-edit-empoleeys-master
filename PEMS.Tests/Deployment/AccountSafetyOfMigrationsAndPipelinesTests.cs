using System.Text.RegularExpressions;

namespace PEMS.Tests.Deployment;

/// <summary>
/// SCRUM-413 / #383 (part of SCRUM-410 / #380): the migration chain and the pipelines move the schema and the files only.
/// They must never insert, change or delete accounts or passwords, because they run on every update of staging, demo and client servers.
/// Database-free: scans the migration sources, the workflows and the deploy scripts.
/// </summary>
public sealed class AccountSafetyOfMigrationsAndPipelinesTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    // Data operations on the user table: InsertData/UpdateData/DeleteData with table AspNetUsers, or raw SQL that names the user table or credential columns.
    private static readonly Regex MigrationDataOnUsers = new(
        @"(?:InsertData|UpdateData|DeleteData)\s*\(\s*(?:schema:\s*""[^""]*"",\s*)?table:\s*""AspNetUsers""|\.Sql\((?:(?!\);)[\s\S])*?(?:AspNetUsers|PasswordHash|SecurityStamp)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PipelineAccountWords = new(
        @"AspNetUsers|PasswordHash|ResetPassword|UserManager|SecurityStamp",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void Detector_flags_a_migration_that_changes_user_data()
    {
        Assert.Matches(MigrationDataOnUsers, "migrationBuilder.UpdateData(\n  table: \"AspNetUsers\",\n  keyColumn: \"Id\");");
        Assert.Matches(MigrationDataOnUsers, "migrationBuilder.Sql(@\"UPDATE AspNetUsers SET PasswordHash = NULL\");");
        Assert.DoesNotMatch(MigrationDataOnUsers, "migrationBuilder.CreateTable(name: \"AspNetUsers\", columns: table => new { PasswordHash = table.Column<string>() });");
    }

    [Fact]
    public void No_migration_inserts_changes_or_deletes_accounts_or_passwords()
    {
        var folder = Path.Combine(Root(), "PEMS.Migrations", "Migrations");
        var files = Directory.GetFiles(folder, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal) && !f.EndsWith("Snapshot.cs", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(files);

        var offenders = files.Where(f => MigrationDataOnUsers.IsMatch(File.ReadAllText(f))).Select(Path.GetFileName).ToList();
        Assert.True(offenders.Count == 0, "Migrations must not touch accounts or passwords: " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_workflow_deploy_script_or_ci_script_touches_accounts_or_passwords()
    {
        var root = Root();
        var files = new List<string>();
        files.AddRange(Directory.GetFiles(Path.Combine(root, ".github", "workflows"), "*.yml"));
        foreach (var folder in new[] { "deploy", Path.Combine("scripts", "ci") })
        {
            var path = Path.Combine(root, folder);
            if (Directory.Exists(path))
                files.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")));
        }
        Assert.NotEmpty(files);

        var offenders = files.Where(f => PipelineAccountWords.IsMatch(File.ReadAllText(f))).Select(f => Path.GetRelativePath(root, f)).ToList();
        Assert.True(offenders.Count == 0, "Pipelines and deploy scripts must not touch accounts or passwords: " + string.Join(", ", offenders));
    }
}
