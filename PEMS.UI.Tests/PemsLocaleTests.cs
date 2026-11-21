using System.Globalization;
using PEMS.UI.Abstractions;
using PEMS.UI.Rendering;

namespace PEMS.UI.Tests;

public sealed class PemsLocaleTests
{
    private static T InCulture<T>(string name, Func<T> action)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(name);
        try { return action(); }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    private static PemsLocale Locale(Action<PemsUiOptions>? configure = null)
    {
        var o = new PemsUiOptions();
        configure?.Invoke(o);
        return new PemsLocale(o);
    }

    [Fact]
    public void Text_follows_the_current_ui_culture_and_the_default_language()
    {
        var save = TranslatableText.Of(("fa", "ذخیره"), ("en", "Save"));
        Assert.Equal("ذخیره", InCulture("fa-IR", () => Locale().Text(save)));
        Assert.Equal("Save", InCulture("en-US", () => Locale().Text(save)));
        Assert.Equal("Save", InCulture("de-DE", () => Locale(o => o.DefaultLanguage = "en").Text(save)));
        Assert.Equal("", Locale().Text(null));
    }

    [Fact]
    public void Persian_culture_shows_jalali_dates_and_others_show_iso()
    {
        var date = new DateOnly(2026, 10, 27);
        Assert.Equal("۱۴۰۵/۰۸/۰۵", InCulture("fa-IR", () => Locale().Format(date)));         // Persian digits are the default
        Assert.Equal("2026-10-27", InCulture("en-US", () => Locale().Format(date)));
        Assert.Equal("۱۴۰۵/۰۱/۰۱", InCulture("fa", () => Locale().Format(new DateTime(2026, 3, 21))));   // Nowruz 1405
    }

    [Fact]
    public void Date_and_time_shows_the_time_only_when_there_is_one()
    {
        Assert.Equal("۱۴۰۵/۰۸/۰۵ ۱۴:۳۰", InCulture("fa", () => Locale().Format(new DateTime(2026, 10, 27, 14, 30, 0))));
        Assert.Equal("2026-10-27", InCulture("en", () => Locale().Format(new DateTime(2026, 10, 27))));
        Assert.Equal("2026-10-27 00:00", InCulture("en", () => Locale().Format(new DateTime(2026, 10, 27), ValueKind.DateTime)));
    }

    [Fact]
    public void Calendar_option_overrides_the_culture()
    {
        var date = new DateOnly(2026, 10, 27);
        Assert.Equal("۲۰۲۶-۱۰-۲۷", InCulture("fa", () => Locale(o => o.Calendar = CalendarMode.Gregorian).Format(date)));   // digits follow the language, the calendar follows the option
        Assert.Equal("1405/08/05", InCulture("en", () => Locale(o => o.Calendar = CalendarMode.Persian).Format(date)));     // English keeps Latin digits
    }

    [Fact]
    public void Native_digits_apply_to_persian_cultures_only_and_can_be_switched_off()
    {
        var date = new DateOnly(2026, 10, 27);
        Assert.Equal("۱۴۰۵/۰۸/۰۵", InCulture("fa", () => Locale().Format(date)));                                       // default: on
        Assert.Equal("1405/08/05", InCulture("fa", () => Locale(o => o.UseNativeDigits = false).Format(date)));
        Assert.Equal("2026-10-27", InCulture("en", () => Locale().Format(date)));                                       // other cultures: Latin
        Assert.Equal("۱٬۲۳۴", InCulture("fa", () => Locale().Format(1234)).Replace(",", "٬"));                          // numbers too (separator depends on ICU data)
        Assert.Equal("1,234", InCulture("en", () => Locale().Format(1234)));                                           // English: Latin digits for numbers too
    }

    [Fact]
    public void Numbers_use_the_culture_and_strings_are_never_touched()
    {
        Assert.Equal("1,234,567.5", InCulture("en-US", () => Locale().Format(1234567.5m)));
        Assert.Equal("1,234,567", InCulture("en-US", () => Locale().Format(1234567)));
        Assert.Equal("12.5", InCulture("en-US", () => Locale().Format(12.5d)));
        Assert.Equal("1405/08/05", InCulture("fa", () => Locale().Format("1405/08/05")));          // already text: left alone
        Assert.Equal("", Locale().Format(null));
    }

    [Fact]
    public void Dates_outside_the_persian_calendar_range_fall_back_to_iso()
    {
        Assert.Equal("۰۰۰۱-۰۱-۰۱", InCulture("fa", () => Locale().Format(DateTime.MinValue)));      // ISO fallback, digits still follow the language
    }

    [Fact]
    public void IsRightToLeft_follows_the_culture()
    {
        Assert.True(InCulture("fa", () => Locale().IsRightToLeft));
        Assert.False(InCulture("en", () => Locale().IsRightToLeft));
    }

    [Fact]
    public void Digits_swaps_digits_in_ready_text_without_reformatting_it()
    {
        Assert.Equal("نمایش ۵۱ تا ۷۵ از ۲۵۰ رکورد", InCulture("fa", () => Locale().Digits("نمایش 51 تا 75 از 250 رکورد")));
        Assert.Equal("۱۲۳۴", InCulture("fa", () => Locale().Digits("1234")));                  // no thousands separator is added
        Assert.Equal("Showing 51-75 of 250", InCulture("en", () => Locale().Digits("Showing 51-75 of 250")));   // English: unchanged
        Assert.Equal("12", InCulture("fa", () => Locale(o => o.UseNativeDigits = false).Digits("12")));        // switched off: unchanged
        Assert.Equal("", InCulture("fa", () => Locale().Digits("")));
    }
}
