using PEMS.UI.Abstractions;

namespace PEMS.UI.Tests.Host;

public static class SpikeData
{
    public static ViewDescriptor Table { get; } = new()
    {
        Id = "spike-table",
        Title = "Instruments",
        Columns =
        [
        new ColumnDescriptor { Key = "tag", Header = "Tag" },
        new ColumnDescriptor { Key = "name", Header = "Name" },
        new ColumnDescriptor { Key = "area", Header = "Area" },
        new ColumnDescriptor { Key = "status", Header = "Status", Alignment = ColumnAlignment.Center },
        new ColumnDescriptor { Key = "range", Header = "Range", Alignment = ColumnAlignment.End },
        new ColumnDescriptor { Key = "owner", Header = "Owner" }
        ]
    };

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(int count) =>
        Enumerable.Range(1, count).Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["tag"] = $"PT-{i:0000}",
            ["name"] = $"Pressure transmitter {i}",
            ["area"] = $"Unit {i % 12}",
            ["status"] = i % 7 == 0 ? "Overdue" : "Active",
            ["range"] = $"0-{i % 50 + 10} bar",
            ["owner"] = $"Team {i % 9}"
        }).ToList();

    public static FormDefinition Form { get; } = new()
    {
        Id = "spike-form",
        Title = "Register instrument",
        Action = "/b/descriptor-form",
        SubmitLabel = "Save",
        Fields =
        [
        new FieldDescriptor { Name = "Name", Label = "Name", Required = true, MaxLength = 50, HelpText = "Unique tag name" },
        new FieldDescriptor { Name = "Quantity", Label = "Quantity", Kind = FieldKind.Number, Required = true },
        new FieldDescriptor { Name = "Due", Label = "Due date", Kind = FieldKind.Date },
        new FieldDescriptor { Name = "Active", Label = "Active", Kind = FieldKind.Checkbox },
        new FieldDescriptor { Name = "Attachment", Label = "Datasheet", Kind = FieldKind.File }
        ]
    };
}
