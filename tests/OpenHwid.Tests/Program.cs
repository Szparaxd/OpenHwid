using OpenHwid.Core;

var tests = new (string Name, Action Run)[]
{
    ("MAC accepts common separators", () => Equal("02AABBCCDDEE", MacAddress.Normalize("02:aa:bb:cc:dd:ee"))),
    ("MAC rejects multicast, global, zero and malformed values", () =>
    {
        foreach (var value in new[] { "01AABBCCDDEE", "00AABBCCDDEE", "000000000000", "FFFFFFFFFFFF", "02AA", "02AABBCCDDGG", "02AA BBCCDDEE", "02:AA-BB:CC:DD:EE" })
            Throws<ArgumentException>(() => MacAddress.Normalize(value));
    }),
    ("Generated MACs are local unicast and vary", () =>
    {
        var values = Enumerable.Range(0, 200).Select(_ => MacAddress.Generate()).ToArray();
        foreach (var value in values) { Equal(12, value.Length); Equal(2, Convert.ToByte(value[..2], 16) & 3); }
        Equal(200, values.Distinct().Count());
    }),
    ("Repeated changes preserve the first original override", () => WithFixture((adapter, store, service) =>
    {
        adapter.Value = "0A1122334455";
        service.Change(adapter.Id, "02AABBCCDDEE");
        service.Change(adapter.Id, "061122334455");
        Equal("0A1122334455", store.Read(adapter.Id)!.OriginalOverride);
        service.Restore(adapter.Id);
        Equal("0A1122334455", adapter.Value);
    })),
    ("Restore removes an originally absent override", () => WithFixture((adapter, store, service) =>
    {
        service.Change(adapter.Id, "02AABBCCDDEE"); service.Restore(adapter.Id); Equal<string?>(null, adapter.Value);
    })),
    ("Restore preserves an originally empty string override", () => WithFixture((adapter, store, service) =>
    {
        adapter.Value = "";
        service.Change(adapter.Id, "02AABBCCDDEE"); service.Restore(adapter.Id); Equal("", adapter.Value);
    })),
    ("Restore preserves a preexisting driver-specific string", () => WithFixture((adapter, store, service) =>
    {
        adapter.Value = "driver-default";
        service.Change(adapter.Id, "02AABBCCDDEE"); service.Restore(adapter.Id); Equal("driver-default", adapter.Value);
    })),
    ("Wrong adapter in backup blocks writes", () => WithFixture((adapter, store, service) =>
    {
        File.WriteAllText(Path.Combine(adapter.Directory, adapter.Id.ToString("N") + ".json"),
            System.Text.Json.JsonSerializer.Serialize(new AdapterBackup(1, "test-machine", Guid.NewGuid(), null, "001122334455", DateTimeOffset.UtcNow)));
        Throws<InvalidOperationException>(() => service.Change(adapter.Id, "02AABBCCDDEE"));
        Throws<InvalidOperationException>(() => service.Restore(adapter.Id)); Equal(0, adapter.Writes);
    })),
    ("Unknown backup schema blocks writes", () => WithFixture((adapter, store, service) =>
    {
        store.SaveFirst(new(2, "test-machine", adapter.Id, null, "001122334455", DateTimeOffset.UtcNow));
        Throws<InvalidOperationException>(() => service.Change(adapter.Id, "02AABBCCDDEE"));
        Throws<InvalidOperationException>(() => service.Restore(adapter.Id)); Equal(0, adapter.Writes);
    })),
    ("Invalid MAC cannot create backup or change adapter", () => WithFixture((adapter, store, service) =>
    {
        Throws<ArgumentException>(() => service.Change(adapter.Id, "01AABBCCDDEE"));
        Equal(0, adapter.Writes); Equal<AdapterBackup?>(null, store.Read(adapter.Id));
    })),
    ("Foreign machine backup blocks change and restore", () => WithFixture((adapter, store, service) =>
    {
        store.SaveFirst(new(1, "other-machine", adapter.Id, null, "001122334455", DateTimeOffset.UtcNow));
        Throws<InvalidOperationException>(() => service.Change(adapter.Id, "02AABBCCDDEE"));
        Throws<InvalidOperationException>(() => service.Restore(adapter.Id)); Equal(0, adapter.Writes);
    })),
    ("Restore without backup leaves adapter untouched", () => WithFixture((adapter, store, service) =>
    {
        Throws<InvalidOperationException>(() => service.Restore(adapter.Id)); Equal(0, adapter.Writes);
    })),
    ("Restart failure leaves a recoverable original backup", () => WithFixture((adapter, store, service) =>
    {
        adapter.FailRestart = true;
        Throws<IOException>(() => service.Change(adapter.Id, "02AABBCCDDEE"));
        Equal<string?>(null, store.Read(adapter.Id)!.OriginalOverride);
        adapter.FailRestart = false; service.Restore(adapter.Id); Equal<string?>(null, adapter.Value);
    })),
    ("Driver ignoring requested MAC is reported", () => WithFixture((adapter, store, service) =>
    {
        adapter.IgnoreOverride = true;
        Throws<InvalidOperationException>(() => service.Change(adapter.Id, "02AABBCCDDEE"));
        Equal(true, store.Read(adapter.Id) is not null);
    })),
    ("Write failure preserves original backup", () => WithFixture((adapter, store, service) =>
    {
        adapter.FailWrite = true;
        Throws<UnauthorizedAccessException>(() => service.Change(adapter.Id, "02AABBCCDDEE"));
        Equal<string?>(null, store.Read(adapter.Id)!.OriginalOverride); Equal<string?>(null, adapter.Value);
    })),
    ("Restore verification failure retains backup", () => WithFixture((adapter, store, service) =>
    {
        adapter.Value = "0A1122334455"; service.Change(adapter.Id, "02AABBCCDDEE");
        adapter.IgnoreOverride = true;
        Throws<InvalidOperationException>(() => service.Restore(adapter.Id));
        Equal("0A1122334455", store.Read(adapter.Id)!.OriginalOverride);
    })),
    ("Unreadable backup blocks writes", () => WithFixture((adapter, store, service) =>
    {
        File.WriteAllText(Path.Combine(adapter.Directory, adapter.Id.ToString("N") + ".json"), "broken");
        Throws<System.Text.Json.JsonException>(() => service.Change(adapter.Id, "02AABBCCDDEE")); Equal(0, adapter.Writes);
    })),
    ("Backup persistence failure blocks writes", () => WithFixture((adapter, store, service) =>
    {
        var file = Path.Combine(adapter.Directory, "file"); File.WriteAllText(file, "occupied");
        var bad = new MacService(adapter, new BackupStore(file), "test-machine");
        Throws<IOException>(() => bad.Change(adapter.Id, "02AABBCCDDEE")); Equal(0, adapter.Writes);
    }))
};
var failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected ?? (object)"null"}, got {actual ?? (object)"null"}");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}");
}
static void WithFixture(Action<FakeAdapter, BackupStore, MacService> action)
{
    var dir = Path.Combine(Path.GetTempPath(), "OpenHwidTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
    try { var adapter = new FakeAdapter(dir); var store = new BackupStore(dir); action(adapter, store, new MacService(adapter, store, "test-machine")); }
    finally { Directory.Delete(dir, true); }
}
sealed class FakeAdapter(string directory) : IAdapterAccess
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Directory { get; } = directory;
    public string? Value { get; set; }
    public int Writes { get; private set; }
    public bool FailRestart { get; set; }
    public bool IgnoreOverride { get; set; }
    public bool FailWrite { get; set; }
    public AdapterState Read(Guid id) => new(id, Value, IgnoreOverride || Value?.Length != 12 ? "001122334455" : Value);
    public void WriteOverride(Guid id, string? value) { if (FailWrite) throw new UnauthorizedAccessException("Simulated denied write"); Writes++; Value = value; }
    public void Restart(Guid id) { if (FailRestart) throw new IOException("Simulated restart failure"); }
}
