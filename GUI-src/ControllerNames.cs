namespace Audi100Diag;

internal static class ControllerNames
{
    /// <summary>Standardowe adresy diagnostyczne VAG (szesnastkowo).</summary>
    public static readonly (int Address, string Name)[] Known =
    [
        (0x01, "Silnik"),
        (0x02, "Skrzynia biegów"),
        (0x03, "ABS"),
        (0x08, "Klimatyzacja"),
        (0x15, "Poduszki powietrzne"),
        (0x17, "Zegary"),
        (0x25, "Immobilizer"),
        (0x35, "Centralny zamek"),
        (0x45, "Alarm / czujniki wnętrza"),
        (0x56, "Radio"),
    ];

    public static string Describe(int address)
    {
        var name = Known.FirstOrDefault(k => k.Address == address).Name;
        return name is null ? $"{address:X2}" : $"{address:X2} – {name}";
    }

    /// <summary>
    /// Opisy wybranych kodów usterek VAG (ogólny słownik, tylko te pewne).
    /// Dokładne znaczenie zależy od sterownika - traktuj jako wskazówkę.
    /// </summary>
    public static readonly Dictionary<int, string> FaultTexts = new()
    {
        [00513] = "Czujnik obrotów silnika (G28)",
        [00515] = "Czujnik Halla (G40)",
        [00516] = "Włącznik biegu jałowego przepustnicy (F60)",
        [00518] = "Potencjometr przepustnicy (G69)",
        [00519] = "Czujnik ciśnienia w kolektorze ssącym (G71)",
        [00522] = "Czujnik temperatury płynu chłodzącego (G62)",
        [00523] = "Czujnik temperatury powietrza zasysanego (G42)",
        [00524] = "Czujnik spalania stukowego 1 (G61)",
        [00525] = "Sonda lambda (G39)",
        [00532] = "Napięcie zasilania B+",
        [00533] = "Regulacja obrotów biegu jałowego",
        [00537] = "Regulacja lambda",
        [00540] = "Czujnik spalania stukowego 2 (G66)",
        [00543] = "Przekroczone maksymalne obroty silnika",
        [00545] = "Połączenie elektryczne silnik – skrzynia biegów",
        [00553] = "Przepływomierz powietrza (G70)",
        [00561] = "Adaptacja składu mieszanki",
        [00625] = "Sygnał prędkości pojazdu",
        [00668] = "Napięcie zasilania – zacisk 30",
        [01247] = "Zawór odpowietrzenia zbiornika z węglem aktywnym (N80)",
        [01249] = "Wtryskiwacz cylindra 1 (N30)",
        [01250] = "Wtryskiwacz cylindra 2 (N31)",
        [01251] = "Wtryskiwacz cylindra 3 (N32)",
        [01252] = "Wtryskiwacz cylindra 4 (N33)",
        [01253] = "Wtryskiwacz cylindra 5 (N83)",
        [01254] = "Wtryskiwacz cylindra 6 (N84)",
        [01259] = "Przekaźnik pompy paliwa (J17)",
        [65535] = "Błąd wewnętrzny sterownika",
    };
}
