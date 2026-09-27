namespace Audi100Diag;

// Zakładka "Sterownik": identyfikacja i pamięć błędów.
internal sealed partial class MainForm
{
    private readonly Label _lblPart = Ui.Value("—", Theme.H1);
    private readonly Label _lblDesc = Ui.Value();
    private readonly Label _lblVersion = Ui.Value();
    private readonly Label _lblCardCoding = Ui.Value();
    private readonly Label _lblCardCodingState = Ui.Paragraph("", 330);
    private readonly Label _lblFaultSummary = Ui.Heading("Pamięć błędów");
    private readonly Label _lblFaultNote = Ui.Paragraph("", 640);
    private readonly ListView _lvFaults = new()
    {
        View = View.Details,
        FullRowSelect = true,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        Font = Theme.Ui,
    };

    private TabPage BuildControllerTab()
    {
        var page = Ui.Page("Sterownik");

        var btnIdent = Ui.Button("Identyfikacja", async (_, _) => await RunToolAsync("Identyfikacja", ["ReadIdent"]), ButtonKind.Primary);
        var btnFaults = Ui.Button("Odczytaj błędy", async (_, _) => await ReadFaultsAsync());
        var btnClear = Ui.Button("Kasuj błędy…", async (_, _) => await ClearFaultsAsync(), ButtonKind.Danger);
        _readActions.AddRange([btnIdent, btnFaults]);
        _writeActions.Add(btnClear);

        var info = Ui.Column(
            Ui.Heading("Sterownik"),
            Ui.Row(btnIdent, btnFaults, btnClear),
            Ui.Caption("Numer części"), _lblPart,
            Ui.Caption("Opis"), _lblDesc,
            Ui.Caption("Wersja"), _lblVersion,
            Ui.Caption("Kodowanie / kod warsztatu"), _lblCardCoding,
            _lblCardCodingState,
            Ui.Paragraph("Dane odświeżają się przy każdym poleceniu – sterownik przedstawia się przy każdym połączeniu.", 330));
        info.Dock = DockStyle.Top;
        info.MaximumSize = new Size(340, 0);
        var infoCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0), AutoScroll = true };
        infoCard.Controls.Add(info);

        _lvFaults.Columns.Add("Kod", 80);
        _lvFaults.Columns.Add("Opis", 440);
        _lvFaults.Columns.Add("Status", 90);
        var faultHeader = Ui.Column(_lblFaultSummary, _lblFaultNote);
        faultHeader.Dock = DockStyle.Top;
        var faultCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        faultCard.Controls.Add(_lvFaults);
        faultCard.Controls.Add(faultHeader);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(infoCard, 0, 0);
        layout.Controls.Add(faultCard, 1, 0);
        page.Controls.Add(layout);

        ClearFaultList("Nie odczytano – kliknij „Odczytaj błędy”.");
        return page;
    }

    private string IdentText()
    {
        if (_ident is not null)
        {
            return $"{_ident.PartNumber}  {_ident.Description}";
        }
        return TryGetAddress(out var a) ? $"adres {ControllerNames.Describe(a)}" : "nieznany";
    }

    private void RefreshControllerCard()
    {
        _lblPart.Text = _ident?.PartNumber ?? "—";
        _lblDesc.Text = _ident?.Description is { Length: > 0 } d ? d : "—";
        _lblVersion.Text = _ident?.Version is { Length: > 0 } v ? v : "—";
        _lblCardCoding.Text = _coding is null ? "—" : $"{_coding.Coding:D5} / {_coding.Workshop:D5}";

        var reference = ReferenceFor(_ident);
        if (_coding is null || reference is null)
        {
            _lblCardCodingState.Text = "";
        }
        else if (CodingDiffersFromReference)
        {
            _lblCardCodingState.Text = $"⚠ Różni się od referencyjnego {reference.Coding:D5} / {reference.Workshop:D5} – zobacz zakładkę Kodowanie.";
            _lblCardCodingState.ForeColor = Theme.Danger;
        }
        else
        {
            _lblCardCodingState.Text = "✔ Zgodne z kodowaniem referencyjnym";
            _lblCardCodingState.ForeColor = Theme.Ok;
        }
    }

    private async Task ReadFaultsAsync()
    {
        var result = await RunToolAsync("Odczyt błędów", ["ReadFaultCodes"]);
        if (result is null || result.Stopped)
        {
            return;
        }
        var faults = KwOutput.ParseFaults(result.Lines);
        if (!faults.Received)
        {
            ClearFaultList(result.Problem ?? "Sterownik nie zwrócił listy błędów.");
            return;
        }
        ShowFaults(faults.Codes);
    }

    private async Task ClearFaultsAsync()
    {
        var ok = Confirm(
            "Skasować pamięć błędów?",
            $"Sterownik: {IdentText()}\n\n" +
            "Kasowanie usuwa zapisane kody. Odczytaj je i zanotuj wcześniej – po skasowaniu nie da się ich odzyskać. " +
            "Usterki, które nadal występują, wrócą po ponownym wykryciu.",
            "Skasuj błędy");
        if (!ok)
        {
            return;
        }

        var result = await RunToolAsync("Kasowanie błędów", ["ClearFaultCodes"], isWrite: true);
        if (result is null || result.Stopped)
        {
            return;
        }
        var faults = KwOutput.ParseFaults(result.Lines);
        if (faults.Cleared)
        {
            ShowFaults([]);
        }
        else if (faults.Received)
        {
            ShowFaults(faults.Codes);
            Warn("Nie wszystkie błędy zostały skasowane",
                "Sterownik nadal zgłasza kody z listy – zwykle to usterki aktywne w tej chwili.");
        }
        else
        {
            Warn("Kasowanie nie powiodło się", result.Problem ?? "Sterownik nie potwierdził skasowania błędów.");
        }
    }

    private void ShowFaults(IReadOnlyList<FaultCode> codes)
    {
        _lvFaults.BeginUpdate();
        _lvFaults.Items.Clear();
        foreach (var code in codes)
        {
            var item = new ListViewItem(code.Code.ToString("D5"));
            item.SubItems.Add(ControllerNames.FaultTexts.TryGetValue(code.Code, out var text) ? text : "(brak w słowniku)");
            item.SubItems.Add(code.Status);
            _lvFaults.Items.Add(item);
        }
        _lvFaults.EndUpdate();

        _lblFaultSummary.Text = codes.Count == 0 ? "Brak zapisanych błędów ✔" : $"Zapisane błędy: {codes.Count}";
        _lblFaultSummary.ForeColor = codes.Count == 0 ? Theme.Ok : Theme.Danger;
        _lblFaultNote.Text =
            $"Odczyt {DateTime.Now:HH:mm:ss}, sterownik {IdentText()}. " +
            "Opisy pochodzą z ogólnego słownika VAG i są orientacyjne; status to surowe bajty ze sterownika.";
    }

    private void ClearFaultList(string note)
    {
        _lvFaults.Items.Clear();
        _lblFaultSummary.Text = "Pamięć błędów";
        _lblFaultSummary.ForeColor = Theme.Text;
        _lblFaultNote.Text = note;
    }
}
