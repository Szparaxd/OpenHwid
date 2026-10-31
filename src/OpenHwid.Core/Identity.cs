using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenHwid.Core;

public static class MacAddress
{
    public static string Normalize(string input)
    {
        if (input is null || !Regex.IsMatch(input, @"\A(?:[0-9a-fA-F]{12}|(?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}|(?:[0-9a-fA-F]{2}-){5}[0-9a-fA-F]{2})\z"))
            throw new ArgumentException("Wpisz 12 cyfr szesnastkowych lub 6 par oddzielonych : lub -.");
        var value = input.Replace(":", "").Replace("-", "").ToUpperInvariant();
        if ((Convert.ToByte(value[..2], 16) & 3) != 2)
            throw new ArgumentException("Adres musi być lokalnie administrowany i unicast (np. zaczynać się od 02).");
        return value;
    }
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(6);
        bytes[0] = (byte)((bytes[0] & 0xFC) | 2);
        return Convert.ToHexString(bytes);
    }
}

public record AdapterState(Guid Id, string? Override, string CurrentMac);
public record AdapterBackup(int Version, string Machine, Guid Adapter, string? OriginalOverride, string OriginalMac, DateTimeOffset Created);

public interface IAdapterAccess
{
    AdapterState Read(Guid id);
    void WriteOverride(Guid id, string? value);
    void Restart(Guid id);
}

public sealed class BackupStore(string directory)
{
    private string FilePath(Guid id) => Path.Combine(directory, id.ToString("N") + ".json");
    public AdapterBackup? Read(Guid id)
    {
        try
        {
            using var stream = File.OpenRead(FilePath(id));
            return JsonSerializer.Deserialize<AdapterBackup>(stream) ?? throw new InvalidDataException("Pusta kopia ustawień.");
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    public void SaveFirst(AdapterBackup backup)
    {
        Directory.CreateDirectory(directory);
        // Complete the file before publishing it. An existing first backup is never replaced.
        var temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, backup);
                stream.Flush(true);
            }
            try { File.Move(temp, FilePath(backup.Adapter), false); }
            catch (IOException) when (File.Exists(FilePath(backup.Adapter))) { }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class MacService(IAdapterAccess adapters, BackupStore backups, string machine)
{
    public string Change(Guid id, string mac)
    {
        var value = MacAddress.Normalize(mac);
        var before = adapters.Read(id);
        var backup = backups.Read(id);
        if (backup is null)
        {
            backups.SaveFirst(new(1, machine, id, before.Override, before.CurrentMac, DateTimeOffset.UtcNow));
            backup = backups.Read(id) ?? throw new IOException("Nie zapisano kopii ustawień.");
        }
        Validate(backup, id);
        adapters.WriteOverride(id, value);
        adapters.Restart(id);
        var after = adapters.Read(id);
        if (!SameMac(after.CurrentMac, value))
            throw new InvalidOperationException("Sterownik nie zastosował MAC. Kopia została zachowana; użyj Przywróć. Może być wymagany restart komputera.");
        return after.CurrentMac;
    }
    public string Restore(Guid id)
    {
        var backup = backups.Read(id) ?? throw new InvalidOperationException("Brak kopii ustawień dla tej karty.");
        Validate(backup, id);
        adapters.WriteOverride(id, backup.OriginalOverride);
        adapters.Restart(id);
        var after = adapters.Read(id);
        if (after.Override != backup.OriginalOverride || !SameMac(after.CurrentMac, backup.OriginalMac))
            throw new InvalidOperationException("Przywrócono ustawienie, ale odczyt MAC nie odpowiada kopii. Sprawdź kartę lub uruchom komputer ponownie. Kopia pozostaje dostępna.");
        return after.CurrentMac;
    }
    private void Validate(AdapterBackup backup, Guid id)
    {
        if (backup.Version != 1 || backup.Adapter != id || backup.Machine != machine ||
            !Regex.IsMatch(backup.OriginalMac ?? "", @"\A[0-9a-fA-F]{12}\z"))
            throw new InvalidOperationException("Kopia jest nieprawidłowa albo pochodzi z innego komputera lub karty.");
    }
    private static bool SameMac(string a, string b) =>
        string.Equals(a.Replace("-", "").Replace(":", ""), b.Replace("-", "").Replace(":", ""), StringComparison.OrdinalIgnoreCase);
}
