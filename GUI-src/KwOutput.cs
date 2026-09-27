using System.Globalization;
using System.Text.RegularExpressions;

namespace Audi100Diag;

internal sealed record EcuIdent(string PartNumber, string Description, string Version);
internal sealed record CodingInfo(int Coding, int Workshop);
internal sealed record FaultCode(int Code, string Status);
internal sealed record FaultReadout(bool Received, bool Cleared, IReadOnlyList<FaultCode> Codes);
internal sealed record AdaptationReading(int Channel, int Value, double? Rpm, string Hex);
internal sealed record GroupReading(int Group, bool Available, IReadOnlyList<string> Fields);
internal sealed record ScanResult(bool Finished, IReadOnlyList<int> Kwp1281, IReadOnlyList<int> Kwp2000, IReadOnlyList<int> Partial);

internal enum CodingGuard { NotReported, Intact, ChangedAndRestored, ChangedNotRestored }

/// <summary>
/// Parsuje tekstowe wyjście kw1281test. Formaty linii pochodzą z logów sesji
/// 20.09 i 27.09.2026 oraz ze źródeł narzędzia.
/// </summary>
internal static partial class KwOutput
{
    [GeneratedRegex(@"^(?:ECU|Ident):\s+(\S+)\s+(.*?)(?:\s{2,}(\S+))?\s*$")]
    private static partial Regex IdentRx();

    [GeneratedRegex(@"Software Coding (\d+), Workshop Code: (\d+)")]
    private static partial Regex CodingRx();

    [GeneratedRegex(@"^\s+(\d{5}) - (\S+)")]
    private static partial Regex FaultRx();

    [GeneratedRegex(@"Adaptation Response"" block:\s*(.+)$")]
    private static partial Regex AdaptationRx();

    [GeneratedRegex(@"^Group (\d{3}): (.*)$")]
    private static partial Regex GroupRx();

    [GeneratedRegex(@"^\s*([-+]?\d+(?:[.,]\d+)?)\s*(.*)$")]
    private static partial Regex NumberRx();

    [GeneratedRegex(@"wake up controller at address ([0-9A-F]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ScanProgressRx();

    public static EcuIdent? ParseIdent(IEnumerable<string> lines)
    {
        EcuIdent? found = null;
        foreach (var line in lines)
        {
            var m = IdentRx().Match(line);
            if (m.Success)
            {
                found = new EcuIdent(m.Groups[1].Value, m.Groups[2].Value.Trim(), m.Groups[3].Value);
            }
        }
        return found;
    }

    public static CodingInfo? ParseCoding(IEnumerable<string> lines)
    {
        CodingInfo? found = null;
        foreach (var line in lines)
        {
            var m = CodingRx().Match(line);
            if (m.Success)
            {
                found = new CodingInfo(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            }
        }
        return found;
    }

    public static FaultReadout ParseFaults(IReadOnlyList<string> lines)
    {
        var cleared = lines.Any(l => l.Contains("Fault codes cleared."));
        var header = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim() == "Fault codes:")
            {
                header = i;
            }
        }
        if (header < 0)
        {
            return new FaultReadout(cleared, cleared, []);
        }

        var codes = new List<FaultCode>();
        for (var i = header + 1; i < lines.Count; i++)
        {
            var m = FaultRx().Match(lines[i]);
            if (!m.Success)
            {
                break;
            }
            codes.Add(new FaultCode(int.Parse(m.Groups[1].Value), m.Groups[2].Value));
        }
        return new FaultReadout(true, cleared, codes);
    }

    /// <summary>
    /// Blok odpowiedzi adaptacji: [0]=kanał, [1..2]=wartość, [3]=znacznik, [4..6]=(SensorID, A, B).
    /// SensorID 1 = obroty: 0.2 * A * B obr/min (SensorValue.cs w kw1281test).
    /// </summary>
    public static AdaptationReading? ParseAdaptation(string line)
    {
        var m = AdaptationRx().Match(line);
        if (!m.Success)
        {
            return null;
        }

        var bytes = new List<byte>();
        foreach (var part in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                bytes.Add(b);
            }
        }
        if (bytes.Count < 3)
        {
            return null;
        }

        double? rpm = bytes.Count >= 7 && bytes[4] == 1 ? 0.2 * bytes[5] * bytes[6] : null;
        return new AdaptationReading(
            bytes[0], bytes[1] * 256 + bytes[2], rpm, string.Join(" ", bytes.Select(b => b.ToString("X2"))));
    }

    public static AdaptationReading? LastAdaptation(IEnumerable<string> lines)
    {
        AdaptationReading? last = null;
        foreach (var line in lines)
        {
            last = ParseAdaptation(line) ?? last;
        }
        return last;
    }

    public static GroupReading? ParseGroup(string line)
    {
        var m = GroupRx().Match(line);
        if (!m.Success)
        {
            return null;
        }

        var group = int.Parse(m.Groups[1].Value);
        var body = m.Groups[2].Value.Trim();
        if (body.StartsWith("Not Available", StringComparison.OrdinalIgnoreCase))
        {
            return new GroupReading(group, false, []);
        }
        return new GroupReading(group, true, body.Split(" | ", StringSplitOptions.TrimEntries));
    }

    /// <summary>"95,6 %" -> 95.6 i "%".</summary>
    public static bool TryNumber(string field, out double value, out string unit)
    {
        value = 0;
        unit = "";
        var m = NumberRx().Match(field);
        if (!m.Success)
        {
            return false;
        }
        unit = m.Groups[2].Value.Trim();
        return double.TryParse(
            m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static int? ParseScanProgress(string line)
    {
        var m = ScanProgressRx().Match(line);
        return m.Success ? int.Parse(m.Groups[1].Value, NumberStyles.HexNumber) : null;
    }

    public static ScanResult ParseScan(IReadOnlyList<string> lines)
    {
        static List<int> Addresses(IReadOnlyList<string> lines, string prefix)
        {
            var line = lines.LastOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
            if (line is null)
            {
                return [];
            }
            return line[prefix.Length..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a) ? a : -1)
                .Where(a => a >= 0)
                .Distinct()
                .ToList();
        }

        return new ScanResult(
            lines.Any(l => l.StartsWith("AutoScan Results:", StringComparison.Ordinal)),
            Addresses(lines, "KWP1281:"),
            Addresses(lines, "KWP2000:"),
            Addresses(lines, "Woke up but comms failed:"));
    }

    public static CodingGuard ParseCodingGuard(IReadOnlyList<string> lines)
    {
        if (lines.Any(l => l.Contains("Coding restored and verified")))
        {
            return CodingGuard.ChangedAndRestored;
        }
        if (lines.Any(l => l.Contains("changed the controller's coding")))
        {
            return CodingGuard.ChangedNotRestored;
        }
        if (lines.Any(l => l.Contains("Coding intact")))
        {
            return CodingGuard.Intact;
        }
        return CodingGuard.NotReported;
    }

    /// <summary>Tłumaczy typowe niepowodzenia na zrozumiały komunikat; null = bez problemu.</summary>
    public static string? Diagnose(IReadOnlyList<string> lines)
    {
        bool Has(string s) => lines.Any(l => l.Contains(s, StringComparison.OrdinalIgnoreCase));

        if (Has("AutoScan Results:"))
        {
            return null; // Przy skanowaniu przekroczenia czasu na pustych adresach są normalne.
        }
        if (Has("Access to the path") || Has("UnauthorizedAccess"))
        {
            return "port zajęty przez inny program (zamknij DIAG_PL / VAG-COM)";
        }
        if (Has("does not exist") || Has("Could not find file 'COM"))
        {
            return "port nie istnieje - kabel odłączony?";
        }

        var connected = Has("Protocol is KW");
        if (!connected && Has("Unexpected sync byte"))
        {
            return "sterownik odpowiada, ale nie synchronizuje się - sprawdź prędkość (silnik: 4800)";
        }
        if (!connected && Has("Sending wakeup message"))
        {
            return "sterownik nie odpowiada - zapłon włączony? właściwy adres?";
        }
        if (Has("Received block end") || Has("Partial block"))
        {
            return "błąd odbioru bloku - dla silnika ustaw R6 = 0 ms";
        }
        if (Has("Received a NAK"))
        {
            return "sterownik odrzucił polecenie (NAK) - np. kanał nie istnieje";
        }
        if (Has("Unhandled exception") || Has("Caught:"))
        {
            return "błąd narzędzia - szczegóły w zakładce Log";
        }
        return null;
    }
}
