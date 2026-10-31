using System.Text;
using System.Text.Json;
using OpenHwid.Core;

namespace OpenHwid.Windows;

internal sealed partial class MainForm
{
    private readonly DataGridView comparisonGrid = NewGrid();
    private readonly DataGridView profileGrid = NewGrid();
    private readonly Label comparisonSummary = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Wybierz raport bazowy lub zapamiętaj aktualny odczyt." };
    private readonly Label baselineLabel = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Brak raportu bazowego." };
    private readonly Label profileSummary = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly TextBox profileName = new() { Width = 220, MaxLength = 64 };
    private readonly TextBox profileMac = new() { Width = 180, MaxLength = 17, CharacterCasing = CharacterCasing.Upper, Text = MacAddress.Generate() };
    private readonly Button useCurrent = MakeButton("Zapamiętaj bieżący");
    private readonly Button loadBaseline = MakeButton("Wczytaj bazowy JSON");
    private readonly Button exportDiff = MakeButton("Eksportuj różnice");
    private readonly Button saveProfile = MakeButton("Zapisz profil", true);
    private readonly Button loadProfile = MakeButton("Wczytaj do pola MAC");
    private readonly Button deleteProfile = MakeButton("Usuń profil");
    private string? baselineReport;
    private IReadOnlyList<ReportDifference> differences = [];

    private static DataGridView NewGrid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
        EnableHeadersVisualStyles = false, ColumnHeadersHeight = 34,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Ink, ForeColor = Color.White },
        DefaultCellStyle = new DataGridViewCellStyle { Padding = new Padding(4) },
        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(244, 248, 249) }
    };
    private TabPage BuildComparisonTab()
    {
        var tab = new TabPage("Porównanie raportów") { Padding = new Padding(12), BackColor = Color.White };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        foreach (var height in new[] { 50, 46, 28, 48 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Porównaj bazowy raport z bieżącym odczytem. Przycisk Odśwież aktualizuje prawą stronę.\nCzas wykonania i kolejność urządzeń nie są traktowane jako zmiana sprzętu." }, 0, 0);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        tools.Controls.Add(useCurrent); tools.Controls.Add(loadBaseline); tools.Controls.Add(exportDiff); layout.Controls.Add(tools, 0, 1);
        layout.Controls.Add(baselineLabel, 0, 2); layout.Controls.Add(comparisonSummary, 0, 3);
        foreach (var (key, title) in new[] { ("Kind", "Zmiana"), ("Category", "Kategoria"), ("Device", "Urządzenie"), ("Property", "Właściwość"), ("Before", "Bazowy raport"), ("After", "Bieżący odczyt") }) comparisonGrid.Columns.Add(key, title);
        comparisonGrid.Columns[0].FillWeight = 65; comparisonGrid.Columns[1].FillWeight = 65;
        layout.Controls.Add(comparisonGrid, 0, 4); tab.Controls.Add(layout);
        useCurrent.Click += (_, _) => { if (report is not null) { baselineReport = report; baselineLabel.Text = "Baza: bieżący odczyt zapamiętany w tej sesji"; UpdateComparison(); } };
        loadBaseline.Click += (_, _) => UiAction(ImportBaseline);
        exportDiff.Click += (_, _) => UiAction(ExportDifferences);
        return tab;
    }
    private TabPage BuildProfilesTab(Action showMacTab)
    {
        var tab = new TabPage("Profile MAC") { Padding = new Padding(12), BackColor = Color.White };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        foreach (var height in new[] { 66, 28, 46, 48, 34 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Zapisz nazwany adres i wróć do niego później. Profile są lokalne i mogą być używane na różnych kartach.\nWczytanie uzupełnia pole MAC w zakładce sieci; ustawienia zmienia dopiero przycisk Zmień MAC.\nProfile nie zastępują kopii poprzednich ustawień karty." }, 0, 0);
        layout.Controls.Add(new Label { Text = "Nazwa profilu                                     Adres MAC", Dock = DockStyle.Fill }, 0, 1);
        var inputs = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        profileName.Margin = profileMac.Margin = new Padding(0, 4, 12, 0); inputs.Controls.Add(profileName); inputs.Controls.Add(profileMac); inputs.Controls.Add(saveProfile);
        layout.Controls.Add(inputs, 0, 2);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        tools.Controls.Add(loadProfile); tools.Controls.Add(deleteProfile);
        var copyMac = MakeButton("Kopiuj z pola MAC"); tools.Controls.Add(copyMac); copyMac.Click += (_, _) => profileMac.Text = macBox.Text;
        layout.Controls.Add(tools, 0, 3); layout.Controls.Add(profileSummary, 0, 4);
        profileGrid.Columns.Add("Name", "Nazwa"); profileGrid.Columns.Add("Mac", "MAC"); profileGrid.Columns.Add("Created", "Zapisano");
        layout.Controls.Add(profileGrid, 0, 5); tab.Controls.Add(layout);
        saveProfile.Click += (_, _) => UiAction(() => SaveProfile());
        loadProfile.Click += (_, _) =>
        {
            if (SelectedProfile() is not { } profile) return;
            macBox.Text = profile.Mac; showMacTab(); status.Text = $"Wczytano profil {profile.Name}. Wybierz kartę i kliknij Zmień MAC, aby zastosować.";
        };
        deleteProfile.Click += (_, _) => UiAction(() =>
        {
            if (SelectedProfile() is not { } profile) return;
            if (MessageBox.Show(this, $"Usunąć profil „{profile.Name}”? Ustawienia karty pozostaną bez zmian.", "Usuń profil", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            profileStore.Delete(profile.Id); RefreshProfiles();
        });
        profileGrid.SelectionChanged += (_, _) => UpdateFeatureActions();
        try { RefreshProfiles(); } catch (Exception ex) { profileSummary.Text = "Nie odczytano profili: " + ex.Message; }
        return tab;
    }
    private MacProfile? SelectedProfile() => profileGrid.CurrentRow?.Tag as MacProfile;
    private MacProfile SaveProfile()
    {
        var saved = profileStore.Save(profileName.Text, profileMac.Text); RefreshProfiles(saved.Id); profileName.Clear();
        status.Text = "Zapisano profil „" + saved.Name + "”. Ustawienia sieci nie zostały zmienione.";
        return saved;
    }
    private void RefreshProfiles(Guid? select = null)
    {
        var items = profileStore.List(); profileGrid.Rows.Clear();
        foreach (var profile in items)
        {
            var index = profileGrid.Rows.Add(profile.Name, profile.Mac, profile.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            profileGrid.Rows[index].Tag = profile;
            if (profile.Id == select) profileGrid.CurrentCell = profileGrid.Rows[index].Cells[0];
        }
        profileSummary.Text = $"Zapisane profile: {items.Count} · %LOCALAPPDATA%\\OpenHwid\\profiles";
        UpdateFeatureActions();
    }
    private void UpdateFeatureActions()
    {
        useCurrent.Enabled = !busy && report is not null;
        loadBaseline.Enabled = !busy; exportDiff.Enabled = !busy && report is not null && baselineReport is not null;
        saveProfile.Enabled = profileName.Enabled = profileMac.Enabled = profileGrid.Enabled = !busy;
        loadProfile.Enabled = deleteProfile.Enabled = !busy && SelectedProfile() is not null;
    }
    private void UpdateComparison()
    {
        comparisonGrid.Rows.Clear(); differences = [];
        if (baselineReport is null || report is null) { UpdateFeatureActions(); return; }
        differences = ReportComparer.Compare(baselineReport, report);
        foreach (var diff in differences)
        {
            var kind = diff.Kind switch { "Added" => "Dodano", "Removed" => "Usunięto", "Unavailable" => "Brak odczytu", _ => "Zmieniono" };
            comparisonGrid.Rows.Add(kind, diff.Category, diff.Device, diff.Property, diff.Before ?? "—", diff.After ?? "—");
        }
        var unavailable = differences.Count(d => d.Kind == "Unavailable");
        comparisonSummary.Text = $"Różnice: {differences.Count - unavailable} · Kategorie bez pełnego odczytu: {unavailable}\nDla kategorii bez jednoznacznego klucza porównywany jest cały zestaw urządzeń.";
        UpdateFeatureActions();
    }
    private void ImportBaseline()
    {
        using var dialog = new OpenFileDialog { Filter = "Raport OpenHwid (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (new FileInfo(dialog.FileName).Length > 5 * 1024 * 1024) throw new InvalidDataException("Limit raportu wynosi 5 MiB.");
        var json = File.ReadAllText(dialog.FileName);
        ReportComparer.Compare(json, json); // Validate before replacing the previous baseline.
        baselineReport = json; baselineLabel.Text = "Baza: " + Path.GetFileName(dialog.FileName); UpdateComparison();
    }
    private void ExportDifferences()
    {
        if (baselineReport is null || report is null) return;
        using var dialog = new SaveFileDialog { Filter = "Różnice JSON (*.json)|*.json", FileName = $"openhwid-diff-{DateTime.Now:yyyyMMdd-HHmmss}.json", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var before = JsonDocument.Parse(baselineReport); using var after = JsonDocument.Parse(report);
        var data = new { SchemaVersion = 1, ReportType = "OpenHwidComparison", ComparedAtUtc = DateTimeOffset.UtcNow, Before = before.RootElement, After = after.RootElement, Differences = differences };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        status.Text = "Zapisano różnice i oba raporty: " + dialog.FileName;
    }
    private void UiAction(Action action) { try { action(); } catch (Exception ex) { ShowError(ex); } }
    private void SaveSmokeImage(string output, string suffix)
    {
        using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
        bitmap.Save(Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "-" + suffix + ".png"));
    }
}
