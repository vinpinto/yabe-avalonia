using System;
using System.IO.BACnet;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Avalonia.Controls;

namespace Yabe;

public partial class ForeignRegistryDialog : Window
{
    private readonly BacnetClient _client;

    public ForeignRegistryDialog(BacnetClient client)
    {
        InitializeComponent();
        _client = client;

        btnRegister.Click += OnRegister;
        btnWhoIs.Click += OnWhoIs;
    }

    private void OnRegister(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var ip = ResolveIp(txtBbmdIp.Text ?? "");
            if (ip == null) { lblStatus.Text = "Invalid IP address"; return; }

            if (!int.TryParse(txtBbmdPort.Text, out int port)) port = 47808;
            _client.RegisterAsForeignDevice(ip, 30, port);
            Thread.Sleep(50);
            _client.RemoteWhoIs(ip, port);
            btnWhoIs.IsEnabled = true;
            lblStatus.Text = "Registered as foreign device";
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void OnWhoIs(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var ip = ResolveIp(txtBbmdIp.Text ?? "");
            if (ip == null) return;
            if (!int.TryParse(txtBbmdPort.Text, out int port)) port = 47808;
            _client.RemoteWhoIs(ip, port);
            lblStatus.Text = "WhoIs sent";
        }
        catch { }
    }

    private string? ResolveIp(string host)
    {
        try
        {
            var ips = Dns.GetHostAddresses(host);
            var match = _client.Transport is BacnetIpUdpProtocolTransport
                ? ips.FirstOrDefault(o => o.AddressFamily == AddressFamily.InterNetwork)
                : ips.FirstOrDefault(o => o.AddressFamily == AddressFamily.InterNetworkV6);
            return match?.ToString();
        }
        catch { return null; }
    }
}
