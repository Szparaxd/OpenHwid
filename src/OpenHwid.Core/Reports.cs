using System.Text.Json;

namespace OpenHwid.Core;

public record ReportDifference(string Category, string Device, string Property, string? Before, string? After, string Kind);
public static class ReportComparer
{
    private static readonly Dictionary<string, string> Categories = new()
    {
        ["Computer"] = "Win32_ComputerSystemProduct", ["Bios"] = "Win32_BIOS", ["BaseBoard"] = "Win32_BaseBoard",
        ["Cpu"] = "Win32_Processor", ["Disks"] = "Win32_DiskDrive", ["Volumes"] = "Win32_LogicalDisk", ["Network"] = "Win32_NetworkAdapter"
    };
    public static IReadOnlyList<ReportDifference> Compare(string before, string after)
    {
        using var a = Parse(before); using var b = Parse(after);
        var differences = new List<ReportDifference>();
        foreach (var (category, className) in Categories)
        {
            var hasA = a.RootElement.TryGetProperty(category, out var itemsA);
            var hasB = b.RootElement.TryGetProperty(category, out var itemsB);
            if (!hasA && !hasB) continue;
            if (!hasA || !hasB || Failed(a, className) || Failed(b, className))
            {
                differences.Add(new(category, "", "", hasA && !Failed(a, className) ? "Odczyt dostępny" : "Brak odczytu", hasB && !Failed(b, className) ? "Odczyt dostępny" : "Brak odczytu", "Unavailable"));
                continue;
            }
            var key = category == "Network" ? "GUID" : "DeviceID";
            if (category == "Network") CompareNetwork(itemsA, itemsB, differences);
            else if (Keyed(itemsA, key) && Keyed(itemsB, key))
            {
                CompareKeyed(category, itemsA.EnumerateArray(), itemsB.EnumerateArray(), key, differences);
            }
            else if (category is "Computer" or "Bios" or "BaseBoard" or "Cpu" && itemsA.GetArrayLength() == 1 && itemsB.GetArrayLength() == 1)
                CompareObject(category, "", itemsA[0], itemsB[0], differences);
            else
            {
                var oldSet = CanonicalSet(itemsA); var newSet = CanonicalSet(itemsB);
                if (oldSet != newSet) differences.Add(new(category, "(cała kategoria)", "Brak jednoznacznego klucza urządzenia", oldSet, newSet, "Changed"));
            }
        }
        return differences;
    }
    private static void CompareKeyed(string category, IEnumerable<JsonElement> a, IEnumerable<JsonElement> b, string key, List<ReportDifference> differences)
    {
        var left = a.ToDictionary(x => x.GetProperty(key).GetString()!, StringComparer.OrdinalIgnoreCase);
        var right = b.ToDictionary(x => x.GetProperty(key).GetString()!, StringComparer.OrdinalIgnoreCase);
        foreach (var id in left.Keys.Union(right.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var foundA = left.TryGetValue(id, out var oldItem); var foundB = right.TryGetValue(id, out var newItem);
            if (!foundA || !foundB) differences.Add(new(category, id, "(urządzenie)", foundA ? Canonical(oldItem) : null, foundB ? Canonical(newItem) : null, foundA ? "Removed" : "Added"));
            else CompareObject(category, id, oldItem, newItem, differences);
        }
    }
    private static void CompareNetwork(JsonElement a, JsonElement b, List<ReportDifference> differences)
    {
        static string? Id(JsonElement item) => item.TryGetProperty("GUID", out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;
        var left = a.EnumerateArray().ToArray(); var right = b.EnumerateArray().ToArray();
        var ambiguous = left.GroupBy(Id, StringComparer.OrdinalIgnoreCase).Concat(right.GroupBy(Id, StringComparer.OrdinalIgnoreCase))
            .Where(group => group.Key is not null && group.Count() > 1).Select(group => group.Key!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Known(JsonElement item) => Id(item) is { } id && !ambiguous.Contains(id);
        CompareKeyed("Network", left.Where(Known), right.Where(Known), "GUID", differences);
        var oldUnknown = "[" + string.Join(",", left.Where(item => !Known(item)).Select(Canonical).Order(StringComparer.Ordinal)) + "]";
        var newUnknown = "[" + string.Join(",", right.Where(item => !Known(item)).Select(Canonical).Order(StringComparer.Ordinal)) + "]";
        if (oldUnknown != newUnknown)
            differences.Add(new("Network", "(bez jednoznacznego GUID)", "Wpisy bez jednoznacznego GUID", oldUnknown, newUnknown, "Changed"));
    }
    private static JsonDocument Parse(string json)
    {
        if (json.Length > 5 * 1024 * 1024) throw new InvalidDataException("Raport jest za duży (limit 5 MiB).");
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 }); }
        catch (JsonException ex) { throw new InvalidDataException("Nieprawidłowy JSON raportu.", ex); }
        try
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !UniqueProperties(root) || !root.TryGetProperty("SchemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schema) || schema != 1)
                throw new InvalidDataException("Wybierz raport OpenHwid w schemacie 1.");
            var found = false;
            foreach (var category in Categories.Keys)
            {
                if (!root.TryGetProperty(category, out var items)) continue;
                found = true;
                if (items.ValueKind != JsonValueKind.Array || items.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Object || !UniqueProperties(x) || x.EnumerateObject().Any(p => p.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)))
                    throw new InvalidDataException("Nieprawidłowa struktura kategorii: " + category);
            }
            if (!found) throw new InvalidDataException("Raport nie zawiera kategorii sprzętu.");
            if (root.TryGetProperty("Warnings", out var warnings) && (warnings.ValueKind != JsonValueKind.Array || warnings.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String)))
                throw new InvalidDataException("Nieprawidłowe ostrzeżenia raportu.");
            return doc;
        }
        catch { doc.Dispose(); throw; }
    }
    private static bool UniqueProperties(JsonElement item) => item.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == item.EnumerateObject().Count();
    private static bool Failed(JsonDocument doc, string name) => doc.RootElement.TryGetProperty("Warnings", out var warnings) && warnings.EnumerateArray().Any(x => x.GetString()!.StartsWith(name + ":", StringComparison.Ordinal));
    private static bool Keyed(JsonElement items, string key)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return items.EnumerateArray().All(item => item.TryGetProperty(key, out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()) && ids.Add(id.GetString()!));
    }
    private static string Canonical(JsonElement item) => JsonSerializer.Serialize(item.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToDictionary(p => p.Name, p => p.Value));
    private static string CanonicalSet(JsonElement items) => "[" + string.Join(",", items.EnumerateArray().Select(Canonical).Order(StringComparer.Ordinal)) + "]";
    private static void CompareObject(string category, string id, JsonElement a, JsonElement b, List<ReportDifference> differences)
    {
        foreach (var property in a.EnumerateObject().Select(p => p.Name).Union(b.EnumerateObject().Select(p => p.Name)).Order(StringComparer.Ordinal))
        {
            var hasA = a.TryGetProperty(property, out var oldValue); var hasB = b.TryGetProperty(property, out var newValue);
            if (hasA && hasB && oldValue.GetRawText() == newValue.GetRawText()) continue;
            // Compare decoded scalar values so different JSON escaping does not create a false change.
            if (hasA && hasB && oldValue.ValueKind == newValue.ValueKind && oldValue.ToString() == newValue.ToString()) continue;
            differences.Add(new(category, id, property, hasA && oldValue.ValueKind != JsonValueKind.Null ? oldValue.ToString() : null, hasB && newValue.ValueKind != JsonValueKind.Null ? newValue.ToString() : null, !hasA ? "Added" : !hasB ? "Removed" : "Changed"));
        }
    }
}
