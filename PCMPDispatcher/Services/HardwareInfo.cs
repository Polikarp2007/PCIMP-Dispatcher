using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PCMPDispatcher.Services;

/// <summary>
/// Collects a rich, uniquely-identifying fingerprint of the machine and user.
/// The stable HWID (used for binding) is a hash of a few unchanging parts;
/// the full fingerprint is sent to the server for the admin dashboard.
/// </summary>
public static class HardwareInfo
{
    // ── Stable HWID (must stay identical across runs) ─────────────────────
    public static string Hwid()
    {
        string cpu  = First("SELECT ProcessorId FROM Win32_Processor", "ProcessorId");
        string disk = First("SELECT SerialNumber FROM Win32_DiskDrive", "SerialNumber");
        string raw  = $"{Environment.MachineName}|{cpu}|{disk}";
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    // ── Full fingerprint for the dashboard ────────────────────────────────
    public static Dictionary<string, object> Collect()
    {
        var fp = new Dictionary<string, object>();

        fp["machine_name"] = Environment.MachineName;
        fp["windows_user"] = Environment.UserName;
        fp["user_domain"]  = Environment.UserDomainName;
        fp["os"]           = RuntimeInformation.OSDescription;
        fp["os_arch"]      = RuntimeInformation.OSArchitecture.ToString();
        fp["dotnet"]       = RuntimeInformation.FrameworkDescription;
        fp["cpu_count"]    = Environment.ProcessorCount;
        fp["local_time"]   = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        fp["timezone"]     = TimeZoneInfo.Local.Id;

        fp["cpu_name"]     = First("SELECT Name FROM Win32_Processor", "Name");
        fp["cpu_id"]       = First("SELECT ProcessorId FROM Win32_Processor", "ProcessorId");
        fp["motherboard"]  = First("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
        fp["mb_product"]   = First("SELECT Product FROM Win32_BaseBoard", "Product");
        fp["bios_serial"]  = First("SELECT SerialNumber FROM Win32_BIOS", "SerialNumber");
        fp["system_uuid"]  = First("SELECT UUID FROM Win32_ComputerSystemProduct", "UUID");
        fp["gpu"]          = First("SELECT Name FROM Win32_VideoController", "Name");

        // RAM (GB)
        string ramRaw = First("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", "TotalPhysicalMemory");
        if (ulong.TryParse(ramRaw, out var bytes))
            fp["ram_gb"] = Math.Round(bytes / 1024d / 1024d / 1024d, 1);

        // Physical disks (model + serial)
        fp["disks"] = All("SELECT Model, SerialNumber FROM Win32_DiskDrive",
            o => $"{Clean(o["Model"])}  |  SN: {Clean(o["SerialNumber"])}");

        // Logical volumes with their volume serial numbers (hex + signed-int form)
        fp["volumes"] = All("SELECT DeviceID, VolumeSerialNumber FROM Win32_LogicalDisk WHERE DriveType=3",
            o =>
            {
                string dev = Clean(o["DeviceID"]);
                string hex = Clean(o["VolumeSerialNumber"]);
                string dec = "";
                if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u))
                    dec = $" ({unchecked((int)u)})";
                return $"{dev} {hex}{dec}";
            });

        // MAC addresses of active adapters
        fp["macs"] = Macs();

        return fp;
    }

    // ── WMI helpers ───────────────────────────────────────────────────────

    private static string First(string query, string prop)
    {
        try
        {
            using var s = new ManagementObjectSearcher(query);
            foreach (ManagementObject o in s.Get())
            {
                string v = Clean(o[prop]);
                if (!string.IsNullOrEmpty(v) && v != "UNKNOWN") return v;
            }
        }
        catch { }
        return "";
    }

    private static List<string> All(string query, Func<ManagementObject, string> map)
    {
        var list = new List<string>();
        try
        {
            using var s = new ManagementObjectSearcher(query);
            foreach (ManagementObject o in s.Get())
            {
                string v = map(o)?.Trim() ?? "";
                if (!string.IsNullOrEmpty(v)) list.Add(v);
            }
        }
        catch { }
        return list;
    }

    private static List<string> Macs()
    {
        var list = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var mac = nic.GetPhysicalAddress().ToString();
                if (string.IsNullOrEmpty(mac)) continue;
                string pretty = string.Join(":", Enumerable.Range(0, mac.Length / 2)
                    .Select(i => mac.Substring(i * 2, 2)));
                list.Add($"{nic.Name}: {pretty}");
            }
        }
        catch { }
        return list;
    }

    private static string Clean(object? v) => v?.ToString()?.Trim() ?? "";
}
