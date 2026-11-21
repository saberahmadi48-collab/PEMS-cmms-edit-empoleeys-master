using PEMS.Application.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-797: the preflight problem names the form, the field and the reason, the same line in the shipping result, the logs and /health/database.</summary>
public sealed class OwnershipPreflightReportTests
{
    [Fact]
    public void A_problem_line_names_form_field_code_and_reason()
    {
        var problem = new OwnershipProblem("form", "core", "warehouses-edit", "code", "name-taken", "The shipped platform field 'code' has the same name as an application field of the customer; rename the application field first.");
        Assert.StartsWith("form/core/warehouses-edit: field 'code' (name-taken) ", problem.Line);
        Assert.Contains("rename the application field first", problem.Line);
    }

    [Fact]
    public void The_merge_reports_a_name_clash_as_an_application_field_conflict_all_at_once()
    {
        const string customer = """{ "fields": [ { "name": "code", "kind": "text" }, { "name": "name", "kind": "text" } ] }""";
        const string shipped = """{ "fields": [ { "name": "code", "kind": "text", "owner": "platform" }, { "name": "name", "kind": "number", "owner": "platform" } ] }""";
        var merge = FormOwnership.Merge(customer, shipped);
        Assert.False(merge.Ok);
        Assert.Contains(merge.Conflicts, c => c.Field == "code" && c.Code == "name-taken");   // the customer's own field has the shipped platform name
        Assert.Null(merge.MergedJson);                                                       // nothing is merged when any conflict exists
    }
}
