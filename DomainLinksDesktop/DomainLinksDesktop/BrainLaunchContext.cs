namespace DomainLinksDesktop;

public enum BrainScopeKind
{
    Domain,
    Collection,
    Document,
    Policy,
    Control,
}

public sealed record BrainLaunchContext(
    BrainScopeKind ScopeKind,
    string Identifier,
    string? DisplayName = null,
    bool IncludeDescendants = true,
    string? FocusNodeId = null)
{
    public static BrainLaunchContext InformationManagement { get; } =
        new(BrainScopeKind.Domain, "information-management", "Information Management");

    public string ScopeKindValue => ScopeKind.ToString().ToLowerInvariant();
}
