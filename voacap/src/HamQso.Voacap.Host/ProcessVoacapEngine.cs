using System.Diagnostics;
using HamQso.Voacap.Core;
using Microsoft.Extensions.Options;

namespace HamQso.Voacap.Host;

/// <summary>
/// voacapl as a process: each run in its own folder (--run-dir), the antenna
/// files written once into itshfbc/antennas (named by their hash), stdin closed
/// (voacapl pauses on an error and waits for "go"), killed after the timeout.
/// voacapl exits 0 even when it fails: a run is good only if its output ends.
/// </summary>
public sealed class ProcessVoacapEngine(IOptions<VoacapOptions> options) : IVoacapEngine, IDisposable
{
    private readonly SemaphoreSlim slots = new(options.Value.MaxParallel);

    public string Name => options.Value.Engine;

    public async Task<string> RunAsync(string deck, IReadOnlyList<AntennaFile> antennas, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(antennas);
        var o = options.Value;
        foreach (var antenna in antennas.Where(a => a.Content is not null)) await WriteAntennaAsync(o.Itshfbc, antenna, cancellationToken).ConfigureAwait(false);

        await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        var dir = Directory.CreateTempSubdirectory("voacap-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir.FullName, "in.dat"), deck, cancellationToken).ConfigureAwait(false);
            var said = await RunProcessAsync(o, dir.FullName, cancellationToken).ConfigureAwait(false);
            var outFile = Path.Combine(dir.FullName, "out.out");
            var output = File.Exists(outFile) ? await File.ReadAllTextAsync(outFile, cancellationToken).ConfigureAwait(false) : "";
            if (!VoacapOutput.Completed(output)) throw new VoacapEngineException(VoacapOutput.ErrorMessage(said) is { Length: > 0 } m ? m : "voacapl gave no output");
            return output;
        }
        finally
        {
            slots.Release();
            try
            {
                dir.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    public void Dispose() => slots.Dispose();

    private static async Task<string> RunProcessAsync(VoacapOptions o, string runDir, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(o.Executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = runDir,
        };
        foreach (var arg in new[] { "-s", $"--run-dir={runDir}", o.Itshfbc, "in.dat", "out.out" }) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new VoacapEngineException($"voacapl cannot start: {e.Message}", e);
        }
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(o.RunTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            cancellationToken.ThrowIfCancellationRequested();
            throw new VoacapEngineException($"voacapl took more than {o.RunTimeout.TotalSeconds:0} s");
        }
        return (await stdout.ConfigureAwait(false)) + "\n" + (await stderr.ConfigureAwait(false));
    }

    /// <summary>Written once (the name is the hash of the text), through a temporary file so a parallel run never reads half of it.</summary>
    private static async Task WriteAntennaAsync(string itshfbc, AntennaFile antenna, CancellationToken cancellationToken)
    {
        var path = Path.Combine(itshfbc, "antennas", antenna.Name);
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temp, antenna.Content, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }
}
