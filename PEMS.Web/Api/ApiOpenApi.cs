using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PEMS.Web.Api;

/// <summary>Adds the parts of the standard that every operation shares, so the contract shows them without each action repeating them.</summary>
public static class ApiOpenApi
{
    public static Task DescribeStandardParts(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var method = context.Description.HttpMethod ?? "";
        if (context.Description.ActionDescriptor is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor action)
        {
            var controller = action.ControllerName.EndsWith("Api", StringComparison.Ordinal) ? action.ControllerName[..^3] : action.ControllerName;
            operation.OperationId = controller + "_" + action.ActionName;                   // unique and stable: Definitions_Publish
            operation.Tags = new HashSet<OpenApiTagReference> { new(controller) };
        }
        var hasId = context.Description.RelativePath?.Contains("{id", StringComparison.Ordinal) == true;
        operation.Responses ??= new OpenApiResponses();
        AddProblem(operation, "401", "Not signed in.");
        AddProblem(operation, "403", "Not allowed.");
        if (hasId) AddProblem(operation, "404", "Not found.");

        if (method is "PUT" or "PATCH" or "DELETE")
        {
            AddHeader(operation, "If-Match", "The ETag from the last read; a missing header gives 428, a stale one 412.", required: true);
            AddProblem(operation, "412", "The resource changed since it was read.");
            AddProblem(operation, "428", "If-Match is required.");
        }
        if (method == "POST") AddHeader(operation, "Idempotency-Key", "Optional; a replay with the same key and body returns the first response.", required: false);
        return Task.CompletedTask;
    }

    private static void AddProblem(OpenApiOperation operation, string status, string description)
    {
        if (operation.Responses!.ContainsKey(status)) return;
        operation.Responses[status] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType> { [ApiProblem.ContentType] = new() },
        };
    }

    private static void AddHeader(OpenApiOperation operation, string name, string description, bool required)
    {
        operation.Parameters ??= new List<IOpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = required,
            Description = description,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        });
    }
}
