using System.Text.Json;

namespace OpenHwid.Core;

public record MacProfile(int Version, Guid Id, string Name, string Mac, DateTimeOffset Created);
public sealed class ProfileStore(string directory)
{
    public IReadOnlyList<MacProfile> List()
    {
        if (!Directory.Exists(directory)) return [];
        var result = new List<MacProfile>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                if (new FileInfo(path).Length > 16 * 1024) throw new InvalidDataException("Plik profilu jest za duży.");
                var profile = JsonSerializer.Deserialize<MacProfile>(File.ReadAllText(path)) ?? throw new InvalidDataException("Pusty profil.");
                if (profile.Version != 1 || profile.Id == Guid.Empty || profile.Created == default ||
                    !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id) || id != profile.Id ||
                    NormalizeName(profile.Name) != profile.Name || MacAddress.Normalize(profile.Mac) != profile.Mac)
                    throw new InvalidDataException("Nieprawidłowy profil.");
                result.Add(profile);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidDataException)
            { throw new InvalidDataException("Nie można odczytać profilu " + Path.GetFileName(path) + ". Plik pozostawiono bez zmian.", ex); }
        }
        if (result.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
            throw new InvalidDataException("Powtarzające się nazwy profili. Sprawdź pliki profili.");
        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public MacProfile Save(string name, string mac)
    {
        var normalizedName = NormalizeName(name); var normalizedMac = MacAddress.Normalize(mac);
        if (List().Any(p => string.Equals(p.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Profil o tej nazwie już istnieje. Wybierz inną nazwę.");
        var profile = new MacProfile(1, Guid.NewGuid(), normalizedName, normalizedMac, DateTimeOffset.UtcNow);
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, profile.Id.ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, profile); stream.Flush(true); }
            File.Move(temporary, FilePath(profile.Id), false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return profile;
    }
    public void Delete(Guid id) => File.Delete(FilePath(id));
    private string FilePath(Guid id) => Path.Combine(directory, id.ToString("N") + ".json");
    private static string NormalizeName(string name)
    {
        if (name is null || name.Length > 64 || name.Any(char.IsControl) || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nazwa profilu musi mieć od 1 do 64 znaków, bez znaków sterujących.");
        return name.Trim();
    }
}
