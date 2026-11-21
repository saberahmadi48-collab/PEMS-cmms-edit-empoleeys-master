using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using PEMS.Application.Validation;

namespace PEMS.Web.Definitions;

/// <summary>
/// SCRUM-373: the generic entity write actions (entity.create, entity.update) check the value of a mapped field with the one validator (<see cref="FieldValidator"/>), using the constraints the entity already declares with
/// <c>[MaxLength]</c>, <c>[StringLength]</c> and <c>[Range]</c>; the stable code and the text in both languages are those of the catalog (docs/architecture/FIELD_VALIDATION_CATALOG.md). Required-ness and type conversion stay with the action.
/// </summary>
public static class EntityConstraints
{
    private static readonly HashSet<Type> Numbers = [typeof(int), typeof(long), typeof(short), typeof(byte), typeof(decimal), typeof(double), typeof(float)];

    /// <summary>The constraints a property declares, or null when it declares none the validator checks.</summary>
    public static FieldConstraints? For(PropertyInfo property, string name)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(string))
        {
            var stringLength = property.GetCustomAttribute<StringLengthAttribute>();
            var max = property.GetCustomAttribute<MaxLengthAttribute>()?.Length ?? stringLength?.MaximumLength;
            var min = stringLength?.MinimumLength is > 0 ? stringLength.MinimumLength : (int?)null;
            if (max is null or <= 0 && min is null) return null;
            return new FieldConstraints { Name = name, Type = FieldType.Text, MaxLength = max is > 0 ? max : null, MinLength = min };
        }
        if (Numbers.Contains(type) && property.GetCustomAttribute<RangeAttribute>() is { } range)
        {
            try
            {
                return new FieldConstraints
                {
                    Name = name, Type = FieldType.Number,
                    Min = Convert.ToDecimal(range.Minimum, CultureInfo.InvariantCulture), Max = Convert.ToDecimal(range.Maximum, CultureInfo.InvariantCulture),
                };
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return null; }
        }
        return null;
    }

    /// <summary>A refusal on <paramref name="field"/> when the posted text breaks a declared constraint; null when it passes, is empty, or the property declares nothing.</summary>
    public static ActionOutcome? Check(PropertyInfo property, string field, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || For(property, field) is not { } constraints) return null;
        var violation = FieldValidator.CheckValue(constraints, text.Trim()).FirstOrDefault(v => !v.Advisory);
        return violation is null ? null : ActionOutcome.Refused(field, ValidationMessages.Text(violation.Code, violation.Params));
    }
}
