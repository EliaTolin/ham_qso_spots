namespace HamQso.Spots.Domain.Rbn;

/// <summary>
/// A node of the RBN node list: the skimmer's call (as in the list, "PA5KT-4"),
/// the locator its owner wrote (not checked here: see <see cref="SkimmerPosition"/>),
/// the bands it listens on, whether it is online and when it was last seen.
/// </summary>
public sealed record RbnNode(string Call, string? Locator, IReadOnlyList<string> Bands, bool Online, DateTimeOffset? LastSeenAt);
