using System.Globalization;

namespace Audi100Diag;

// Zakładka "Kodowanie": porównanie z kodowaniem referencyjnym i zapis kodowania.
internal sealed partial class MainForm
{
    private readonly Label _lblCodCurrent = Ui.Value("—", Theme.H1);
    private readonly Label _lblCodReference = Ui.Value("—", Theme.H1);
    private readonly Label _lblCodState = Ui.Paragraph("", 620);
    private readonly Label _lblCodEcu = Ui.Paragraph("", 620);
    private readonly NumericUpDown _numCoding = Ui.Number(0, 32767, 0, 90);
    private readonly NumericUpDown _numWorkshop = Ui.Number(0, 99999, 0, 90);

    private TabPage BuildCodingTab()
    {
        var page = Ui.Page("Kodowanie");

        var btnRead = Ui.Button("Odczytaj (identyfikacja)", async (_, _) => await RunToolAsync("Identyfikacja", ["ReadIdent"]), ButtonKind.Primary);
        var btnRestore = Ui.Button("Przywróć referencyjne…", async (_, _) => await RestoreReferenceAsync(), ButtonKind.Danger);
        var btnSetRef = Ui.Button("Ustaw obecne jako referencyjne…", (_, _) => SetCurrentAsReference());
        var btnWrite = Ui.Button("Zapisz kodowanie…", async (_, _) => await WriteManualCodingAsync(), ButtonKind.Danger);
        _readActions.AddRange([btnRead, btnSetRef]);
        _writeActions.AddRange([btnRestore, btnWrite]);

        _lblCodCurrent.Margin = new Padding(0, 0, 40, 10);
        var stateCard = new CardPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        stateCard.Controls.Add(Ui.Column(
            Ui.Heading("Kodowanie sterownika"),
            _lblCodEcu,
            Ui.Row(
                Ui.Column(Ui.Caption("Obecne (kodowanie / kod warsztatu)"), _lblCodCurrent),
                Ui.Column(Ui.Caption("Referencyjne (znane jako dobre)"), _lblCodReference)),
            _lblCodState,
            Ui.Row(btnRead, btnRestore, btnSetRef)));

        var manualCard = new CardPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        manualCard.Controls.Add(Ui.Column(
            Ui.Heading("Ręczny zapis kodowania (tryb serwisowy)"),
            Ui.Row(Ui.Field("Kodowanie (0–32767)", _numCoding), Ui.Field("Kod warsztatu (0–99999)", _numWorkshop)),
            Ui.Row(btnWrite),
            Ui.Paragraph(
                "Po zapisie program łączy się ponownie i porównuje odczytane kodowanie z zapisanym – " +
                "potwierdzenie samego sterownika nie wystarcza.", 620)));

        var whyCard = new CardPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.WarnSoft, BorderColor = Color.FromArgb(240, 214, 160) };
        whyCard.Controls.Add(Ui.Column(
            Ui.Heading("Dlaczego to jest pilnowane"),
            new Label
            {
                AutoSize = true,
                MaximumSize = new Size(760, 0),
                Text =
                    "27.09.2026 każdy zapis adaptacji w 4A0907473A przesuwał bajty kodowania " +
                    "(17453/28419 → 17463/66307 → 17537/66307). Kodowanie przywrócono poleceniem " +
                    "SetSoftwareCoding 17453 28419. Dlatego ten program porównuje kodowanie z referencyjnym " +
                    "przy każdym połączeniu i ostrzega o różnicy – również na zakładce Sterownik.",
            }));

        page.Controls.Add(Ui.Stack(stateCard, manualCard, whyCard));

        RefreshCodingTab();
        return page;
    }

    private void RefreshCodingTab()
    {
        var reference = ReferenceFor(_ident);
        _lblCodEcu.Text = _ident is null
            ? "Sterownik jeszcze nie odczytany – kliknij „Odczytaj (identyfikacja)”."
            : $"{_ident.PartNumber}  {_ident.Description}  {_ident.Version}";
        _lblCodCurrent.Text = _coding is null ? "—" : $"{_coding.Coding:D5} / {_coding.Workshop:D5}";
        _lblCodReference.Text = reference is null ? "—" : $"{reference.Coding:D5} / {reference.Workshop:D5}";

        if (_coding is null || reference is null)
        {
            _lblCodState.Text = "";
        }
        else if (CodingDiffersFromReference)
        {
            _lblCodState.Text = "⚠ Kodowanie różni się od referencyjnego. Jeśli nie zmieniałeś go celowo, przywróć referencyjne.";
            _lblCodState.ForeColor = Theme.Danger;
        }
        else
        {
            _lblCodState.Text = "✔ Kodowanie zgodne z referencyjnym.";
            _lblCodState.ForeColor = Theme.Ok;
        }

        if (_coding is not null)
        {
            _numCoding.Value = Math.Clamp(_coding.Coding, 0, 32767);
            _numWorkshop.Value = Math.Clamp(_coding.Workshop, 0, 99999);
        }
    }

    private void SetCurrentAsReference()
    {
        if (_ident is null || _coding is null)
        {
            Warn("Brak odczytu", "Najpierw odczytaj sterownik (identyfikacja).");
            return;
        }
        var old = ReferenceFor(_ident);
        var ok = Confirm(
            "Ustawić obecne kodowanie jako referencyjne?",
            $"{_ident.PartNumber}: {_coding.Coding:D5} / {_coding.Workshop:D5}" +
            (old is null ? "" : $"\nDotychczasowe referencyjne: {old.Coding:D5} / {old.Workshop:D5}") +
            "\n\nZmienia to tylko ustawienia tego programu – nic nie jest wysyłane do sterownika. " +
            "Rób to wyłącznie, gdy masz pewność, że obecne kodowanie jest poprawne.",
            "Ustaw");
        if (!ok)
        {
            return;
        }
        _settings.ReferenceCoding[_ident.PartNumber] = new CodingRef(_coding.Coding, _coding.Workshop);
        _settings.Save();
        RefreshControllerCard();
        RefreshCodingTab();
    }

    private async Task RestoreReferenceAsync()
    {
        if (ReferenceFor(_ident) is not { } reference)
        {
            Warn("Brak kodowania referencyjnego", "Najpierw odczytaj sterownik (identyfikacja).");
            return;
        }
        await WriteCodingAsync(reference.Coding, reference.Workshop, "Przywrócenie kodowania referencyjnego");
    }

    private async Task WriteManualCodingAsync() =>
        await WriteCodingAsync((int)_numCoding.Value, (int)_numWorkshop.Value, "Zapis kodowania");

    private async Task WriteCodingAsync(int coding, int workshop, string title)
    {
        var current = _coding is null ? "nieznane (sterownik nie odczytany)" : $"{_coding.Coding:D5} / {_coding.Workshop:D5}";
        var ok = Confirm(
            $"{title}?",
            $"Sterownik: {IdentText()}\n" +
            $"Obecne: {current}\n" +
            $"Nowe:   {coding:D5} / {workshop:D5}\n\n" +
            "Kodowanie określa wariant wyposażenia – złe kodowanie może zmienić pracę sterownika. " +
            "Po zapisie program odczyta je ponownie na nowym połączeniu.",
            "Zapisz kodowanie");
        if (!ok)
        {
            return;
        }

        var result = await RunToolAsync(title,
            ["SetSoftwareCoding", coding.ToString(CultureInfo.InvariantCulture), workshop.ToString(CultureInfo.InvariantCulture)],
            isWrite: true);
        if (result is null || result.Stopped)
        {
            return;
        }
        var acknowledged = result.Lines.Any(l => l.Contains("Software coding set", StringComparison.OrdinalIgnoreCase));

        var verify = await RunToolAsync("Kontrola kodowania", ["ReadIdent"]);
        var read = verify is null ? null : KwOutput.ParseCoding(verify.Lines);
        var matches = read is not null && read.Coding == coding && read.Workshop == workshop;

        Inform(
            matches ? "Kodowanie zapisane i potwierdzone" : "Kodowanie nie zgadza się",
            $"Oczekiwane: {coding:D5} / {workshop:D5}\n" +
            $"Odczytane:  {(read is null ? "brak odczytu" : $"{read.Coding:D5} / {read.Workshop:D5}")}\n\n" +
            (acknowledged ? "Sterownik potwierdził zapis." : "Sterownik NIE potwierdził zapisu."),
            matches);
    }
}
