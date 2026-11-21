using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

/// <summary>Identity, accessibility attributes and CSS of one field, computed from the descriptor and whether it has an error.</summary>
public sealed record FieldState(string Id, string? DescribedBy, string InputCss, bool Invalid)
{
    public string HelpId => Id + "-help";
    public string ErrorId => Id + "-error";

    public static FieldState From(FieldDescriptor d, bool hasError)
    {
        var id = "f-" + d.Name.Replace('.', '-').Replace('[', '-').Replace(']', '-');
        var hasHelp = d.HelpText is { IsEmpty: false };
        var describedBy = hasError ? id + "-error" : hasHelp ? id + "-help" : null;
        return new FieldState(id, describedBy, hasError ? "pems-input pems-input--invalid" : "pems-input", hasError);
    }
}
