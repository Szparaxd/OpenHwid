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
    })),
    ("Report comparison ignores capture time and device order", () =>
    {
        var a = """{"SchemaVersion":1,"CapturedAtUtc":"2026-01-01","Network":[{"GUID":"a","MACAddress":"02AA"},{"GUID":"b","MACAddress":"02BB"}],"Warnings":[]}""";
        var b = """{"Warnings":[],"Network":[{"MACAddress":"02BB","GUID":"b"},{"MACAddress":"02AA","GUID":"a"}],"CapturedAtUtc":"2026-02-02","SchemaVersion":1}""";
        Equal(0, ReportComparer.Compare(a, b).Count);
    }),
    ("Report comparison locates changed MAC by GUID", () =>
    {
        var a = """{"SchemaVersion":1,"Network":[{"GUID":"a","MACAddress":"001122334455"},{"GUID":"b","MACAddress":"006677889900"}]}""";
        var b = """{"SchemaVersion":1,"Network":[{"GUID":"b","MACAddress":"006677889900"},{"GUID":"a","MACAddress":"02AABBCCDDEE"}]}""";
        var diff = ReportComparer.Compare(a, b).Single();
        Equal(new ReportDifference("Network", "a", "MACAddress", "001122334455", "02AABBCCDDEE", "Changed"), diff);
    }),
    ("Report comparison detects added and removed devices", () =>
    {
        var a = """{"SchemaVersion":1,"Volumes":[{"DeviceID":"C:","VolumeSerialNumber":"AAA"}]}""";
        var b = """{"SchemaVersion":1,"Volumes":[{"DeviceID":"D:","VolumeSerialNumber":"BBB"}]}""";
        var diff = ReportComparer.Compare(a, b);
        Equal(2, diff.Count); Equal("Removed", diff.Single(d => d.Device == "C:").Kind); Equal("Added", diff.Single(d => d.Device == "D:").Kind);
    }),
    ("Unidentified virtual adapter does not hide a physical adapter MAC change", () =>
    {
        var a = """{"SchemaVersion":1,"Network":[{"GUID":null,"Name":"Virtual"},{"GUID":"physical","MACAddress":"old"}]}""";
        var b = """{"SchemaVersion":1,"Network":[{"GUID":"physical","MACAddress":"new"},{"GUID":null,"Name":"Virtual"}]}""";
        var diff = ReportComparer.Compare(a, b).Single(); Equal("physical", diff.Device); Equal("MACAddress", diff.Property);
    }),
    ("Singleton hardware values compare by property", () =>
    {
        var diff = ReportComparer.Compare("""{"SchemaVersion":1,"Computer":[{"UUID":"old"}]}""", """{"SchemaVersion":1,"Computer":[{"UUID":"new"}]}""").Single();
        Equal("UUID", diff.Property); Equal("old", diff.Before); Equal("new", diff.After);
    }),
    ("Unkeyed duplicate devices compare as unordered sets", () =>
    {
        var a = """{"SchemaVersion":1,"Disks":[{"Model":"same","SerialNumber":"x"},{"SerialNumber":"y","Model":"same"}]}""";
        var b = """{"SchemaVersion":1,"Disks":[{"Model":"same","SerialNumber":"y"},{"SerialNumber":"x","Model":"same"}]}""";
        Equal(0, ReportComparer.Compare(a, b).Count);
    }),
    ("Duplicate and null device keys use aggregate comparison", () =>
    {
        foreach (var key in new[] { "\"same\"", "null" })
        {
            var a = "{\"SchemaVersion\":1,\"Network\":[{\"GUID\":" + key + ",\"MACAddress\":\"old\"},{\"GUID\":" + key + ",\"MACAddress\":\"second\"}]}";
            var b = a.Replace("old", "new");
            var diff = ReportComparer.Compare(a, b).Single(); Equal("Changed", diff.Kind);
            using var group = System.Text.Json.JsonDocument.Parse(diff.Before!); Equal(2, group.RootElement.GetArrayLength());
        }
    }),
    ("Partial query failure is unavailable rather than hardware removal", () =>
    {
        var a = """{"SchemaVersion":1,"Disks":[{"Model":"disk"}]}""";
        var b = """{"SchemaVersion":1,"Disks":[],"Warnings":["Win32_DiskDrive: Access denied"]}""";
        Equal("Unavailable", ReportComparer.Compare(a, b).Single().Kind);
    }),
    ("Missing category is unavailable rather than removal", () =>
    {
        var a = """{"SchemaVersion":1,"Disks":[{"Model":"disk"}],"Network":[]}""";
        var b = """{"SchemaVersion":1,"Network":[]}""";
        Equal("Unavailable", ReportComparer.Compare(a, b).Single().Kind);
    }),
    ("Comparison rejects invalid report schema and shape", () =>
    {
        var valid = """{"SchemaVersion":1,"Network":[]}""";
        foreach (var bad in new[] { "{}", "[]", "null", "{\"SchemaVersion\":\"1\",\"Network\":[]}", "{\"SchemaVersion\":2,\"Network\":[]}", "{\"SchemaVersion\":1,\"Network\":\"bad\"}", "{\"SchemaVersion\":1,\"Network\":[null]}" })
            Throws<InvalidDataException>(() => ReportComparer.Compare(bad, valid));
    }),
    ("Profile save normalizes MAC and persists across instances", () => WithDirectory(dir =>
    {
        var store = new ProfileStore(dir); var profile = store.Save("  Laboratorium  ", "02:aa:bb:cc:dd:ee");
        var saved = new ProfileStore(dir).List().Single(); Equal(profile, saved); Equal("Laboratorium", saved.Name); Equal("02AABBCCDDEE", saved.Mac);
    })),
    ("Profile validation rejects bad input without writing files", () => WithDirectory(dir =>
    {
        var store = new ProfileStore(dir);
        foreach (var name in new[] { "", " ", "bad\nname", new string('a', 65) }) Throws<ArgumentException>(() => store.Save(name, "02AABBCCDDEE"));
        Throws<ArgumentException>(() => store.Save("bad mac", "01AABBCCDDEE")); Equal(0, Directory.GetFiles(dir).Length);
    })),
    ("Duplicate profile names cannot overwrite existing MAC", () => WithDirectory(dir =>
    {
        var store = new ProfileStore(dir); store.Save("QA", "02AABBCCDDEE");
        Throws<InvalidOperationException>(() => store.Save("qa", "061122334455")); Equal("02AABBCCDDEE", store.List().Single().Mac);
    })),
    ("Deleting a profile preserves other profiles", () => WithDirectory(dir =>
    {
        var store = new ProfileStore(dir); var a = store.Save("A", "02AABBCCDDEE"); var b = store.Save("B", "061122334455");
        store.Delete(a.Id); Equal(b, store.List().Single());
    })),
    ("Profile name cannot escape storage directory", () => WithDirectory(dir =>
    {
        var store = new ProfileStore(dir); var profile = store.Save("../outside", "02AABBCCDDEE");
        Equal(profile.Id.ToString("N") + ".json", Path.GetFileName(Directory.GetFiles(dir).Single()));
    })),
    ("Corrupt profile blocks writes and is retained", () => WithDirectory(dir =>
    {
        var file = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(file, "corrupt");
        Throws<InvalidDataException>(() => new ProfileStore(dir).Save("QA", "02AABBCCDDEE")); Equal("corrupt", File.ReadAllText(file));
    })),
    ("Profile content identity must match its filename", () => WithDirectory(dir =>
    {
        File.WriteAllText(Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json"), System.Text.Json.JsonSerializer.Serialize(new MacProfile(1, Guid.NewGuid(), "QA", "02AABBCCDDEE", DateTimeOffset.UtcNow)));
        Throws<InvalidDataException>(() => new ProfileStore(dir).List());
    })),
    ("Profile missing required fields is rejected", () => WithDirectory(dir =>
    {
        File.WriteAllText(Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json"), "{\"Version\":1}");
        Throws<InvalidDataException>(() => new ProfileStore(dir).List());
    })),
    ("Oversized profile is rejected without modification", () => WithDirectory(dir =>
    {
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(path, new string(' ', 16385));
        Throws<InvalidDataException>(() => new ProfileStore(dir).List()); Equal(16385L, new FileInfo(path).Length);
    })),
    ("Profile save fails without replacing a blocking file", () => WithDirectory(dir =>
    {
        var path = Path.Combine(dir, "blocked"); File.WriteAllText(path, "preserve");
        Throws<IOException>(() => new ProfileStore(path).Save("QA", "02AABBCCDDEE")); Equal("preserve", File.ReadAllText(path));
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
static void WithDirectory(Action<string> action)
{
    var dir = Path.Combine(Path.GetTempPath(), "OpenHwidTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
    try { action(dir); } finally { Directory.Delete(dir, true); }
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
