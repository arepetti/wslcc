using Wslcc.Abstractions.Compose;
using YamlDotNet.Serialization;

namespace Wslcc.Compose;

/// <summary>
/// Parses Compose YAML into <see cref="ComposeFile"/>. This is a tolerant parser that understands
/// the common short/long forms of the most-used keys. Full Compose specification fidelity is tracked
/// in docs/todo.md.
/// </summary>
public sealed class ComposeFileParser
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();

    public ComposeFile ParseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must be provided.", nameof(path));
        }

        return Parse(File.ReadAllText(path), path);
    }

    public ComposeFile Parse(string yaml, string? source = null)
    {
        var root = _deserializer.Deserialize<object?>(yaml);
        var map = AsMap(root);
        var file = new ComposeFile();

        if (map is null)
        {
            return file;
        }

        file.Name = GetString(map, "name");

        if (AsMap(GetValue(map, "services")) is { } services)
        {
            foreach (var kvp in services)
            {
                file.Services[kvp.Key] = ParseService(kvp.Key, kvp.Value);
            }
        }

        if (AsMap(GetValue(map, "networks")) is { } networks)
        {
            foreach (var kvp in networks)
            {
                file.Networks[kvp.Key] = ParseNetwork(kvp.Key, kvp.Value);
            }
        }

        if (AsMap(GetValue(map, "volumes")) is { } volumes)
        {
            foreach (var kvp in volumes)
            {
                file.Volumes[kvp.Key] = ParseVolume(kvp.Key, kvp.Value);
            }
        }

        return file;
    }

    private static ServiceSpec ParseService(string name, object? value)
    {
        var service = new ServiceSpec { Name = name };
        var map = AsMap(value);
        if (map is null)
        {
            return service;
        }

        service.Image = GetString(map, "image");
        service.ContainerName = GetString(map, "container_name");
        service.Restart = GetString(map, "restart");
        service.WorkingDir = GetString(map, "working_dir");
        service.User = GetString(map, "user");

        service.Build = ParseBuild(GetValue(map, "build"));
        service.Command = ToStringList(GetValue(map, "command"));
        service.Entrypoint = ToStringList(GetValue(map, "entrypoint"));
        service.Environment = ToKeyValues(GetValue(map, "environment"));
        service.EnvFile = ToStringList(GetValue(map, "env_file"));
        service.Ports = ToStringList(GetValue(map, "ports"));
        service.Volumes = ToStringList(GetValue(map, "volumes"));
        service.DependsOn = ToKeyList(GetValue(map, "depends_on"));
        service.Networks = ToKeyList(GetValue(map, "networks"));
        service.Labels = ToNonNullKeyValues(GetValue(map, "labels"));

        return service;
    }

    private static BuildSpec? ParseBuild(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string context:
                return new BuildSpec { Context = context };
            default:
                var map = AsMap(value);
                if (map is null)
                {
                    return null;
                }

                return new BuildSpec
                {
                    Context = GetString(map, "context"),
                    Dockerfile = GetString(map, "dockerfile"),
                    Target = GetString(map, "target"),
                    Args = ToKeyValues(GetValue(map, "args")),
                };
        }
    }

    private static NetworkSpec ParseNetwork(string name, object? value)
    {
        var map = AsMap(value);
        return new NetworkSpec
        {
            Name = name,
            Driver = map is null ? null : GetString(map, "driver"),
            External = map is not null && GetBool(map, "external"),
        };
    }

    private static VolumeSpec ParseVolume(string name, object? value)
    {
        var map = AsMap(value);
        return new VolumeSpec
        {
            Name = name,
            Driver = map is null ? null : GetString(map, "driver"),
            External = map is not null && GetBool(map, "external"),
        };
    }

    // --- YAML graph helpers -------------------------------------------------

    private static IDictionary<string, object?>? AsMap(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case IDictionary<string, object?> typed:
                return typed;
            case IDictionary<object, object?> raw:
                var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var kvp in raw)
                {
                    normalized[Convert.ToString(kvp.Key) ?? string.Empty] = kvp.Value;
                }

                return normalized;
            default:
                return null;
        }
    }

    private static IList<object?> AsList(object? value)
    {
        switch (value)
        {
            case null:
                return new List<object?>();
            case string s:
                return new List<object?> { s };
            case System.Collections.IEnumerable enumerable:
                var list = new List<object?>();
                foreach (var item in enumerable)
                {
                    list.Add(item);
                }

                return list;
            default:
                return new List<object?> { value };
        }
    }

    private static object? GetValue(IDictionary<string, object?> map, string key)
        => map.TryGetValue(key, out var value) ? value : null;

    private static string? GetString(IDictionary<string, object?> map, string key)
        => GetValue(map, key) is { } value ? Convert.ToString(value) : null;

    private static bool GetBool(IDictionary<string, object?> map, string key)
        => GetValue(map, key) is { } value && bool.TryParse(Convert.ToString(value), out var b) && b;

    private static IList<string> ToStringList(object? value)
        => AsList(value)
            .Where(v => v is not null)
            .Select(v => Convert.ToString(v) ?? string.Empty)
            .ToList();

    /// <summary>Keys of a map, or items of a list (used by depends_on / networks).</summary>
    private static IList<string> ToKeyList(object? value)
    {
        if (AsMap(value) is { } map)
        {
            return map.Keys.ToList();
        }

        return ToStringList(value);
    }

    /// <summary>Reads a map or a list of "KEY=VALUE" entries into a dictionary (nullable values).</summary>
    private static IDictionary<string, string?> ToKeyValues(object? value)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (AsMap(value) is { } map)
        {
            foreach (var kvp in map)
            {
                result[kvp.Key] = kvp.Value is null ? null : Convert.ToString(kvp.Value);
            }

            return result;
        }

        foreach (var item in ToStringList(value))
        {
            var index = item.IndexOf('=');
            if (index < 0)
            {
                result[item] = null;
            }
            else
            {
                result[item.Substring(0, index)] = item.Substring(index + 1);
            }
        }

        return result;
    }

    private static IDictionary<string, string> ToNonNullKeyValues(object? value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kvp in ToKeyValues(value))
        {
            result[kvp.Key] = kvp.Value ?? string.Empty;
        }

        return result;
    }
}
