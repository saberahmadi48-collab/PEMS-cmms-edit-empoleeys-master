using PEMS.UI.Abstractions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-386: the mask rule on the server: what is posted is stored without separators and Persian digits are Latin; the display format fills the slots.</summary>
public sealed class FieldMaskTests
{
    [Theory]
    [InlineData("1234-5678 9012", "123456789012")]
    [InlineData("۰۹۱۲ ۳۴۵ ۶۷۸۹", "09123456789")]
    [InlineData("ab 12-cd", "ab12cd")]
    [InlineData("", "")]
    public void The_stored_value_has_no_separators_and_latin_digits(string typed, string stored) => Assert.Equal(stored, FieldMask.Canonical(typed));

    [Theory]
    [InlineData("1234567812345678", "9999-9999-9999-9999", "1234-5678-1234-5678")]
    [InlineData("123", "9999-9999", "123")]
    [InlineData("12345", "9999-9999", "1234-5")]
    [InlineData("ab123", "AA99", "ab12")]
    public void The_display_fills_the_slots_in_order(string stored, string mask, string shown) => Assert.Equal(shown, FieldMask.Format(stored, mask));
}
