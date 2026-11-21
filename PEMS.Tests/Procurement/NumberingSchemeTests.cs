using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Interfaces;
using PEMS.Application.Numbering;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Procurement;

/// <summary>
/// SCRUM-40: the pattern language (validation, counter scope, formatting in both calendars) and the administration service
/// (validation, versioning, the create-form field). Allocation on a real SQL Server is in <see cref="NumberingSchemeSqlTests"/>.
/// </summary>
public class NumberingSchemeTests
{
    private static NumberingScheme Scheme(string pattern, string calendar = NumberingCalendars.Gregorian, string reset = NumberingResetPeriods.Yearly, int pad = 4) =>
        new() { DocumentType = "PurchaseOrder", Pattern = pattern, Calendar = calendar, ResetPeriod = reset, PadWidth = pad };

    // 2026-10-28 is 1405-08-06 in the Solar Hijri calendar
    private static readonly DateTime Day = new(2026, 10, 28, 10, 0, 0);

    [Theory]
    [InlineData("PO-{YYYY}-{SEQ}", 4, "Jalali", "Yearly", null)]
    [InlineData("", 4, "Jalali", "Yearly", "Num_Err_PatternRequired")]
    [InlineData("PO-{YYYY}", 4, "Jalali", "Yearly", "Num_Err_SeqOnce")]
    [InlineData("PO-{SEQ}-{SEQ}-{YY}", 4, "Jalali", "Yearly", "Num_Err_SeqOnce")]
    [InlineData("PO-{MONTH}-{SEQ}", 4, "Jalali", "Never", "Num_Err_UnknownToken")]
    [InlineData("PO/{YYYY}/{SEQ}", 4, "Jalali", "Yearly", "Num_Err_Literal")]
    [InlineData("PO {SEQ}", 4, "Jalali", "Never", "Num_Err_Literal")]
    [InlineData("PO-{SEQ}", 0, "Jalali", "Never", "Num_Err_Pad")]
    [InlineData("PO-{SEQ}", 9, "Jalali", "Never", "Num_Err_Pad")]
    [InlineData("PO-{SEQ}", 4, "Lunar", "Never", "Num_Err_Calendar")]
    [InlineData("PO-{SEQ}", 4, "Jalali", "Monthly", "Num_Err_Reset")]
    [InlineData("PO-{SEQ}", 4, "Jalali", "Yearly", "Num_Err_YearlyNeedsYear")]
    [InlineData("PO-{PROJECT}-{DISC}-{SEQ}", 6, "Gregorian", "Never", null)]
    public void Validate_accepts_only_safe_patterns(string pattern, int pad, string calendar, string reset, string? error)
        => Assert.Equal(error, NumberingPattern.Validate(pattern, pad, calendar, reset));

    [Fact]
    public void Format_uses_the_scheme_calendar_codes_and_padding()
    {
        var ctx = new NumberingContext("ppd-01", "mec");
        Assert.Equal("PO-1405-0007", NumberingPattern.Format(Scheme("PO-{YYYY}-{SEQ}", NumberingCalendars.Jalali), Day, ctx, 7));
        Assert.Equal("PO-2026-0007", NumberingPattern.Format(Scheme("PO-{YYYY}-{SEQ}"), Day, ctx, 7));
        Assert.Equal("MR-PPD-01-MEC-26-012", NumberingPattern.Format(Scheme("MR-{PROJECT}-{DISC}-{YY}-{SEQ}", pad: 3), Day, ctx, 12));
        Assert.Equal("CT-X-123456", NumberingPattern.Format(Scheme("CT-{PROJECT}-{SEQ}", reset: NumberingResetPeriods.Never, pad: 2), Day, NumberingContext.Empty, 123456));
        // the create form shows the shape while the project is unknown
        Assert.Equal("PO-{PROJECT}-1405-####", NumberingPattern.Shape(Scheme("PO-{PROJECT}-{YYYY}-{SEQ}", NumberingCalendars.Jalali), Day));
    }

    [Fact]
    public void Codes_are_upper_case_and_stripped_to_the_number_alphabet()
    {
        Assert.Equal("ABC-1", NumberingPattern.Code(" abc-1 "));
        Assert.Equal("ABC", NumberingPattern.Code("a b/c"));
        Assert.Equal("X", NumberingPattern.Code("پروژه"));
        Assert.Equal("X", NumberingPattern.Code(null));
    }

    [Fact]
    public void Counter_scope_follows_the_pattern_and_the_reset_period()
    {
        var ctx = new NumberingContext("P1", "D1");
        // yearly, no codes: one counter per year
        Assert.Equal("S|1405||", NumberingPattern.ScopeKey(Scheme("PO-{YYYY}-{SEQ}", NumberingCalendars.Jalali), Day, ctx));
        // never resets: one counter, but one per project when {PROJECT} is used
        Assert.Equal("S||P1|", NumberingPattern.ScopeKey(Scheme("PO-{PROJECT}-{SEQ}", reset: NumberingResetPeriods.Never), Day, ctx));
        Assert.Equal("S|2026|P1|D1", NumberingPattern.ScopeKey(Scheme("MR-{PROJECT}-{DISC}-{YYYY}-{SEQ}"), Day, ctx));
        // a code that is not in the pattern does not split the counter
        Assert.Equal(NumberingPattern.ScopeKey(Scheme("PO-{YYYY}-{SEQ}"), Day, new NumberingContext("A", "B")),
                     NumberingPattern.ScopeKey(Scheme("PO-{YYYY}-{SEQ}"), Day, new NumberingContext("C", "D")));
    }

    [Fact]
    public async Task Save_validates_raises_the_version_and_drives_the_create_form_field()
    {
        await using var ctx = PemsDbContextFactory.CreateInMemory(nameof(Save_validates_raises_the_version_and_drives_the_create_form_field));
        var svc = new NumberingSchemeService(ctx);

        // without a scheme the form keeps the old required text box
        Assert.Equal(new DocumentNumberField(false, true, null), await svc.GetFieldAsync("PurchaseOrder"));

        Assert.Equal("Num_Err_DocumentType", await svc.SaveAsync(new("WorkOrder", "WO-{SEQ}", 4, "Jalali", "Never", true, false, null)));
        Assert.Equal("Num_Err_SeqOnce", await svc.SaveAsync(new("PurchaseOrder", "PO-{YYYY}", 4, "Jalali", "Yearly", true, false, null)));
        Assert.Empty(ctx.NumberingSchemes);

        Assert.Null(await svc.SaveAsync(new("PurchaseOrder", " PO-{YYYY}-{SEQ} ", 5, "Gregorian", "Yearly", true, false, null)));
        var saved = Assert.Single(ctx.NumberingSchemes);
        Assert.Equal("PO-{YYYY}-{SEQ}", saved.Pattern);
        Assert.Equal(1, saved.Version);

        var field = await svc.GetFieldAsync("PurchaseOrder");
        Assert.True(field.Automatic);
        Assert.False(field.AllowManualEntry);
        Assert.Equal($"PO-{DateTime.Now.Year}-00001", field.NextNumberHint);

        Assert.Null(await svc.SaveAsync(new("PurchaseOrder", "PO-{YYYY}-{SEQ}", 5, "Gregorian", "Yearly", true, true, null)));
        Assert.Equal(2, ctx.NumberingSchemes.Single().Version);
        Assert.True((await svc.GetFieldAsync("PurchaseOrder")).AllowManualEntry);

        // switched off: back to the typed number
        Assert.Null(await svc.SaveAsync(new("PurchaseOrder", "PO-{YYYY}-{SEQ}", 5, "Gregorian", "Yearly", false, true, null)));
        Assert.False((await svc.GetFieldAsync("PurchaseOrder")).Automatic);
    }

    [Fact]
    public async Task List_shows_every_procurement_document_and_the_preview_consumes_no_number()
    {
        await using var ctx = PemsDbContextFactory.CreateInMemory(nameof(List_shows_every_procurement_document_and_the_preview_consumes_no_number));
        var svc = new NumberingSchemeService(ctx);

        var all = await svc.GetAllAsync();
        Assert.Equal(NumberingSchemeService.ProcurementDocumentTypes, all.Select(x => x.DocumentType));
        Assert.All(all, x => Assert.False(x.IsActive));   // nothing configured yet: defaults shown, inactive

        var (number, error) = svc.Preview(new("Contract", "CT-{PROJECT}-{DISC}-{SEQ}", 3, "Jalali", "Never", true, false, null), new NumberingContext("PRJ", "MEC"));
        Assert.Null(error);
        Assert.Equal("CT-PRJ-MEC-001", number);
        Assert.Empty(ctx.NumberingSequences);

        Assert.Equal((null, "Num_Err_YearlyNeedsYear"), svc.Preview(new("Contract", "CT-{SEQ}", 3, "Jalali", "Yearly", true, false, null), NumberingContext.Empty));
    }

    [Fact]
    public async Task Without_an_active_scheme_the_typed_number_is_used_as_before()
    {
        await using var ctx = PemsDbContextFactory.CreateInMemory(nameof(Without_an_active_scheme_the_typed_number_is_used_as_before));
        ctx.NumberingSchemes.Add(new NumberingScheme { DocumentType = "Contract", Pattern = "CT-{YYYY}-{SEQ}", IsActive = false });
        await ctx.SaveChangesAsync();
        INumberingService numbering = new NumberingService(ctx);

        Assert.Equal("CT-77", await numbering.ResolveNumberAsync("Contract", "  CT-77 "));
        Assert.Equal("", await numbering.ResolveNumberAsync("Contract", "   "));          // the document service rejects it, as before
        Assert.Equal("PO-1", await numbering.ResolveNumberAsync("PurchaseOrder", "PO-1"));  // no scheme row at all
    }

    [Fact]
    public async Task Seeder_adds_missing_schemes_active_and_never_touches_an_edited_one()
    {
        await using var ctx = PemsDbContextFactory.CreateInMemory(nameof(Seeder_adds_missing_schemes_active_and_never_touches_an_edited_one));
        ctx.NumberingSchemes.Add(new NumberingScheme { DocumentType = "Contract", Pattern = "K-{SEQ}", ResetPeriod = "Never", IsActive = false, Version = 3 });
        await ctx.SaveChangesAsync();
        var services = new ServiceCollection().AddSingleton(ctx).BuildServiceProvider();

        await PEMS.Persistence.Seed.NumberingSchemeSeeder.SeedAsync(services);
        await PEMS.Persistence.Seed.NumberingSchemeSeeder.SeedAsync(services);   // idempotent

        Assert.Equal(NumberingSchemeService.ProcurementDocumentTypes.Count, ctx.NumberingSchemes.Count());
        var contract = ctx.NumberingSchemes.Single(x => x.DocumentType == "Contract");
        Assert.Equal(("K-{SEQ}", false, 3), (contract.Pattern, contract.IsActive, contract.Version));
        var po = ctx.NumberingSchemes.Single(x => x.DocumentType == "PurchaseOrder");
        Assert.Equal(("PO-{YYYY}-{SEQ}", true, NumberingCalendars.Jalali), (po.Pattern, po.IsActive, po.Calendar));
    }
}
