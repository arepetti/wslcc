namespace Wslcc.Abstractions.Compose;

/// <summary>
/// One service mount from <c>volumes:</c> (short or long form) or from service <c>tmpfs:</c>.
/// Sources are unresolved (no project prefix / host path rooting) until the engine resolves them.
/// </summary>
public sealed class ServiceMount
{
    /// <summary>Mount kind: volume, bind, or tmpfs.</summary>
    public MountType Type { get; set; }

    /// <summary>
    /// Volume name or host path. Unused for tmpfs and for anonymous volumes (target only).
    /// </summary>
    public string? Source { get; set; }

    /// <summary>Container path where the mount appears.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>When <c>true</c>, the mount is read-only (<c>ro</c> / <c>read_only: true</c>).</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Compose <c>volume.nocopy</c>.</summary>
    public bool? VolumeNocopy { get; set; }

    /// <summary>Compose <c>volume.subpath</c>.</summary>
    public string? VolumeSubpath { get; set; }

    /// <summary>Compose <c>bind.propagation</c>.</summary>
    public string? BindPropagation { get; set; }

    /// <summary>Compose <c>bind.create_host_path</c> — create the host directory if missing.</summary>
    public bool? BindCreateHostPath { get; set; }

    /// <summary>Compose <c>bind.selinux</c> (<c>z</c> / <c>Z</c>).</summary>
    public string? BindSelinux { get; set; }

    /// <summary>Compose <c>bind.recursive</c>.</summary>
    public string? BindRecursive { get; set; }

    /// <summary>Compose <c>tmpfs.size</c> (bytes or a size string), pass-through.</summary>
    public string? TmpfsSize { get; set; }

    /// <summary>Compose <c>tmpfs.mode</c> permission bits (e.g. <c>1777</c>), without a <c>0o</c> prefix.</summary>
    public string? TmpfsMode { get; set; }

    /// <summary>
    /// Extra tmpfs option tokens from short syntax (e.g. <c>noexec</c>) that are not <c>size</c>/<c>mode</c>.
    /// </summary>
    public string? TmpfsExtraOptions { get; set; }

    /// <summary>
    /// Whether this mount needs <c>docker run --mount</c> instead of plain <c>-v</c>
    /// (volume/bind option blocks beyond <c>read_only</c>).
    /// </summary>
    public bool RequiresMountFlag
    {
        get
        {
            if (Type is MountType.Tmpfs)
                return false;

            return VolumeNocopy == true
                || !string.IsNullOrEmpty(VolumeSubpath)
                || !string.IsNullOrEmpty(BindPropagation)
                || !string.IsNullOrEmpty(BindRecursive);
        }
    }

    /// <summary>
    /// Builds a mount from Compose short syntax <c>[SOURCE:]TARGET[:MODE]</c>.
    /// A single segment is an anonymous volume (target only).
    /// </summary>
    public static ServiceMount FromShortSyntax(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);

        var (source, target, mode) = SplitShortSyntax(raw.Trim());
        if (source is null || target is null)
        {
            // Anonymous volume or malformed: treat a single path as anonymous target.
            return new ServiceMount
            {
                Type = MountType.Volume,
                Target = string.IsNullOrEmpty(target) ? raw.Trim() : target,
            };
        }

        var readOnly = ModeIsReadOnly(mode);
        if (IsBindSource(source))
        {
            return new ServiceMount
            {
                Type = MountType.Bind,
                Source = source,
                Target = target,
                ReadOnly = readOnly,
                BindSelinux = ModeSelinux(mode),
            };
        }

        return new ServiceMount
        {
            Type = MountType.Volume,
            Source = source,
            Target = target,
            ReadOnly = readOnly,
        };
    }

    /// <summary>Builds a tmpfs mount from a service <c>tmpfs:</c> entry (<c>/path</c> or <c>/path:opts</c>).</summary>
    public static ServiceMount FromTmpfsShortSyntax(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);

        var value = raw.Trim();
        var colon = value.IndexOf(':');
        if (colon < 0)
            return new ServiceMount { Type = MountType.Tmpfs, Target = value };

        var mount = new ServiceMount
        {
            Type = MountType.Tmpfs,
            Target = value[..colon],
        };
        ApplyTmpfsOptionSuffix(mount, value[(colon + 1)..]);
        return mount;
    }

    /// <summary>Normalizes Compose octal forms (<c>0o1777</c>) to a bare digit string for <c>mode=</c>.</summary>
    public static string? NormalizeTmpfsMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return null;

        var value = mode.Trim();
        if (value.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            return value[2..];

        return value;
    }

    private static void ApplyTmpfsOptionSuffix(ServiceMount mount, string suffix)
    {
        var extras = new List<string>();
        foreach (var part in suffix.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
            {
                extras.Add(part);
                continue;
            }

            var key = part[..eq];
            var val = part[(eq + 1)..];
            if (string.Equals(key, "size", StringComparison.OrdinalIgnoreCase))
                mount.TmpfsSize = val;
            else if (string.Equals(key, "mode", StringComparison.OrdinalIgnoreCase))
                mount.TmpfsMode = NormalizeTmpfsMode(val);
            else
                extras.Add(part);
        }

        if (extras.Count > 0)
            mount.TmpfsExtraOptions = string.Join(',', extras);
    }

    private static (string? Source, string? Target, string? Mode) SplitShortSyntax(string value)
    {
        if (value.Length == 0)
            return (null, null, null);

        var parts = value.Split(':');

        // Re-join a Windows drive ("C" + "\path") that ':' split apart.
        var startsWithDriveLetter = parts.Length >= 2 && parts[0].Length == 1 && char.IsLetter(parts[0][0]);
        if (startsWithDriveLetter)
            parts = new[] { parts[0] + ":" + parts[1] }.Concat(parts.Skip(2)).ToArray();

        return parts.Length switch
        {
            1 => (null, parts[0], null),
            2 => (parts[0], parts[1], null),
            3 => (parts[0], parts[1], parts[2]),
            _ => (null, null, null),
        };
    }

    /// <summary>Whether a short-syntax source is a host bind path (vs a named volume).</summary>
    public static bool IsBindSource(string source)
        => source.StartsWith('/')
            || source.StartsWith('.')
            || source.StartsWith('~')
            || source.Contains('/')
            || source.Contains('\\')
            || (source.Length >= 2 && char.IsLetter(source[0]) && source[1] == ':');

    private static bool ModeIsReadOnly(string? mode)
    {
        if (string.IsNullOrEmpty(mode))
            return false;

        foreach (var part in mode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(part, "ro", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? ModeSelinux(string? mode)
    {
        if (string.IsNullOrEmpty(mode))
            return null;

        foreach (var part in mode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part is "z" or "Z")
                return part;
        }

        return null;
    }
}
