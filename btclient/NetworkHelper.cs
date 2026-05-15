using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace btclient;

public static class NetworkHelper
{
    public static IPAddress? GetPrimaryIPv4()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();

        foreach (var ni in interfaces)
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;

            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var ipProps = ni.GetIPProperties();

            foreach (var ua in ipProps.UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ua.Address;
                }
            }
        }

        return null;
    }
}