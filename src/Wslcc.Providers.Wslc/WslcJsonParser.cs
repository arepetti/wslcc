using System.Text.Json;
using Wslcc.Abstractions;

namespace Wslcc.Providers.Wslc;

/// <summary>Parses the JSON output produced by WSLc 3.0.1 list commands.</summary>
public static class WslcJsonParser
{
    public static IReadOnlyList<ContainerInfo> ParseContainers(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var result = new List<ContainerInfo>();

        foreach (var item in ParseItems(output))
        {
            var labels = ParseLabels(Get(item, "Labels"));
            result.Add(new ContainerInfo(
                Id: Text(Get(item, "ID", "Id")),
                Name: FirstName(Get(item, "Names", "Name")),
                Image: Text(Get(item, "Image")),
                State: Text(Get(item, "State")),
                Status: NullIfEmpty(Text(Get(item, "Status"))),
                Service: Label(labels, WslccLabels.Service),
                Ports: NullIfEmpty(FormatValue(Get(item, "Ports"))),
                Project: Label(labels, WslccLabels.Project),
                ConfigHash: Label(labels, WslccLabels.ConfigHash)));
        }

        return result;
    }

    public static IReadOnlyList<string> ParseNames(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var items = ParseItems(output);
        if (items.Count == 0)
        {
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !line.StartsWith('{') && !line.StartsWith('['))
                .ToArray();
        }

        return items.Select(item => FirstName(Get(item, "Name", "Names")))
            .Where(name => name.Length > 0)
            .ToArray();
    }

    private static IReadOnlyList<JsonElement> ParseItems(string output)
    {
        var text = output.Trim();
        if (text.Length == 0)
            return Array.Empty<JsonElement>();

        try
        {
            using var document = JsonDocument.Parse(text);
            return Flatten(document.RootElement);
        }
        catch (JsonException)
        {
            var items = new List<JsonElement>();
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    items.AddRange(Flatten(document.RootElement));
                }
                catch (JsonException)
                {
                    // WSLc can mix warnings with JSON output. Ignore non-JSON lines.
                }
            }

            return items;
        }
    }

    private static IReadOnlyList<JsonElement> Flatten(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray().Select(item => item.Clone()).ToArray();

        return root.ValueKind == JsonValueKind.Object
            ? new[] { root.Clone() }
            : Array.Empty<JsonElement>();
    }

    private static JsonElement Get(JsonElement item, params string[] names)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return default;

        foreach (var property in item.EnumerateObject())
        {
            if (names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
                return property.Value;
        }

        return default;
    }

    private static string FirstName(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Select(Text).FirstOrDefault(name => name.Length > 0) ?? string.Empty;

        var text = Text(value);
        var comma = text.IndexOf(',');
        return comma < 0 ? text : text[..comma].Trim();
    }

    private static Dictionary<string, string> ParseLabels(JsonElement value)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
                labels[property.Name] = Text(property.Value);
            return labels;
        }

        var values = value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(Text)
            : Text(value).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in values)
        {
            var equals = entry.IndexOf('=');
            if (equals > 0)
                labels[entry[..equals]] = entry[(equals + 1)..];
        }

        return labels;
    }

    private static string? Label(IReadOnlyDictionary<string, string> labels, string key)
        => labels.TryGetValue(key, out var value) ? value : null;

    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
        _ => string.Empty,
    };

    private static string FormatValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(FormatValue)),
        JsonValueKind.Object => value.ToString(),
        _ => Text(value),
    };

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
