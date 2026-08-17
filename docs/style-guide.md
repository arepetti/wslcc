# C# style guide

Conventions that differ from (or go beyond) default C# / `dotnet format` habits. Shared IDE suggestions also live in [`.editorconfig`](../.editorconfig); this page is the human-readable source of truth for review.

Build / package / architecture conventions remain in [CONTRIBUTING.md](../CONTRIBUTING.md).

---

## 1. Braces

Omit `{` / `}` when the body is a **single simple statement** on the next line:

```csharp
if (path is null)
    throw new ArgumentNullException(nameof(path));

foreach (var item in items)
    result.Add(item);

if (!done)
    continue;
```

Keep braces when the body is complex, multi-statement, or spans multiple lines (including a multi-line expression statement).

### `if` / `else` consistency

If **either** branch needs braces, **both** get braces:

```csharp
if (ok)
{
    return value;
}
else
{
    Log(error);
    throw new InvalidOperationException(error);
}
```

Do **not** mix braced and brace-less branches on the same `if`/`else`.

### Loops

`foreach` follows the same brace rules as `if`. Prefer keeping braces on **`while`** loops whose body advances the loop variable (character scanners, parsers) — brace-less `while` is easy to misread when the increment is not on the `while` line itself.

---

## 2. Null checks

Prefer pattern forms over `== null` / `!= null`:

| Prefer | Avoid |
| --- | --- |
| `x is null` | `x == null` |
| `x is not null` | `x != null` |

Declaration patterns are fine: `if (value is { } map)`, `if (name is { Length: > 0 } s)`.

---

## 3. Conditions

**Do not** put multi-line or dense boolean logic in the `if (` … `)` header. Extract a named local (or a small helper) first:

```csharp
// Bad
if (YamlGraph.AsMap(service) is { } serviceMap
    && YamlGraph.AsList(serviceMap.TryGetValue("profiles", out var p) ? p : null) is { } list)
{
    …
}

// Good
var profiles = TryGetProfilesList(service);
if (profiles is null)
    continue;

foreach (var profile in profiles)
    …
```

Names should say what is being tested (`hasProfiles`, `profilesList`, `isExternal`), not `flag` or `tmp`.

---

## 4. Method length and shape

- Prefer methods of roughly **≤ 25–30** non-blank, non-comment lines.
- A **public** (or other orchestrating) method should read as an **index**: a short sequence of named steps that call private helpers — not a deep nest of implementation detail.
- Split when a block needs its own name to stay readable, not for its own sake.

---

## 5. Argument validation

Every **public** method and constructor on a **public** type validates its parameters before use:

| Kind | Prefer |
| --- | --- |
| Reference that must not be null | `ArgumentNullException.ThrowIfNull(arg)` |
| Required string (name, path, id, image, …) | `ArgumentException.ThrowIfNullOrWhiteSpace(arg)` |
| Numeric range | `ArgumentOutOfRangeException` (e.g. `ThrowIfNegative`) |
| Collection that must be non-empty | `ArgumentException` / domain exception after a null check |

```csharp
public static string BuildPullArguments(string image)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(image);
    return Join(new[] { "pull", image });
}
```

Notes:

- Optional parameters (`string?`, `IProgress<T>?`, `CancellationToken`) are not “required”; only validate when the caller actually supplies a value that must be well-formed (e.g. `tail is { } n` → `n >= 0`).
- Prefer **argument** exceptions at API boundaries. Keep domain exceptions (`ComposeLoadException`, `ProviderException`) for semantic failures after parameters are known to be present (missing compose files, image has no context, CLI failure).
- Property-bag DTOs do not need setter validation; validate when the bag is consumed (engine / command builder).
- Document thrown argument exceptions in XML (`<exception cref="ArgumentNullException">` / `ArgumentException` / …).

---

## 6. XML documentation

Every **public** member of every **public** type gets `<summary>` (plus `<param>` / `<returns>` / `<exception>` / `<remarks>` when useful).

Important types (`IComposeEngine`, `IContainerProvider`, `ComposeLoader`, `ComposeFileParser`, `CliCommandBuilder`, key DTOs) should include an `<example>` when usage is non-obvious.

Implementations may use `<inheritdoc/>` when the interface already documents the member.

---

## 7. Comments that cite the Compose file

When code intentionally follows (or deliberately diverges from) Compose file semantics, cite by **section title**, not fragile section numbers:

```csharp
// Compose file — command: string is shell form (/bin/sh -c), list is exec.
// Compose file — ports: long map form is rejected; short syntax only.
```

Format reference: [compose-file.md](compose-file.md). Support status lives only in [compatibility.md](compatibility.md).

Comments explain **intent / trade-offs / quirks**, not what the next line obviously does.

---

## 8. Other project norms (quick)

| Topic | Convention |
| --- | --- |
| Namespaces | File-scoped (`namespace Foo;`) |
| Indent | 4 spaces |
| Nullable | Enabled project-wide |
| Targets | `net10.0` only |
| Packages | Central Package Management ([Directory.Packages.props](../Directory.Packages.props)) |
| WSL SDK | Behind `WSLC_SDK`; do not sprinkle `#if` elsewhere |

`.editorconfig` suggestions are not CI-enforced today; reviews still expect this guide.
