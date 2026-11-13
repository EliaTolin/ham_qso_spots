using HamQso.Spots.Domain.Protection;

namespace HamQso.Spots.Application.Ports;

/// <summary>The callsigns to protect: profiles with the protection on (Supabase).</summary>
public interface IProtectedCallsignSource
{
    Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>A source that could not be read, as the backend takes it.</summary>
public sealed record SourceFailure(string Source, string Message);

/// <summary>A batch for callsign_protection: the uses, and which sources were checked or not readable.</summary>
public sealed record ProtectionBatch(IReadOnlyList<ProtectedUse> Uses, IReadOnlyList<string> Checked, IReadOnlyList<SourceFailure> Failed);

/// <summary>The backend said no and will say it again (a 4xx): never retried.</summary>
public sealed class UplinkRejectedException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>The one way to the backend (callsign_protection). Throws <see cref="UplinkRejectedException"/> for a 4xx, anything else is worth a retry.</summary>
public interface IProtectionUplink
{
    Task SendAsync(ProtectionBatch batch, CancellationToken cancellationToken);
}

/// <summary>Where the accepted cluster spots are offered to the protection.</summary>
public interface IProtectionTap
{
    void OfferSpots(IEnumerable<AlertSpot> accepted);
}

/// <summary>Is a source of the protection working? Null: yes; otherwise what is wrong.</summary>
public interface IProtectionProbe
{
    ProtectionSignal Signal { get; }

    string? Problem();
}
