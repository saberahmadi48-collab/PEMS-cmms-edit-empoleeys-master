using System.ComponentModel.DataAnnotations;
using System.Reflection;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-373: the generic entity write actions use the one validator with the constraints the entity declares.</summary>
public sealed class EntityConstraintsTests
{
    private sealed class Part
    {
        [MaxLength(5)] public string Code { get; set; } = "";
        [StringLength(10, MinimumLength = 3)] public string Name { get; set; } = "";
        [Range(1, 100)] public int Qty { get; set; }
        [Range(0.5, 2.5)] public decimal? Factor { get; set; }
        public string Note { get; set; } = "";
        public DateTime When { get; set; }
    }

    private static PropertyInfo P(string name) => typeof(Part).GetProperty(name)!;

    private sealed record EntityConstraintsResult(string? Field, string? Message);

    private static ActionOutcome? Invoke(string property, string? text) => EntityConstraints.Check(P(property), property.ToLowerInvariant(), text);

    private static EntityConstraintsResult Run(string property, string? text)
    {
        var outcome = Invoke(property, text);
        return new EntityConstraintsResult(outcome?.FieldErrors?.Keys.SingleOrDefault(), outcome?.FieldErrors?.Values.SingleOrDefault()?.Resolve(System.Globalization.CultureInfo.GetCultureInfo("en")));
    }

    [Theory]
    [InlineData("Code", "ABCDEF", "code", "At most 5 characters.")]
    [InlineData("Name", "ab", "name", "At least 3 characters.")]
    [InlineData("Name", "abcdefghijk", "name", "At most 10 characters.")]
    [InlineData("Qty", "0", "qty", "The smallest allowed value is 1.")]
    [InlineData("Qty", "101", "qty", "The largest allowed value is 100.")]
    [InlineData("Factor", "3", "factor", "The largest allowed value is 2.5.")]
    public void A_value_that_breaks_a_declared_constraint_is_refused_on_its_field_with_the_catalog_text(string property, string text, string field, string message)
        => Assert.Equal(new EntityConstraintsResult(field, message), Run(property, text));

    [Theory]
    [InlineData("Code", "ABCDE")]
    [InlineData("Name", "abc")]
    [InlineData("Qty", "50")]
    [InlineData("Qty", "۵۰")]            // Persian digits are read as digits
    [InlineData("Factor", "1.5")]
    [InlineData("Code", "")]             // empty is the business of required, not of the constraints
    [InlineData("Code", null)]
    [InlineData("Note", "any text of any length")]      // declares nothing
    [InlineData("When", "2026-11-19")]                   // types are converted by the action, not validated here
    public void A_value_inside_the_constraints_or_a_property_that_declares_none_passes(string property, string? text)
        => Assert.Null(Invoke(property, text));
}
