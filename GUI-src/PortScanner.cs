using System.Diagnostics;
using Microsoft.Win32;

namespace Audi100Diag;

internal sealed record PortInfo(string Name, string Description, bool Present, bool IsFtdi)
{
    public override string ToString() =>
        Present ? $"{Name} – {Description}" : $"{Name} – niepodłączony";
}

/// <summary>Porty COM z rejestru (bez dodatkowych bibliotek) i programy, które mogą je blokować.</summary>
internal static class PortScanner
{
    private static Dictionary<string, (string Description, bool Ftdi)>? _descriptions;
    private static string _descriptionsKey = "";

    public static IReadOnlyList<PortInfo> Scan()
    {
        // SERIALCOMM zawiera tylko porty obecne w tej chwili (np. \Device\VCP0 -> COM1 dla FTDI).
        var present = new List<(string Port, string Device)>();
        using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
        {
            if (key is not null)
            {
                foreach (var name in key.GetValueNames())
                {
                    if (key.GetValue(name) is string port)
                    {
                        present.Add((port, name));
                    }
                }
            }
        }

        var cacheKey = string.Join(";", present.Select(p => p.Port).Order());
        if (_descriptions is null || cacheKey != _descriptionsKey)
        {
            _descriptions = ReadDescriptions();
            _descriptionsKey = cacheKey;
        }

        return present
            .Select(p =>
            {
                var isVcp = p.Device.Contains("VCP", StringComparison.OrdinalIgnoreCase);
                var (description, ftdi) = _descriptions.TryGetValue(p.Port, out var d)
                    ? d
                    : (isVcp ? "USB Serial Port (FTDI)" : p.Device, isVcp);
                return new PortInfo(p.Port, description, true, ftdi || isVcp);
            })
            .OrderByDescending(p => p.IsFtdi)
            .ThenBy(p => int.TryParse(p.Name.AsSpan(3), out var n) ? n : 999)
            .ToList();
    }

    private static Dictionary<string, (string, bool)> ReadDescriptions()
    {
        var map = new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase);
        using var enumKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum");
        if (enumKey is null)
        {
            return map;
        }

        foreach (var bus in new[] { "FTDIBUS", "USB", "BTHENUM", "ACPI" })
        {
            using var busKey = enumKey.OpenSubKey(bus);
            if (busKey is null)
            {
                continue;
            }
            foreach (var deviceName in busKey.GetSubKeyNames())
            {
                try
                {
                    using var device = busKey.OpenSubKey(deviceName);
                    if (device is null)
                    {
                        continue;
                    }
                    foreach (var instanceName in device.GetSubKeyNames())
                    {
                        using var instance = device.OpenSubKey(instanceName);
                        using var parameters = instance?.OpenSubKey("Device Parameters");
                        if (parameters?.GetValue("PortName") is not string port || !port.StartsWith("COM"))
                        {
                            continue;
                        }
                        var ftdi = bus == "FTDIBUS" || deviceName.Contains("VID_0403", StringComparison.OrdinalIgnoreCase);
                        var name = instance!.GetValue("FriendlyName") as string
                                   ?? instance.GetValue("DeviceDesc") as string
                                   ?? port;
                        if (!map.ContainsKey(port) || ftdi)
                        {
                            map[port] = (Clean(name, port), ftdi);
                        }
                    }
                }
                catch (System.Security.SecurityException)
                {
                    // Część kluczy Enum jest niedostępna bez uprawnień administratora - pomijamy.
                }
            }
        }
        return map;
    }

    private static string Clean(string name, string port)
    {
        // "@oem5.inf,%ftdi%;USB Serial Port" -> "USB Serial Port"; "USB Serial Port (COM1)" -> "USB Serial Port"
        var semicolon = name.LastIndexOf(';');
        if (semicolon >= 0)
        {
            name = name[(semicolon + 1)..];
        }
        return name.Replace($"({port})", "", StringComparison.OrdinalIgnoreCase).Trim();
    }

    /// <summary>Programy diagnostyczne, które trzymają port COM i uniemożliwiają połączenie.</summary>
    public static IReadOnlyList<string> ConflictingProcesses(int? ownProcessId)
    {
        var found = new List<string>();
        foreach (var name in new[] { "DIAG_PL", "VagCom", "VCDS", "VagScope", "LCode_PL" })
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length > 0)
            {
                found.Add(name);
            }
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
        foreach (var p in Process.GetProcesses())
        {
            if (p.ProcessName.StartsWith("kw1281test", StringComparison.OrdinalIgnoreCase) && p.Id != ownProcessId)
            {
                found.Add(p.ProcessName + " (SILNIK.bat?)");
            }
            p.Dispose();
        }
        return found;
    }
}
