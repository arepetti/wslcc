namespace Wslcc.Abstractions.Compose;

/// <summary>
/// A single <c>depends_on</c> entry: the name of the service depended upon, the condition that must
/// hold before the dependent starts, and whether the dependency is required. The implicit conversion
/// from <see cref="string"/> models the short list form (<c>depends_on: [db]</c>), which is equivalent
/// to <see cref="DependencyCondition.ServiceStarted"/>.
/// </summary>
/// <param name="Name">Name of the service depended upon.</param>
/// <param name="Condition">What must hold for the dependency before the dependent is started.</param>
/// <param name="Required">
/// Compose <c>required:</c> — when <c>false</c>, a missing or failed dependency is a warning rather
/// than a reason to skip the dependent.
/// </param>
/// <exception cref="ArgumentException"><paramref name="Name"/> is null, empty or whitespace.</exception>
public sealed record ServiceDependency(
    string Name,
    DependencyCondition Condition = DependencyCondition.ServiceStarted,
    bool Required = true)
{
    /// <summary>Name of the service depended upon.</summary>
    public string Name { get; init; } = RequireName(Name);

    /// <summary>Builds a required <see cref="DependencyCondition.ServiceStarted"/> dependency from a bare service name.</summary>
    /// <param name="name">Name of the service depended upon.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static implicit operator ServiceDependency(string name) => new(name);

    private static string RequireName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}
