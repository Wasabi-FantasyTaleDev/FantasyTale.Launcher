using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Hosting;

/// <summary>Lists private IPv4 interfaces that a LAN-only legacy game server can bind explicitly.</summary>
public static class LocalServerNetwork
{
    /// <summary>Finds four distinct listener ports for a new local profile; startup still reports bind races.</summary>
    public static IReadOnlyList<int> AvailablePorts()
    {
        var listeners = new List<TcpListener>();
        try
        {
            for (var index = 0; index < 4; index++)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listeners.Add(listener); listener.Start();
            }
            return listeners.Select(listener => ((IPEndPoint)listener.LocalEndpoint).Port).ToArray();
        }
        finally { foreach (var listener in listeners) listener.Stop(); }
    }

    /// <summary>Returns usable private addresses assigned to running local network interfaces.</summary>
    public static IReadOnlyList<string> Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses).Select(address => address.Address)
        .Where(IsLan).Select(address => address.ToString()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Accepts RFC1918 and link-local IPv4 addresses; public, wildcard and loopback addresses are excluded.</summary>
    public static bool IsLan(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 169 && bytes[1] == 254;
    }

    /// <summary>Rejects stale LAN settings before process startup instead of falling back to all interfaces.</summary>
    public static void RequireAssigned(LocalServerProfile profile)
    {
        if (profile.Mode == "lan" && !Addresses().Contains(profile.AdvertisedHost, StringComparer.Ordinal))
            throw new InvalidOperationException("The selected LAN address is no longer assigned to this computer.");
    }
}
