using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using OpenHwid.Core;

namespace OpenHwid.Windows;

internal static class PowerShell
{
    // Only fixed scripts and strongly typed GUIDs are passed here. No user-entered command text.
    public static string Run(string script)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(
            "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8; " + script)));
        using var process = Process.Start(start) ?? throw new IOException("Nie uruchomiono PowerShell.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(true); process.WaitForExit();
            throw new TimeoutException("PowerShell przekroczył limit 60 s. Jeśli trwała zmiana MAC, kopia pozostała dostępna. Sprawdź stan karty.");
        }
        Task.WaitAll(output, error);
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Result.Trim());
        return output.Result.Trim('\uFEFF', ' ', '\r', '\n');
    }
}

internal record NetworkAdapter(Guid Id, string Name, string Description, string Mac, string Status, bool CanChange, string Reason)
{
    public override string ToString() => $"{Name} — {Mac} ({Status})";
}

internal sealed class WindowsAdapters : IAdapterAccess
{
    private const string ClassPath = @"SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}";
    public static bool IsAdministrator
    {
        get { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
    }
    public static string MachineToken()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var id = key?.GetValue("MachineGuid") as string ?? throw new InvalidOperationException("Nie odczytano identyfikatora komputera.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    }
    private static RegistryKey OpenAdapter(Guid id, bool writable)
    {
        using var root = Registry.LocalMachine.OpenSubKey(ClassPath) ?? throw new IOException("Brak klasy kart sieciowych.");
        foreach (var name in root.GetSubKeyNames())
        {
            using var candidate = root.OpenSubKey(name);
            if (Guid.TryParse(candidate?.GetValue("NetCfgInstanceId") as string, out var found) && found == id)
                return Registry.LocalMachine.OpenSubKey(ClassPath + "\\" + name, writable) ?? throw new IOException("Brak klucza karty.");
        }
        throw new InvalidOperationException("Karta nie istnieje lub została usunięta.");
    }
    private static bool SupportsOverride(RegistryKey key)
    {
        using var parameter = key.OpenSubKey(@"Ndi\params\NetworkAddress");
        return parameter is not null;
    }
    private static NetworkInterface Find(Guid id) => NetworkInterface.GetAllNetworkInterfaces()
        .SingleOrDefault(n => Guid.TryParse(n.Id, out var found) && found == id)
        ?? throw new InvalidOperationException("Nie znaleziono interfejsu sieciowego.");
    public IReadOnlyList<NetworkAdapter> List()
    {
        var result = new List<NetworkAdapter>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!Guid.TryParse(nic.Id, out var id) || nic.GetPhysicalAddress().GetAddressBytes().Length != 6) continue;
            var supported = false; var reason = "Sterownik nie udostępnia parametru NetworkAddress.";
            try { using var key = OpenAdapter(id, false); supported = SupportsOverride(key); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException) { reason = ex.Message; }
            if (nic.OperationalStatus == OperationalStatus.Down) { supported = false; reason = "Włącz i podłącz kartę przed zmianą adresu."; }
            result.Add(new(id, nic.Name, nic.Description, nic.GetPhysicalAddress().ToString(), nic.OperationalStatus.ToString(), supported, supported ? "Sterownik udostępnia NetworkAddress; wynik zostanie sprawdzony po zmianie." : reason));
        }
        return result;
    }
    public AdapterState Read(Guid id)
    {
        using var key = OpenAdapter(id, false);
        var value = key.GetValue("NetworkAddress");
        if (value is not null && (key.GetValueKind("NetworkAddress") != RegistryValueKind.String || value is not string))
            throw new InvalidOperationException("Nietypowy format NetworkAddress. Zmiana została zablokowana.");
        return new(id, (string?)value, Find(id).GetPhysicalAddress().ToString());
    }
    public void WriteOverride(Guid id, string? value)
    {
        if (!IsAdministrator) throw new UnauthorizedAccessException("Uruchom aplikację jako administrator, aby zmieniać ustawienia.");
        using var key = OpenAdapter(id, true);
        // Restoration remains possible even if the driver stops advertising the property.
        if (value is null) key.DeleteValue("NetworkAddress", false);
        else key.SetValue("NetworkAddress", value, RegistryValueKind.String);
        key.Flush();
    }
    public void Restart(Guid id)
    {
        PowerShell.Run($"$a = @(Get-NetAdapter -IncludeHidden | Where-Object {{ $_.InterfaceGuid -eq [guid]'{id:D}' }}); if ($a.Count -ne 1) {{ throw 'Adapter not found or ambiguous' }}; $a[0] | Restart-NetAdapter -Confirm:$false");
        // Give the driver time to publish the new address; reading a fresh interface avoids cached values.
        Thread.Sleep(1500);
    }
}

internal static class Inventory
{
    public static string Read() => PowerShell.Run("""
        $warnings = [System.Collections.Generic.List[string]]::new()
        function Read-Class($class, $fields) {
            try { @(Get-CimInstance -ClassName $class | Select-Object -Property $fields) }
            catch { $warnings.Add($class + ': ' + $_.Exception.Message); @() }
        }
        [ordered]@{
            SchemaVersion = 1
            CapturedAtUtc = [DateTime]::UtcNow.ToString('o')
            Computer = @(Read-Class 'Win32_ComputerSystemProduct' @('Vendor','Name','UUID','IdentifyingNumber'))
            Bios = @(Read-Class 'Win32_BIOS' @('Manufacturer','SMBIOSBIOSVersion','SerialNumber'))
            BaseBoard = @(Read-Class 'Win32_BaseBoard' @('Manufacturer','Product','SerialNumber'))
            Cpu = @(Read-Class 'Win32_Processor' @('DeviceID','Name','ProcessorId'))
            Disks = @(Read-Class 'Win32_DiskDrive' @('DeviceID','Model','SerialNumber','InterfaceType'))
            Volumes = @(Read-Class 'Win32_LogicalDisk' @('DeviceID','VolumeName','VolumeSerialNumber'))
            Network = @(Read-Class 'Win32_NetworkAdapter' @('Name','GUID','MACAddress','PhysicalAdapter'))
            Warnings = @($warnings.ToArray())
        } | ConvertTo-Json -Depth 6
        """);
    public static string Pretty(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }
}
