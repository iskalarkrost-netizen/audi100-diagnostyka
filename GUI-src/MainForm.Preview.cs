namespace Audi100Diag;

// Tylko dla --zrzut: przepuszcza linie w formacie kw1281test przez te same parsery i widoki,
// z których korzysta prawdziwy pomiar. Linie ECU, kodowania i adaptacji pochodzą z KW1281Test.log (27.09.2026).
internal sealed partial class MainForm
{
    private void LoadPreviewData()
    {
        AbsorbControllerState(
        [
            "ECU: 4A0907473A  2,6l V6 MPFI        D09",
            "Software Coding 17453, Workshop Code: 28419",
        ]);
        ShowFaults(KwOutput.ParseFaults(["Fault codes:", "Sending EndCommunication block"]).Codes);

        // Grupa 1 - format SensorValue.ToString (wartości przykładowe).
        foreach (var rpm in new[] { 832.0, 825.6, 819.2, 825.6, 812.8, 819.2, 806.4, 812.8, 819.2 })
        {
            OnLiveLine($"Group 001: {rpm:0.0} rpm | 95,6 % | 1,4 °BTDC | 108,0");
        }

        foreach (var line in new[]
        {
            "Received \"Adaptation Response\" block: 01 00 05 E7 01 40 42",
            "Received \"Adaptation Response\" block: 01 00 05 E7 01 40 41",
            "Received \"Adaptation Response\" block: 01 00 00 E7 01 40 40",
            "Received \"Adaptation Response\" block: 01 00 00 E7 01 40 3E",
            "Received \"Adaptation Response\" block: 01 00 00 E7 01 40 3D",
            "Received \"Adaptation Response\" block: 01 00 00 E7 01 40 3C",
            "Received \"Adaptation Response\" block: 01 00 00 E7 01 40 3D",
            "Received \"Adaptation Response\" block: 01 00 00 E7 01 40 3D",
        })
        {
            OnAdaptationLine(line);
        }

        ShowScan(KwOutput.ParseScan(["AutoScan Results:", "KWP1281: 1 8", "KWP2000: ", "Woke up but comms failed: "]));
        AppendLog("(podgląd – dane z logów, bez połączenia z autem)");
    }
}
