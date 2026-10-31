using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenHwid.Core;

namespace OpenHwid.Windows;

internal sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(26, 40, 57);
    private static readonly Color Accent = Color.FromArgb(0, 108, 112);
    private readonly WindowsAdapters adapters = new();
    private readonly string backupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenHwid", "backups");
    private readonly DataGridView grid = new();
    private readonly ComboBox adapterBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox macBox = new() { Text = MacAddress.Generate(), Width = 220, CharacterCasing = CharacterCasing.Upper, MaxLength = 17 };
    private readonly Label adapterInfo = new() { AutoSize = true, MaximumSize = new Size(900, 0) };
    private readonly TextBox backupInfo = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label status = new() { Text = "Uruchamianie…", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Button refresh = MakeButton("Odśwież");
    private readonly Button export = MakeButton("Eksportuj JSON");
    private readonly Button generate = MakeButton("Losuj MAC");
    private readonly Button change = MakeButton("Zmień MAC", true);
    private readonly Button restore = MakeButton("Przywróć z kopii");
    private bool busy;
    private string? report;

    public MainForm(string? smokeOutput)
    {
        Text = "OpenHwid · identyfikatory i prywatność";
        Size = new Size(1080, 800); MinimumSize = new Size(850, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); ForeColor = Ink; BackColor = Color.FromArgb(245, 247, 250);
        if (smokeOutput is not null) { Opacity = 0; ShowInTaskbar = false; }

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(22, 14, 22, 8) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        Controls.Add(root);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label { Text = "OpenHwid", AutoSize = true, Font = new Font("Segoe UI", 25, FontStyle.Bold), ForeColor = Accent }, 0, 0);
        header.Controls.Add(new Label { Text = "Twój sprzęt. Przejrzyste ustawienia. Bez limitu użyć.", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, 0, 1);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        tools.Controls.Add(export); tools.Controls.Add(refresh); header.Controls.Add(tools, 1, 0);
        header.Controls.Add(new Label { Text = WindowsAdapters.IsAdministrator ? "●  Administrator" : "●  Podgląd · bez uprawnień administratora", AutoSize = true, ForeColor = Accent, Padding = new Padding(0, 7, 0, 0) }, 1, 1);
        root.Controls.Add(header, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(18, 8) };
        var hardwareTab = new TabPage("Identyfikatory sprzętu") { Padding = new Padding(12), BackColor = Color.White };
        var macTab = new TabPage("Adres MAC i przywracanie") { Padding = new Padding(16), BackColor = Color.White };
        tabs.TabPages.Add(hardwareTab); tabs.TabPages.Add(macTab); root.Controls.Add(tabs, 0, 1); root.Controls.Add(status, 0, 2);
        grid.Dock = DockStyle.Fill; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None; grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True; grid.DefaultCellStyle.Padding = new Padding(5);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(244, 248, 249);
        grid.Columns.Add("Category", "Kategoria"); grid.Columns.Add("Property", "Właściwość"); grid.Columns.Add("Value", "Wartość");
        grid.Columns[0].FillWeight = 22; grid.Columns[1].FillWeight = 28; grid.Columns[2].FillWeight = 50;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Ink;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersHeight = 34;
        var hardwareLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        hardwareLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); hardwareLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        hardwareLayout.Controls.Add(new Label { Text = "Odczyt lokalny · BIOS, SMBIOS, CPU, dyski, woluminy i sieć\nRaport zawiera identyfikatory urządzenia. Przejrzyj go przed udostępnieniem.", Dock = DockStyle.Fill }, 0, 0);
        hardwareLayout.Controls.Add(grid, 0, 1); hardwareTab.Controls.Add(hardwareLayout);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9 };
        foreach (var height in new[] { 34, 44, 76, 30, 50, 54, 66, 28 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "Karta sieciowa", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        layout.Controls.Add(adapterBox, 0, 1); layout.Controls.Add(adapterInfo, 0, 2);
        layout.Controls.Add(new Label { Text = "Nowy adres MAC (lokalnie administrowany, unicast)", AutoSize = true }, 0, 3);
        var input = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        macBox.Margin = new Padding(0, 5, 12, 0); input.Controls.Add(macBox); input.Controls.Add(generate); layout.Controls.Add(input, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.Add(change); actions.Controls.Add(restore);
        if (!WindowsAdapters.IsAdministrator)
        {
            var elevate = MakeButton("Uruchom jako administrator");
            elevate.Click += (_, _) => Elevate(); actions.Controls.Add(elevate);
        }
        layout.Controls.Add(actions, 0, 5);
        layout.Controls.Add(new Label { Text = "Zmiana i przywracanie restartują wybraną kartę i przerywają jej połączenie.\nPierwsza kopia ustawień jest zachowywana przy kolejnych zmianach.\nTa wersja nie zmienia UUID SMBIOS ani numerów seryjnych dysków i firmware.", Dock = DockStyle.Fill }, 0, 6);
        layout.Controls.Add(new Label { Text = "Kopia ustawień", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 7);
        layout.Controls.Add(backupInfo, 0, 8); macTab.Controls.Add(layout);

        refresh.Click += async (_, _) => await RefreshAsync(); export.Click += (_, _) => ExportReport();
        generate.Click += (_, _) => macBox.Text = MacAddress.Generate();
        adapterBox.SelectedIndexChanged += (_, _) => UpdateActions();
        change.Click += async (_, _) => await MutateAsync(false); restore.Click += async (_, _) => await MutateAsync(true);
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        Shown += async (_, _) =>
        {
            await RefreshAsync(smokeOutput is null);
            if (smokeOutput is not null)
            {
                try
                {
                    if (report is null || grid.Rows.Count == 0) throw new InvalidOperationException("Inventory did not populate the UI.");
                    using var smokeReport = JsonDocument.Parse(report);
                    if (!smokeReport.RootElement.EnumerateObject().Any(p => p.Name != "Warnings" && p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() > 0))
                        throw new InvalidOperationException("All hardware queries failed; warnings alone do not count as inventory.");
                    using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                    bitmap.Save(Path.ChangeExtension(smokeOutput, ".png"));
                    tabs.SelectedIndex = 1; Application.DoEvents();
                    using var networkBitmap = new Bitmap(Width, Height); DrawToBitmap(networkBitmap, new Rectangle(0, 0, Width, Height));
                    networkBitmap.Save(Path.Combine(Path.GetDirectoryName(smokeOutput)!, Path.GetFileNameWithoutExtension(smokeOutput) + "-network.png"));
                    File.WriteAllText(smokeOutput, $"UI loaded; inventory rows: {grid.Rows.Count}; adapters: {adapterBox.Items.Count}; mutation performed: false", Encoding.UTF8);
                }
                catch (Exception ex) { File.WriteAllText(smokeOutput, ex.ToString()); Environment.ExitCode = 1; }
                Close();
            }
        };
        UpdateActions();
    }
    private static Button MakeButton(string text, bool primary = false) => new()
    {
        Text = text, AutoSize = true, Height = 36, MinimumSize = new Size(100, 36),
        Padding = new Padding(9, 2, 9, 2), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
        BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
        Margin = new Padding(0, 0, 10, 5)
    };
    private void UpdateActions()
    {
        var selected = adapterBox.SelectedItem as NetworkAdapter;
        refresh.Enabled = export.Enabled = generate.Enabled = adapterBox.Enabled = macBox.Enabled = !busy;
        export.Enabled &= report is not null;
        change.Enabled = !busy && WindowsAdapters.IsAdministrator && selected?.CanChange == true;
        restore.Enabled = false;
        if (selected is null) { adapterInfo.Text = "Brak dostępnych kart sieciowych."; backupInfo.Text = backupDirectory; return; }
        adapterInfo.Text = selected.Description + "\n" + selected.Reason;
        try
        {
            var backup = new BackupStore(backupDirectory).Read(selected.Id);
            restore.Enabled = !busy && WindowsAdapters.IsAdministrator && backup is not null;
            backupInfo.Text = backup is null ? "Brak kopii dla tej karty. Powstanie przed pierwszą zmianą.\r\n" + backupDirectory
                : $"Data: {backup.Created.ToLocalTime():yyyy-MM-dd HH:mm:ss}\r\nMAC przed pierwszą zmianą: {backup.OriginalMac}\r\nPoprzednie nadpisanie: {backup.OriginalOverride ?? "brak (adres sterownika)"}\r\n{backupDirectory}";
        }
        catch (Exception ex) { change.Enabled = false; backupInfo.Text = "Nie można odczytać kopii. Zmiana zablokowana.\r\n" + ex.Message; }
    }
    private async Task RefreshAsync(bool showError = true)
    {
        busy = true; UpdateActions(); status.Text = "Odczytywanie sprzętu…";
        try
        {
            var selectedId = (adapterBox.SelectedItem as NetworkAdapter)?.Id;
            var data = await Task.Run(() => (Json: Inventory.Pretty(Inventory.Read()), Adapters: adapters.List()));
            report = data.Json; grid.Rows.Clear();
            using var doc = JsonDocument.Parse(report);
            foreach (var category in doc.RootElement.EnumerateObject())
            {
                if (category.Value.ValueKind != JsonValueKind.Array) continue;
                var index = 0;
                foreach (var item in category.Value.EnumerateArray())
                {
                    index++;
                    if (item.ValueKind == JsonValueKind.Object)
                        foreach (var property in item.EnumerateObject()) grid.Rows.Add(category.Name + " #" + index, property.Name, property.Value.ToString());
                    else grid.Rows.Add(category.Name, index.ToString(), item.ToString());
                }
            }
            adapterBox.Items.Clear(); foreach (var adapter in data.Adapters) adapterBox.Items.Add(adapter);
            if (adapterBox.Items.Count > 0)
            {
                var index = data.Adapters.ToList().FindIndex(a => a.Id == selectedId); adapterBox.SelectedIndex = Math.Max(0, index);
            }
            var warnings = doc.RootElement.GetProperty("Warnings").GetArrayLength();
            status.Text = warnings == 0 ? "Gotowe · odczyt lokalny, bez wysyłania danych" : $"Raport częściowy · ostrzeżenia: {warnings}. Szczegóły w tabeli i JSON.";
        }
        catch (Exception ex) { status.Text = "Odczyt nie powiódł się: " + ex.Message; if (showError) ShowError(ex); }
        finally { busy = false; UpdateActions(); }
    }
    private async Task MutateAsync(bool isRestore)
    {
        if (adapterBox.SelectedItem is not NetworkAdapter selected) return;
        string requested = "";
        try { if (!isRestore) requested = MacAddress.Normalize(macBox.Text); }
        catch (Exception ex) { ShowError(ex); return; }
        var action = isRestore ? "przywrócenie ustawień z pierwszej kopii" : "zmianę MAC na " + requested;
        if (MessageBox.Show(this, $"Potwierdź {action}.\nKarta: {selected.Name}\n\nKarta zostanie zrestartowana. Jej połączenie sieciowe zostanie przerwane.\nKontynuować?", "Zmiana ustawień sieci", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        busy = true; UpdateActions(); status.Text = "Zapisywanie ustawień i restart wybranej karty…";
        string? result = null; Exception? failure = null;
        try
        {
            result = await Task.Run(() =>
            {
                var service = new MacService(adapters, new BackupStore(backupDirectory), WindowsAdapters.MachineToken());
                return isRestore ? service.Restore(selected.Id) : service.Change(selected.Id, requested);
            });
        }
        catch (Exception ex) { failure = ex; }
        finally { busy = false; }
        await RefreshAsync();
        if (failure is not null) { status.Text = "Operacja nie została potwierdzona. Sprawdź stan karty i kopię ustawień."; ShowError(failure); }
        else status.Text = $"{(isRestore ? "Przywrócono" : "Zmieniono")} i sprawdzono MAC: {result}";
    }
    private void ExportReport()
    {
        if (report is null) return;
        using var dialog = new SaveFileDialog { Filter = "Raport JSON (*.json)|*.json", FileName = $"openhwid-{DateTime.Now:yyyyMMdd-HHmmss}.json", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, report, new UTF8Encoding(false)); status.Text = "Zapisano raport: " + dialog.FileName; }
        catch (Exception ex) { ShowError(ex); }
    }
    private void Elevate()
    {
        try
        {
            // The new process waits briefly for this window to close and release the single-instance mutex.
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory });
            Close();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { status.Text = "Anulowano uruchomienie jako administrator."; }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "OpenHwid", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
