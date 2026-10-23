using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>شماره‌گذاری [DEPT]-[YEAR]-[SEQ]: قالب، شمارنده‌ی مستقل هر (دپارتمان، سال)، ادامه از شماره‌های موجود، و عدم تکرار.</summary>
public class LetterNumberingTests
{
    private static readonly int Year = DateTime.Now.Year;

    [Theory]
    [InlineData("eng", "ENG")]
    [InlineData("ENG-01", "ENG01")]
    [InlineData(" qa/qc ", "QAQC")]
    [InlineData("a-very-long-department-code", "AVERYLONGD")]
    [InlineData("بخش", "GEN")]
    [InlineData("", "GEN")]
    [InlineData(null, "GEN")]
    public void SanitizeScope_KeepsOnlyLatinLettersAndDigits_CapsAtTen_AndFallsBackToGen(string? code, string expected)
        => Assert.Equal(expected, LetterNumberingService.SanitizeScope(code));

    [Fact]
    public void Format_IsDeptYearFiveDigitSequence()
        => Assert.Equal("ENG-2026-00123", LetterNumberingService.Format("ENG", 2026, 123));

    [Fact]
    public async Task NumbersAreSequential_AndUniquePerDepartmentAndYear()
    {
        var ctx = LetterTestWorld.NewContext(nameof(NumbersAreSequential_AndUniquePerDepartmentAndYear));
        ctx.Departments.Add(new Department { DepartmentId = 2, Code = "HSE", Name = "HSE" });
        await ctx.SaveChangesAsync();
        var svc = new LetterNumberingService(ctx);

        var a1 = await svc.GenerateNextNumberAsync("Internal", null, 1);
        var a2 = await svc.GenerateNextNumberAsync("Outgoing", null, 1);   // the sequence is shared by every letter type of the department
        var h1 = await svc.GenerateNextNumberAsync("Internal", null, 2);
        var a3 = await svc.GenerateNextNumberAsync("Incoming", null, 1);
        var gen = await svc.GenerateNextNumberAsync("Incoming", null, null);

        Assert.Equal($"ENG01-{Year}-00001", a1);
        Assert.Equal($"ENG01-{Year}-00002", a2);
        Assert.Equal($"HSE-{Year}-00001", h1);
        Assert.Equal($"ENG01-{Year}-00003", a3);
        Assert.Equal($"GEN-{Year}-00001", gen);
    }

    [Fact]
    public async Task ManyConsecutiveNumbers_AreAllDistinct()
    {
        var ctx = LetterTestWorld.NewContext(nameof(ManyConsecutiveNumbers_AreAllDistinct));
        var svc = new LetterNumberingService(ctx);

        var numbers = new List<string>();
        for (var i = 0; i < 60; i++) numbers.Add(await svc.GenerateNextNumberAsync("Internal", null, 1));

        Assert.Equal(60, numbers.Distinct().Count());
        Assert.Equal($"ENG01-{Year}-00060", numbers[^1]);
    }

    [Fact]
    public async Task TheFirstNumberContinuesFromTheHighestExistingOne_NotFromZero()
    {
        var ctx = LetterTestWorld.NewContext(nameof(TheFirstNumberContinuesFromTheHighestExistingOne_NotFromZero));
        ctx.Letters.AddRange(
            new Letter { LetterNumber = $"ENG01-{Year}-00041", LetterType = "Internal", Subject = "a", IsActive = true },
            new Letter { LetterNumber = $"ENG01-{Year}-00007", LetterType = "Internal", Subject = "b", IsActive = true },
            new Letter { LetterNumber = $"ENG01-{Year - 1}-00999", LetterType = "Internal", Subject = "old year", IsActive = true },
            new Letter { LetterNumber = "OLDFMT-IN-2024-0009", LetterType = "Incoming", Subject = "legacy format", IsActive = true });
        await ctx.SaveChangesAsync();

        Assert.Equal($"ENG01-{Year}-00042", await new LetterNumberingService(ctx).GenerateNextNumberAsync("Internal", null, 1));
    }

    [Fact]
    public async Task ANumberAlreadyTakenByHand_IsSkipped()
    {
        var ctx = LetterTestWorld.NewContext(nameof(ANumberAlreadyTakenByHand_IsSkipped));
        var svc = new LetterNumberingService(ctx);
        var first = await svc.GenerateNextNumberAsync("Internal", null, 1);       // ...00001 consumed from the sequence only

        // somebody typed the next number manually (ManualNumber permission) without going through the sequence
        ctx.Letters.Add(new Letter { LetterNumber = $"ENG01-{Year}-00002", LetterType = "Internal", Subject = "manual", IsActive = true });
        await ctx.SaveChangesAsync();

        var next = await svc.GenerateNextNumberAsync("Internal", null, 1);

        Assert.Equal($"ENG01-{Year}-00001", first);
        Assert.Equal($"ENG01-{Year}-00003", next);
    }

    [Fact]
    public async Task TheSequenceRowIsPersisted_SoANewServiceInstanceContinuesCorrectly()
    {
        var ctx = LetterTestWorld.NewContext(nameof(TheSequenceRowIsPersisted_SoANewServiceInstanceContinuesCorrectly));
        await new LetterNumberingService(ctx).GenerateNextNumberAsync("Internal", null, 1);
        await new LetterNumberingService(ctx).GenerateNextNumberAsync("Internal", null, 1);

        var row = Assert.Single(ctx.LetterNumberSequences);
        Assert.Equal(2, row.LastValue);
        Assert.Equal("ENG01", row.Scope);
        Assert.Equal(Year, row.Year);
    }
}
