using Microsoft.Extensions.Caching.Memory;
using PEMS.Application.Organization;
using PEMS.Web.Services;

namespace PEMS.Tests.Organization;

/// <summary>SCRUM-640: validate keeps a clean file for the user, confirm commits from it once, a rejected file keeps nothing.</summary>
public class OrganizationImportPageServiceTests
{
    private sealed class FakeImport(Func<bool, OrganizationImportResult> run) : IOrganizationImportService
    {
        public int Commits;
        public Task<OrganizationImportResult> ImportAsync(Stream xlsx, bool commit, CancellationToken ct = default) { if (commit) Commits++; return Task.FromResult(run(commit)); }
        public Task<byte[]> BuildTemplateAsync(CancellationToken ct = default) => Task.FromResult(new byte[] { 1, 2, 3 });
    }

    private static OrganizationImportResult Ok(bool commit) => new() { Committed = commit, PositionsCreated = 2 };
    private static OrganizationImportResult Bad(bool commit) { var r = new OrganizationImportResult(); r.Errors.Add("row 2: unknown parent"); return r; }

    [Fact]
    public async Task A_clean_file_is_kept_for_the_user_and_confirm_commits_it_once()
    {
        var fake = new FakeImport(Ok); var svc = new OrganizationImportPageService(fake, new MemoryCache(new MemoryCacheOptions()));
        Assert.True((await svc.ValidateAsync([1], "ali")).Success);
        Assert.Equal(2, (await svc.PendingAsync("ali")).Single().PositionsCreated);
        Assert.Empty(await svc.PendingAsync("reza"));                                    // another user sees nothing
        Assert.True((await svc.ConfirmAsync("ali")).Success);
        Assert.Equal(1, fake.Commits);
        Assert.Equal("Org_Imp_Expired", (await svc.ConfirmAsync("ali")).Error);          // the file is gone after the commit
    }

    [Fact]
    public async Task A_rejected_or_empty_file_keeps_nothing_and_confirm_has_nothing_to_commit()
    {
        var fake = new FakeImport(Bad); var svc = new OrganizationImportPageService(fake, new MemoryCache(new MemoryCacheOptions()));
        var rejected = await svc.ValidateAsync([1], "ali");
        Assert.False(rejected.Success); Assert.Equal("Org_Imp_RejectedDetail", rejected.Error); Assert.Contains("unknown parent", (string)rejected.Args![0]!);
        Assert.Equal("Org_Imp_NoFile", (await svc.ValidateAsync([], "ali")).Error);
        Assert.Equal("Org_Imp_BadFile", (await svc.ValidateAsync(new byte[5 * 1024 * 1024 + 1], "ali")).Error);
        Assert.False((await svc.ConfirmAsync("ali")).Success);
        Assert.Equal(0, fake.Commits);
    }

    [Fact]
    public async Task The_template_is_a_download_result()
    {
        var r = await new OrganizationImportPageService(new FakeImport(Ok), new MemoryCache(new MemoryCacheOptions())).TemplateAsync();
        Assert.True(r.Success); Assert.Equal("OrganizationStructureTemplate.xlsx", r.FileName); Assert.NotEmpty(r.Bytes!);
    }
}
