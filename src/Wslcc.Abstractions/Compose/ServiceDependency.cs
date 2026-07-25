namespace Wslcc.Abstractions.Compose;

/// <summary>
/// A single <c>depends_on</c> entry: the name of the service depended upon, the condition that must
/// hold before the dependent starts, and whether the dependency is required. The implicit conversion
/// from <see cref="string"/> models the short list form (<c>depends_on: [db]</c>), which is equivalent
/// to <see cref="DependencyCondition.ServiceStarted"/>.
/// </summary>
public sealed record ServiceDependency(
    string Name,
    DependencyCondition Condition = DependencyCondition.ServiceStarted,
    bool Required = true)
{
    public static implicit operator ServiceDependency(string name) => new(name);
}
