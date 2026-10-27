namespace HamQso.Spots.Domain.Radio;

/// <summary>Same grouping as the app's ModeCategory.</summary>
public enum ModeCategory
{
    Cw,
    Phone,
    Digital,
}

/// <summary>Where a spot's mode comes from: reported = source/comment/skimmer, band plan = frequency.</summary>
public enum ModeSource
{
    Reported,
    BandPlan,
}

/// <summary>The band plan a station follows.</summary>
public enum IaruRegion
{
    R1,
    R2,
    R3,
    Fcc,
}

/// <summary>A resolved mode: <see cref="Mode"/> is empty when it cannot be told.</summary>
public sealed record ResolvedMode(string Mode, ModeSource? Source);
