using System.Text.Json.Nodes;

namespace PEMS.Tests.Definitions;

/// <summary>The payload of a component definition that ships with the product (read from the system package the test project copies), for tests that put it in front of a renderer.</summary>
internal static class ShippedComponents
{
    public static string Payload(string key)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "definitions-system", "component", "core", key + ".json")))!["payload"]!.ToJsonString();
}
