namespace PEMS.UI.Abstractions;

/// <summary>How a displayed value is formatted. Values are never translated, only formatted for the culture.</summary>
public enum ValueKind
{
    /// <summary>Choose by the runtime type of the value.</summary>
    Auto,
    Text,
    Number,
    Date,
    DateTime
}
