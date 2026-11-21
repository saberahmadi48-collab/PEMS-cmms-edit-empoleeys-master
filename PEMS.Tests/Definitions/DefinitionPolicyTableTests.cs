using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Common;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.Web.Api;
using PEMS.Web.Api.V1;

namespace PEMS.Tests.Definitions;

/// <summary>
/// SCRUM-808 (gap of SCRUM-440): one test per cell of the policy table in guideline 04 ("Who may change a definition"): view opens the workbench and reads, create makes a definition and imports, edit changes a draft,
/// approve publishes and rolls back; the workbench operations carry the same permissions as the REST endpoints. A new endpoint or operation without a row here fails, so the table cannot drift. The two-person rule is
/// tested as a pure function here and through the guard in <c>DefinitionPublishGuardTests</c>.
/// </summary>
public sealed class DefinitionPolicyTableTests
{
    // REST endpoint (controller method) -> the action of the module Definitions it needs
    private static readonly Dictionary<string, string> Rest = new(StringComparer.Ordinal)
    {
        [nameof(DefinitionsApiController.List)] = "View", [nameof(DefinitionsApiController.Get)] = "View", [nameof(DefinitionsApiController.GetVersion)] = "View", [nameof(DefinitionsApiController.GetDraft)] = "View",
        [nameof(DefinitionsApiController.Export)] = "View", [nameof(DefinitionsApiController.GetUpstream)] = "View", [nameof(DefinitionsApiController.UsedBy)] = "View", [nameof(DefinitionsApiController.References)] = "View",
        [nameof(DefinitionsApiController.PrecheckPublish)] = "View", [nameof(DefinitionsApiController.PrecheckRollback)] = "View",
        [nameof(DefinitionsApiController.Create)] = "Create", [nameof(DefinitionsApiController.Import)] = "Create",
        [nameof(DefinitionsApiController.UpdateEnvelope)] = "Edit", [nameof(DefinitionsApiController.SaveDraft)] = "Edit", [nameof(DefinitionsApiController.DiscardDraft)] = "Edit",
        [nameof(DefinitionsApiController.CreateDraft)] = "Edit", [nameof(DefinitionsApiController.Validate)] = "Edit",
        [nameof(DefinitionsApiController.Publish)] = "Approve", [nameof(DefinitionsApiController.Rollback)] = "Approve", [nameof(DefinitionsApiController.ResetToShipped)] = "Approve",
    };

    // workbench operation (model Definition) -> permission
    private static readonly Dictionary<string, string> Workbench = new(StringComparer.Ordinal)
    {
        ["Create"] = "Definitions.Create", ["SaveDraft"] = "Definitions.Edit", ["Validate"] = "Definitions.Edit", ["Publish"] = "Definitions.Approve", ["Rollback"] = "Definitions.Approve", ["DiscardDraft"] = "Definitions.Edit",
    };

    private static IEnumerable<MethodInfo> Endpoints()
        => typeof(DefinitionsApiController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    public static IEnumerable<object[]> RestCells() => Rest.Select(c => new object[] { c.Key, c.Value });

    [Theory]
    [MemberData(nameof(RestCells))]
    public void Each_rest_endpoint_needs_the_permission_of_its_cell(string method, string action)
    {
        var endpoint = Assert.Single(Endpoints(), m => m.Name == method);
        var permission = Assert.Single(endpoint.GetCustomAttributes<ApiPermissionAttribute>());
        Assert.Equal(ApiPermissionAttribute.Prefix + "Definitions." + action, permission.Policy);
    }

    [Fact]
    public void An_endpoint_that_is_not_in_the_table_fails_so_the_table_cannot_drift()
    {
        var missing = Endpoints().Select(m => m.Name).Where(n => !Rest.ContainsKey(n)).ToList();
        Assert.True(missing.Count == 0, "Add the endpoint to the policy table (guideline 04, SCRUM-440) and to this test: " + string.Join(", ", missing));
    }

    public static IEnumerable<object[]> WorkbenchCells() => Workbench.Select(c => new object[] { c.Key, c.Value });

    [Theory]
    [MemberData(nameof(WorkbenchCells))]
    public void Each_workbench_operation_needs_the_permission_of_its_cell(string action, string permission)
    {
        var operation = typeof(IDefinitionWorkbench).GetMethods().Select(m => m.GetCustomAttribute<PemsOperationAttribute>()).Single(a => a is { Model: "Definition" } && a.Action == action)!;
        Assert.Equal(permission, operation.Permission);
    }

    [Fact]
    public void An_operation_that_is_not_in_the_table_fails()
    {
        var missing = typeof(IDefinitionWorkbench).GetMethods().Select(m => m.GetCustomAttribute<PemsOperationAttribute>()).Where(a => a is { Model: "Definition" } && !Workbench.ContainsKey(a.Action)).Select(a => a!.Action).ToList();
        Assert.True(missing.Count == 0, "Add the operation to the policy table: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("page:home", "page", "home", true)]
    [InlineData("PAGE:Home", "page", "home", true)]
    [InlineData("page:*", "page", "anything", true)]
    [InlineData("layout:*,page:home", "page", "home", true)]
    [InlineData("page:home", "page", "other", false)]
    [InlineData("page:home", "form", "home", false)]
    [InlineData("", "page", "home", false)]
    [InlineData(null, "page", "home", false)]
    public void The_protected_setting_names_a_definition_or_a_whole_kind(string? setting, string kind, string key, bool expected)
        => Assert.Equal(expected, TwoPersonPublish.IsProtected(setting, kind, key));

    [Theory]
    [InlineData("page:home", "page", "home", "hamid", "hamid", true)]          // the author publishes a protected page: a second person is needed
    [InlineData("page:home", "page", "home", "Hamid", "hamid", true)]          // names compare without case
    [InlineData("page:home", "page", "home", "hamid", "saber", false)]         // another approver may publish
    [InlineData("page:home", "page", "other", "hamid", "hamid", false)]        // not protected: one person may do both
    [InlineData("", "page", "home", "hamid", "hamid", false)]                  // no protected definitions configured
    [InlineData("page:home", "page", "home", null, "hamid", false)]            // an author that is not recorded (shipped drafts) does not block
    public void A_protected_definition_waits_for_a_second_approver_only_when_the_publisher_made_the_draft(string setting, string kind, string key, string? author, string publisher, bool needsSecond)
        => Assert.Equal(needsSecond, TwoPersonPublish.NeedsSecondApprover(setting, kind, key, author, publisher));
}
