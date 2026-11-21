using System.Text.Json;
using PEMS.Application.Validation;
using PEMS.UI.Abstractions;
using PEMS.Web.Api;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-380: the validator, a refused action and an import cell give a client the same field-error members.</summary>
public sealed class ErrorShapeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string[] Members(ApiFieldError e)
        => JsonSerializer.SerializeToElement(e, Web).EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Null).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    public void A_refused_action_has_a_pointer_a_stable_code_a_key_and_the_text_in_the_users_language()
    {
        var outcome = ActionOutcome.Refused("name", TranslatableText.Of(("en", "Too long."), ("fa", "بیش از حد طولانی است.")));
        var error = Assert.Single(ErrorShape.FromOutcome(outcome, "fa"));
        Assert.Equal(("/name", "refused", "Err_Refused", "بیش از حد طولانی است."), (error.Pointer, error.Code, error.MessageKey, error.Message));

        var form = Assert.Single(ErrorShape.FromOutcome(ActionOutcome.Refused(TranslatableText.Of(("en", "Not allowed."), ("fa", "مجاز نیست."))), "en"));
        Assert.Equal(("", "Not allowed."), (form.Pointer, form.Message));
    }

    [Fact]
    public void The_validator_a_refused_action_and_an_import_cell_share_the_same_members()
    {
        var fromValidator = ValidationMessages.ToApiErrors([new Violation("/name", ConstraintCodes.Required, ConstraintCodes.MessageKey(ConstraintCodes.Required), new Dictionary<string, object?>())], "en").Single();
        var fromAction = ErrorShape.FromOutcome(ActionOutcome.Refused("name", TranslatableText.Of(("en", "No."), ("fa", "نه."))), "en").Single();
        var fromImport = ErrorShape.ImportCell(3, "name", ConstraintCodes.Required, "Val_Required", "Required.");

        Assert.Equal("/rows/3/name", fromImport.Pointer);
        foreach (var member in new[] { "pointer", "code", "messageKey", "message" })
            Assert.All(new[] { fromValidator, fromAction, fromImport }, e => Assert.Contains(member, Members(e)));
    }
}
