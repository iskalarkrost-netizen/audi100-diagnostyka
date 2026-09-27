using System.Text.Json;

namespace Audi100Diag;

internal sealed record CodingRef(int Coding, int Workshop);

/// <summary>Ustawienia zapisywane obok programu w Audi100-Diagnostyka.json.</summary>
internal sealed class AppSettings
{
    public string Port { get; set; } = "COM1";
    public int Baud { get; set; } = 4800;
    public int Address { get; set; } = 0x01;
    public int R6 { get; set; } = 0;

    /// <summary>
    /// Kodowanie "znane jako dobre" dla numeru sterownika. Program porównuje z nim każdy
    /// odczyt, bo w 4A0907473A zapis adaptacji potrafi przesunąć bajty kodowania.
    /// </summary>
    public Dictionary<string, CodingRef> ReferenceCoding { get; set; } = new()
    {
        // Odczytane przed jakimikolwiek zapisami (sesja 27.09.2026).
        ["4A0907473A"] = new(17453, 28419),
        ["4A0820043D"] = new(15405, 28419),
    };

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "Audi100-Diagnostyka.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
            }
        }
        catch (Exception)
        {
            // Uszkodzony plik - start z ustawień domyślnych.
        }
        return new();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Brak zapisu ustawień nie może blokować diagnostyki.
        }
    }
}
