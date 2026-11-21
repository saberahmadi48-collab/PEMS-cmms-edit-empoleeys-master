using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;

namespace PEMS.UI.Binding;

/// <summary>
/// Binds numbers and dates the way a browser submits them, in every culture (SCRUM-421, ADR-0008): a number input posts "12.5" even when the
/// page language is Persian, whose culture expects a different decimal mark; a Persian user may also type Persian digits. The value is
/// normalised with <see cref="PemsDigits"/>, numbers are parsed with the invariant culture and dates as ISO first, then with the request
/// culture (as the built-in binder does). Empty values, nullable types and MVC's error messages behave exactly as in the built-in binder.
/// </summary>
public sealed class PemsNumericModelBinder(Type modelType) : IModelBinder
{
    private static readonly string[] IsoDateTimeFormats =
        ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"];

    private readonly Type _type = Nullable.GetUnderlyingType(modelType) ?? modelType;
    private readonly bool _nullable = Nullable.GetUnderlyingType(modelType) is not null || !modelType.IsValueType;

    public Task BindModelAsync(ModelBindingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = context.ValueProvider.GetValue(context.ModelName);
        if (result == ValueProviderResult.None) return Task.CompletedTask;       // nothing posted: leave the model alone, like the built-in binder

        context.ModelState.SetModelValue(context.ModelName, result);              // keep what the user typed for redisplay
        var provider = context.ModelMetadata.ModelBindingMessageProvider;
        var raw = result.FirstValue;

        if (string.IsNullOrWhiteSpace(raw))
        {
            if (_nullable) context.Result = ModelBindingResult.Success(null);
            else context.ModelState.TryAddModelError(context.ModelName, provider.ValueMustNotBeNullAccessor(result.ToString()));
            return Task.CompletedTask;
        }

        var value = Parse(PemsDigits.ToLatin(raw.Trim()), result.Culture);
        if (value is null)
        {
            context.ModelState.TryAddModelError(context.ModelName, provider.AttemptedValueIsInvalidAccessor(raw, context.ModelMetadata.GetDisplayName()));
            return Task.CompletedTask;
        }
        context.Result = ModelBindingResult.Success(value);
        return Task.CompletedTask;
    }

    private object? Parse(string s, CultureInfo culture)
    {
        var inv = CultureInfo.InvariantCulture;
        if (_type == typeof(decimal)) return decimal.TryParse(s, NumberStyles.Float, inv, out var d) ? d : null;
        if (_type == typeof(double)) return double.TryParse(s, NumberStyles.Float, inv, out var d) ? d : null;
        if (_type == typeof(float)) return float.TryParse(s, NumberStyles.Float, inv, out var d) ? d : null;
        if (_type == typeof(int)) return int.TryParse(s, NumberStyles.Integer, inv, out var d) ? d : null;
        if (_type == typeof(long)) return long.TryParse(s, NumberStyles.Integer, inv, out var d) ? d : null;
        if (_type == typeof(short)) return short.TryParse(s, NumberStyles.Integer, inv, out var d) ? d : null;
        if (_type == typeof(byte)) return byte.TryParse(s, NumberStyles.Integer, inv, out var d) ? d : null;
        if (_type == typeof(sbyte)) return sbyte.TryParse(s, NumberStyles.Integer, inv, out var d) ? d : null;
        if (_type == typeof(ushort)) return ushort.TryParse(s, NumberStyles.Integer, inv, out var d) ? d : null;
        if (_type == typeof(uint)) return uint.TryParse(s, NumberStyles.Integer, inv, out var d) ? d : null;
        if (_type == typeof(ulong)) return ulong.TryParse(s, NumberStyles.Integer, inv, out var d) ? d : null;
        if (_type == typeof(DateTime))
            return DateTime.TryParseExact(s, IsoDateTimeFormats, inv, DateTimeStyles.None, out var iso) ? iso
                : DateTime.TryParse(s, culture, DateTimeStyles.None, out var dt) ? dt : null;
        if (_type == typeof(DateOnly))
            return DateOnly.TryParseExact(s, "yyyy-MM-dd", inv, DateTimeStyles.None, out var iso) ? iso
                : DateOnly.TryParse(s, culture, DateTimeStyles.None, out var d) ? d : null;
        if (_type == typeof(TimeOnly)) return TimeOnly.TryParse(s, culture, DateTimeStyles.None, out var t) ? t : null;
        if (_type == typeof(DateTimeOffset))
            return DateTimeOffset.TryParseExact(s, IsoDateTimeFormats, inv, DateTimeStyles.None, out var iso) ? iso
                : DateTimeOffset.TryParse(s, culture, DateTimeStyles.None, out var d) ? d : null;
        return null;
    }
}

/// <summary>Chooses <see cref="PemsNumericModelBinder"/> for numeric and date/time types; every other type, and any property with an explicit binder, keeps the built-in behaviour.</summary>
public sealed class PemsNumericModelBinderProvider : IModelBinderProvider
{
    private static readonly HashSet<Type> Supported =
    [
        typeof(decimal), typeof(double), typeof(float),
        typeof(int), typeof(long), typeof(short), typeof(byte), typeof(sbyte), typeof(ushort), typeof(uint), typeof(ulong),
        typeof(DateTime), typeof(DateOnly), typeof(TimeOnly), typeof(DateTimeOffset)
    ];

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.BindingInfo?.BinderType is not null) return null;                // an explicit [ModelBinder] wins
        var type = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        return Supported.Contains(type) ? new PemsNumericModelBinder(context.Metadata.ModelType) : null;
    }
}
