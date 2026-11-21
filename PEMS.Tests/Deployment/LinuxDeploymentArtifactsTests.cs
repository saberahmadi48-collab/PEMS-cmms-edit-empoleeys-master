using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PEMS.Tests.Deployment;

/// <summary>
/// ویژگی‌های ایمنی کیت استقرار Docker Compose (deploy/): بدون Secret در مخزن، Compose مخصوص pems-web با Volumeهای پایدار، Dockerfile آفلاین،
/// قوانین حذف داده‌ی کاربر از بسته، SSH کلیدی با کلید میزبان سخت‌گیر، و اجرای واقعی تست‌های Mock اسکریپت کنترلر.
/// فقط فایل‌های مخزن را می‌خوانند؛ به شبکه/سرور/دیتابیس وصل نمی‌شوند.
/// </summary>
public class LinuxDeploymentArtifactsTests
{
    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
    private static string Read(string rel) => File.ReadAllText(Path.Combine(Repo(), rel.Replace('/', Path.DirectorySeparatorChar)));

    [Theory]
    [InlineData("PEMS.Web/appsettings.json")]
    public void CommittedAppSettings_ContainNoLiteralSecrets(string file)
    {
        var text = Read(file);
        Assert.DoesNotMatch(new Regex("\"[A-Za-z_:]*(password|secret|apikey|api_key|token)[A-Za-z_]*\"\\s*:\\s*\"[^\"]+\"", RegexOptions.IgnoreCase), text);
        Assert.DoesNotMatch(new Regex(@"\b(password|pwd)\s*=\s*[^;""\s<{$]", RegexOptions.IgnoreCase), text);
    }

    [Fact]
    public void KitFiles_ContainNoCredentials_AndNoSystemdSecondDeployment()
    {
        foreach (var f in new[] { "deploy/deploy.ps1", "deploy/Build-Package.ps1", "deploy/Convert-EfScript.ps1", "deploy/remote/pems-docker.sh", "deploy/docker/compose.pems-web.yml", "deploy/docker/Dockerfile.release", "deploy/targets.example.json" })
        {
            var t = Read(f);
            Assert.DoesNotMatch(new Regex(@"\b(password|pwd)\s*=\s*[A-Za-z0-9]", RegexOptions.IgnoreCase), t.Replace("Password=<redacted>", ""));   // only the redaction placeholder may mention it
            if (!f.EndsWith("Build-Package.ps1")) Assert.DoesNotContain("-----BEGIN", t);      // the package scanner itself must mention the marker it searches for
        }
        // the VM already runs the app in Docker: the systemd kit must not come back as a competing architecture
        Assert.False(File.Exists(Path.Combine(Repo(), "deploy", "systemd", "pems.service")));
        Assert.False(File.Exists(Path.Combine(Repo(), "deploy", "remote", "pems-release.sh")));
    }

    [Fact]
    public void Compose_ManagesOnlyPemsWeb_WithPersistentVolumes_AndNoInlineSecrets()
    {
        var c = Read("deploy/docker/compose.pems-web.yml");
        Assert.Contains("name: pems-cmms-edit-empoleeys-master", c);
        Assert.Contains("container_name: pems-web", c);
        Assert.Contains("restart: unless-stopped", c);
        Assert.Contains("env_file:", c);                                            // connection string lives in the VM-local 0600 env file
        Assert.DoesNotMatch(new Regex(@"^\s*(sqlserver|caddy|ollama):", RegexOptions.Multiline), c);   // never redefines SQL Server / Caddy
        foreach (var mount in new[] { "pems-uploads:/app/wwwroot/uploads", "pems-logs:/app/logs", "pems-filestore:/app/filestore", "pems-dpkeys:/root/.aspnet/DataProtection-Keys" })
            Assert.Contains(mount, c);
        // every volume and the network are EXTERNAL: compose can never create-over or delete existing data
        Assert.Equal(4, Regex.Matches(c, @"external:\s*true").Count - 1);          // 4 volumes (+1 network)
        Assert.Contains("pems-cmms-edit-empoleeys-master_default", c);
        Assert.Contains("/health", c);
        Assert.DoesNotContain("Password", c, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseDockerfile_IsOffline_AndWiresLinuxOcr()
    {
        var d = Read("deploy/docker/Dockerfile.release");
        Assert.Contains("FROM ${BASE_IMAGE}", d);                                  // a local image only; nothing is pulled
        Assert.DoesNotMatch(new Regex(@"apt-get|apt install|curl |wget |dotnet restore|dotnet publish", RegexOptions.IgnoreCase), d);
        Assert.Contains("/app/x64/libleptonica-1.82.0.so", d);
        Assert.Contains("/app/x64/libtesseract50.so", d);
        Assert.Contains("libdl.so", d);
        Assert.Contains("OCR__TessDataPath=/usr/share/tesseract-ocr/5/tessdata", d);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"PEMS.Web.dll\"]", d);
    }

    [Fact]
    public void PackageBuilder_ExcludesUserDataDevSettingsAndWindowsOnlyBinaries_AndFailsClosed()
    {
        var b = Read("deploy/Build-Package.ps1");
        foreach (var needle in new[] { @"wwwroot\uploads", "filestore", "appsettings.Development.json", "appsettings.UAT.json", "*.pdb", "x64", "x86",
                                       "Package verification failed", "possible literal password", "release-manifest.json", "SHA256", "System32", "tar.exe" })
            Assert.Contains(needle, b);
    }

    [Fact]
    public void EntryPoint_UsesKeyBasedSshWithStrictHostKeys_NeverStoresPasswords_AndKeepsMigrationsExplicit()
    {
        var d = Read("deploy/deploy.ps1");
        Assert.Contains("BatchMode=yes", d);
        Assert.Contains("StrictHostKeyChecking=yes", d);
        Assert.DoesNotContain("StrictHostKeyChecking=no", d);
        Assert.DoesNotContain("sshpass", d, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"-Password\b|--password|PlainText|ConvertTo-SecureString", RegexOptions.IgnoreCase), d);
        Assert.Contains("-ConfirmReviewed", d);                                     // migrations: only via an explicit, reviewed action
        Assert.Contains("Convert-EfScript.ps1", d);
        Assert.Contains("targets.local.json", Read(".gitignore"));
        // the Deploy flow itself never calls db-apply / migrate
        var deployFlow = d[d.IndexOf("# ---------------------------------------------------------------------------------------------- Deploy", StringComparison.Ordinal)..];
        Assert.DoesNotContain("db-apply", deployFlow);
        Assert.DoesNotContain("migrations script", deployFlow);
    }

    [Fact]
    public void Controller_NeverRunsDestructiveDockerCommands_OrMigrations_AndIsLfOnly()
    {
        var s = Read("deploy/remote/pems-docker.sh");
        Assert.DoesNotContain('\r', s);
        Assert.DoesNotMatch(new Regex(@"dotnet ef|database update", RegexOptions.IgnoreCase), s);
        Assert.Contains("refused destructive docker command", s);
        Assert.Contains("--pull=false", s);                                         // offline build
        Assert.Contains("--no-deps --no-build --force-recreate", s);
        Assert.Contains("-C -b -I -d PEMSDB", s);                                   // sqlcmd: abort on error + QUOTED_IDENTIFIER ON
        Assert.Contains("manifest check inconclusive", s);                          // fails closed
        Assert.Contains("--confirm-reviewed", s);
        foreach (var f in new[] { "deploy/docker/compose.pems-web.yml", "deploy/docker/Dockerfile.release", "deploy/remote/pems-docker.sh" })
            Assert.DoesNotContain('\r', Read(f));
    }

    [Fact]
    public void GitAttributes_CheckOutEveryLinuxFileWithLf()
    {
        // SCRUM-534: on Windows `text=auto` turned the docker files into CRLF in the working copy, so the LF check above failed there
        var a = Read(".gitattributes");
        foreach (var rule in new[] { "deploy/remote/*.sh text eol=lf", "deploy/docker/* text eol=lf", "deploy/tests/*.sh text eol=lf", "deploy/tests/fakes/* text eol=lf" })
            Assert.Contains(rule, a);
    }

    [Theory]
    [InlineData("graphify-out/")]
    [InlineData(".claude/")]
    [InlineData("tools/loadtest/results/")]
    [InlineData("**/logs/")]
    [InlineData("PEMS.Web/filestore/")]   // uploaded files are runtime data, never baked into an image
    [InlineData(".kilo/")]
    [InlineData("**/bin/")]
    [InlineData(".git/")]
    public void DockerIgnore_KeepsLocalToolOutputOutOfTheBuildContext(string entry)
    {
        // SCRUM-532: graphify-out (GBs) in the context made `docker build` fail at COPY . .
        var lines = Read(".dockerignore").Split('\n').Select(l => l.Trim()).ToList();
        Assert.Contains(entry, lines);
    }

    [Theory]
    [InlineData("tools/loadtest/pems-api-baseline.js")]
    [InlineData("tools/loadtest/pems-baseline.js")]
    public void LoadTestScripts_KeepTheLoginCookieAcrossIterations(string script)
    {
        // SCRUM-535: both scripts log in once per VU; without this k6 clears the cookie jar each iteration and every later request gets 401
        Assert.Matches(new System.Text.RegularExpressions.Regex(@"export const options = \{\s*(//[^\n]*\n\s*)*noCookiesReset: true,"), Read(script));
    }

    [Fact]
    public void EfScriptConverter_WrapsOnlyRawSqlStatementsInExec()
    {
        var c = Read("deploy/Convert-EfScript.ps1");
        Assert.Contains("EXEC(N'", c);
        Assert.Contains("Replace(\"'\", \"''\")", c);                               // single quotes doubled => identical semantics
        Assert.Contains("UPDATE", c);
        Assert.Contains("WITH\\s+(NO)?CHECK", c);
    }

    [Fact]
    public void Dockerfile_RestoreLayer_ListsEveryProjectReferencedByTheWebProject()
    {
        // regression: PEMS.WorkflowPlatform was referenced by PEMS.Web but missing from the restore layer (also used by the Azure UAT build)
        var docker = Read("Dockerfile");
        var web = Read("PEMS.Web/PEMS.Web.csproj");
        foreach (Match m in Regex.Matches(web, @"ProjectReference Include=""\.\.\\(PEMS\.[A-Za-z]+)\\"))
            Assert.Contains($"COPY {m.Groups[1].Value}/*.csproj", docker);
    }

    [Fact]
    public void AzureUat_ConfigurationIsUntouchedByTheLinuxKit()
    {
        var wf = Read(".github/workflows/deploy-uat.yml");
        Assert.Contains("az containerapp up", wf);
        Assert.Contains("--source .", wf);                                          // still builds the root Dockerfile
        Assert.DoesNotContain("deploy/", wf);                                       // the Linux kit is not wired into the Azure workflow
        Assert.Contains("ASPNETCORE_ENVIRONMENT=UAT", wf);
    }

    [Fact]
    public void ControllerScript_PassesItsMockBasedHarness()
    {
        // runs deploy/tests/test-pems-docker.sh (fake docker/curl/df) when a bash is available (Git Bash on Windows, bash on Linux)
        var bash = new[] { "bash", @"C:\Program Files\Git\bin\bash.exe", @"C:\Program Files\Git\usr\bin\bash.exe", "/bin/bash", "/usr/bin/bash" }
            .FirstOrDefault(p => { try { using var pr = Process.Start(new ProcessStartInfo(p, "--version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true }); pr!.WaitForExit(5000); return pr.ExitCode == 0; } catch { return false; } });
        Assert.True(bash != null, "bash is required to run the deployment-script harness (Git Bash on Windows)");
        var psi = new ProcessStartInfo(bash!, "deploy/tests/test-pems-docker.sh") { WorkingDirectory = Repo(), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(240000);
        Assert.True(p.ExitCode == 0, "harness failed:\n" + string.Join('\n', output.Split('\n').Where(l => l.Contains("[FAIL]") || l.Contains("RESULT"))));
        Assert.Matches(@"RESULT: \d+ passed, 0 failed", output);
    }
}
