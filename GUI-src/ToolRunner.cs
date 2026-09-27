using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Audi100Diag;

internal sealed record ToolResult(IReadOnlyList<string> Lines, int ExitCode, bool Stopped, TimeSpan Elapsed)
{
    public string? Problem { get; init; }
}

/// <summary>
/// Uruchamia kw1281test jako osobny proces i przekazuje jego wyjście linia po linii.
/// Jedno polecenie naraz - port COM może mieć otwarty tylko jeden program.
/// </summary>
internal sealed class ToolRunner
{
    private static readonly string[] ToolNames =
        ["kw1281test-FIXED3.exe", "kw1281test-FIXED2.exe", "kw1281test-FIXED.exe"];

    private readonly SynchronizationContext _ui;
    private Process? _process;
    private string? _stopFile;

    private ToolRunner(string exePath, SynchronizationContext ui)
    {
        ExePath = exePath;
        _ui = ui;
    }

    public string ExePath { get; }
    public string ToolName => Path.GetFileNameWithoutExtension(ExePath);

    /// <summary>FIXED3+: AdaptationWatch i czyste zatrzymanie pętli przez KW1281_STOPFILE.</summary>
    public bool SupportsStopFile { get; private set; }

    public bool IsBusy => _process is not null;
    public bool CurrentIsWrite { get; private set; }
    public bool CurrentIsLoop { get; private set; }
    public int? CurrentProcessId { get; private set; }
    private bool _stopRequested;

    public event Action<string>? LineReceived;

    public static async Task<ToolRunner?> CreateAsync()
    {
        var dirs = new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Audi100-VAG"),
        };
        var exe = ToolNames
            .SelectMany(name => dirs.Select(dir => Path.Combine(dir, name)))
            .FirstOrDefault(File.Exists);
        if (exe is null)
        {
            return null;
        }

        var runner = new ToolRunner(exe, SynchronizationContext.Current ?? new SynchronizationContext());
        runner.SupportsStopFile = await Task.Run(() => HelpMentions(exe, "AdaptationWatch"));
        return runner;
    }

    private static bool HelpMentions(string exe, string text)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath(), // pomoc nie powinna dopisywać do KW1281Test.log
            })!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10_000);
            return output.Contains(text, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<ToolResult> RunAsync(
        IReadOnlyList<string> args, IReadOnlyDictionary<string, string> environment, bool isWrite, bool isLoop)
    {
        if (_process is not null)
        {
            throw new InvalidOperationException("Narzędzie już działa.");
        }

        // Narzędzie ma ukrytą konsolę, więc pisze w stronie kodowej OEM (852 dla polskiego Windows).
        var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(ExePath)!, // KW1281Test.log obok narzędzia, jak dotąd
            StandardOutputEncoding = oem,
            StandardErrorEncoding = oem,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        foreach (var (k, v) in environment)
        {
            psi.Environment[k] = v;
        }
        _stopFile = Path.Combine(Path.GetTempPath(), $"audi100diag-stop-{Guid.NewGuid():N}");
        psi.Environment["KW1281_STOPFILE"] = _stopFile;

        var lines = new List<string>();
        void OnData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is null)
            {
                return;
            }
            lock (lines)
            {
                lines.Add(e.Data);
            }
            _ui.Post(_ => LineReceived?.Invoke(e.Data), null);
        }

        var process = new Process { StartInfo = psi };
        process.OutputDataReceived += OnData;
        process.ErrorDataReceived += OnData;

        var stopwatch = Stopwatch.StartNew();
        _stopRequested = false;
        CurrentIsWrite = isWrite;
        CurrentIsLoop = isLoop;
        try
        {
            process.Start();
            _process = process;
            CurrentProcessId = process.Id;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            // WaitForExit() bez limitu czeka też na ostatnie linie wyjścia - dzięki temu wszystkie
            // linie trafiają do interfejsu przed zakończeniem tej metody.
            await Task.Run(() => process.WaitForExit());
        }
        finally
        {
            _process = null;
            CurrentProcessId = null;
            TryDelete(_stopFile);
        }

        string[] snapshot;
        lock (lines)
        {
            snapshot = [.. lines];
        }
        var result = new ToolResult(snapshot, process.ExitCode, _stopRequested, stopwatch.Elapsed);
        process.Dispose();
        return result;
    }

    /// <summary>
    /// clean=true: pętla (grupy, podgląd, test) kończy się sama i wysyła EndCommunication.
    /// Jeśli narzędzie nie zareaguje w ciągu 8 s albo clean=false - proces jest zabijany.
    /// </summary>
    public void RequestStop(bool clean)
    {
        var process = _process;
        if (process is null)
        {
            return;
        }
        _stopRequested = true;

        if (clean && SupportsStopFile && _stopFile is not null)
        {
            try
            {
                File.WriteAllText(_stopFile, "stop");
            }
            catch (Exception)
            {
                Kill(process);
                return;
            }
            _ = KillIfStillRunningAsync(process, TimeSpan.FromSeconds(8));
        }
        else
        {
            Kill(process);
        }
    }

    private async Task KillIfStillRunningAsync(Process process, TimeSpan after)
    {
        await Task.Delay(after);
        if (ReferenceEquals(_process, process))
        {
            Kill(process);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Proces zdążył się zakończyć.
        }
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Plik tymczasowy - nieistotne.
        }
    }
}
