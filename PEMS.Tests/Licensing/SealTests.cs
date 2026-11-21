using PEMS.Licensing.Fingerprint;
using PEMS.Licensing.Seal;

namespace PEMS.Tests.Licensing;

public class SealTests
{
    private static async Task<(CustomerInstall Install, SealRecord Record)> NewSeal()
    {
        var install = new CustomerInstall(new PEMS.Licensing.Crypto.TrustedKeySet(Array.Empty<PEMS.Licensing.Crypto.TrustedKey>()));
        var record = await install.Seal.CreateAsync(FingerprintCollector.Collect(install.Signals));
        return (install, record);
    }

    [Fact]
    public async Task Created_seal_is_written_to_every_store_and_loads_ok()
    {
        var (i, record) = await NewSeal();
        using var _ = i;
        Assert.NotNull(i.Db.Content);
        Assert.True(File.Exists(Path.Combine(i.Dir, "install.seal")));
        var check = await i.Seal.LoadAsync();
        Assert.Equal(SealStatus.Ok, check.Status);
        Assert.Equal(record.InstallId, check.Record!.InstallId);
        Assert.Matches(@"^PEMS-[0-9A-Z]{5}(-[0-9A-Z]{5}){4}$", record.InstallId);
    }

    [Fact]
    public async Task Each_start_raises_the_counter_in_all_stores()
    {
        var (i, record) = await NewSeal();
        using var _ = i;
        var next = await i.Seal.RecordStartAsync(record, i.Time.GetUtcNow(), null, "start");
        Assert.Equal(record.Counter + 1, next.Counter);
        var check = await i.Seal.LoadAsync();
        Assert.Equal(SealStatus.Ok, check.Status);
        Assert.Equal(next.Counter, check.Record!.Counter);
    }

    [Fact]
    public async Task A_store_lagging_by_one_write_is_repaired()
    {
        var (i, record) = await NewSeal();
        using var _ = i;
        var older = i.Db.Content;                       // counter 1
        await i.Seal.RecordStartAsync(record, i.Time.GetUtcNow(), null, "start");   // counter 2 everywhere
        i.Db.Content = older;                           // the database misses the last write
        var check = await i.Seal.LoadAsync();
        Assert.Equal(SealStatus.Repaired, check.Status);
        Assert.NotEqual(older, i.Db.Content);           // healed from the file
    }

    [Fact]
    public async Task Database_restored_from_an_old_backup_is_a_suspected_restore()
    {
        var (i, record) = await NewSeal();
        using var _ = i;
        var backup = i.Db.Content;                      // taken at counter 1
        var r = record;
        for (var n = 0; n < 4; n++) r = await i.Seal.RecordStartAsync(r, i.Time.GetUtcNow(), null, "start");
        i.Db.Content = backup;                          // DB restored; the file keeps counting
        var check = await i.Seal.LoadAsync();
        Assert.Equal(SealStatus.RestoreSuspected, check.Status);
        Assert.Equal(r.Counter, check.Record!.Counter);   // the greater counter wins
    }

    [Fact]
    public async Task File_restored_from_an_old_copy_is_a_suspected_restore()
    {
        var (i, record) = await NewSeal();
        using var _ = i;
        var oldFile = await File.ReadAllTextAsync(Path.Combine(i.Dir, "install.seal"));
        var r = record;
        for (var n = 0; n < 3; n++) r = await i.Seal.RecordStartAsync(r, i.Time.GetUtcNow(), null, "start");
        await File.WriteAllTextAsync(Path.Combine(i.Dir, "install.seal"), oldFile);
        Assert.Equal(SealStatus.RestoreSuspected, (await i.Seal.LoadAsync()).Status);
    }

    [Fact]
    public async Task A_store_that_vanished_after_the_first_run_is_a_suspected_restore()
    {
        var (i, record) = await NewSeal();
        using var _ = i;
        var r = record;
        for (var n = 0; n < 5; n++) r = await i.Seal.RecordStartAsync(r, i.Time.GetUtcNow(), null, "start");
        i.Db.Content = null;                            // a database restored from before activation
        Assert.Equal(SealStatus.RestoreSuspected, (await i.Seal.LoadAsync()).Status);
    }

    [Fact]
    public async Task A_database_that_is_late_in_the_first_run_is_created_not_suspected()
    {
        var (i, _) = await NewSeal();
        using var __ = i;
        i.Db.Content = null;                            // counter is still 1: first-run artefact
        var check = await i.Seal.LoadAsync();
        Assert.Equal(SealStatus.Repaired, check.Status);
        Assert.NotNull(i.Db.Content);
    }

    [Fact]
    public async Task An_unavailable_database_is_not_a_verdict()
    {
        var (i, _) = await NewSeal();
        using var __ = i;
        i.Db.Unavailable = true;
        var check = await i.Seal.LoadAsync();
        Assert.Equal(SealStatus.Ok, check.Status);
        Assert.Contains(check.Notes, n => n.EndsWith("unavailable"));
    }

    [Fact]
    public async Task A_tampered_store_is_invalid()
    {
        var (i, _) = await NewSeal();
        using var __ = i;
        i.Db.Content = TamperSeal(i.Db.Content!);
        Assert.Equal(SealStatus.Invalid, (await i.Seal.LoadAsync()).Status);
    }

    private static string TamperSeal(string bundleJson)
    {
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower };
        var bundle = System.Text.Json.JsonSerializer.Deserialize<SealBundle>(bundleJson, opts)!;
        var m = System.Text.RegularExpressions.Regex.Match(bundle.Seal, "\"payload\": \"(.)");
        var flipped = m.Groups[1].Value == "A" ? "B" : "A";
        var seal = bundle.Seal.Remove(m.Groups[1].Index, 1).Insert(m.Groups[1].Index, flipped);
        return System.Text.Json.JsonSerializer.Serialize(bundle with { Seal = seal }, opts);
    }

    [Fact]
    public async Task Seal_copied_to_other_hardware_without_the_install_key_is_invalid()
    {
        var (i, _) = await NewSeal();
        using var __ = i;
        File.Delete(Path.Combine(i.Dir, "install.key"));   // the machine-bound key does not travel with the copy
        Assert.Equal(SealStatus.Invalid, (await i.Seal.LoadAsync()).Status);
    }

    [Fact]
    public async Task Event_chain_is_valid_and_detects_edits()
    {
        var (i, record) = await NewSeal();
        using var _ = i;
        var r = record;
        for (var n = 0; n < 3; n++) r = await i.Seal.RecordStartAsync(r, i.Time.GetUtcNow(), null, "start");
        var entries = i.Events.ReadAll().ToList();
        Assert.Equal(4, entries.Count);
        Assert.True(EventChain.IsValid(entries));
        entries[1] = entries[1] with { Type = "forged" };
        Assert.False(EventChain.IsValid(entries));
        Assert.Equal(entries[^1].EntryHash, r.AuditHead);
    }

    [Fact]
    public void Clock_guard_never_goes_backwards_and_flags_a_jump()
    {
        var last = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);
        var back = ClockGuard.Evaluate(last.AddDays(-30), last, null, TimeSpan.FromMinutes(5));
        Assert.True(back.BackwardJump);
        Assert.Equal(last, back.EffectiveNow);
        var small = ClockGuard.Evaluate(last.AddMinutes(-2), last, null, TimeSpan.FromMinutes(5));
        Assert.False(small.BackwardJump);
        var forward = ClockGuard.Evaluate(last.AddDays(3), last, null, TimeSpan.FromMinutes(5));
        Assert.Equal(last.AddDays(3), forward.EffectiveNow);
        var eventFloor = ClockGuard.Evaluate(last.AddHours(-1), last.AddDays(-5), last, TimeSpan.FromMinutes(5));
        Assert.Equal(last, eventFloor.EffectiveNow);   // the newest audit event raises the floor
    }
}
