namespace HamQso.Spots.Application.Ports;

/// <summary>What a source tells /health about itself.</summary>
public interface ISourceHealth
{
    void Succeeded(string source);

    void Failed(string source, string reason);
}
