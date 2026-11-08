using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Yabe;

public partial class AddDeviceDialog : Window
{
    public BacnetClient? Result { get; private set; }

    public AddDeviceDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        btnAddIp.Click += OnAddIp;
        btnAddIpV6.Click += OnAddIpV6;
        btnAddMstp.Click += OnAddMstp;
        btnAddPtp.Click += OnAddPtp;
        btnAddEthernet.Click += OnAddEthernet;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var ips = GetAvailableIps();
        foreach (var ip in ips)
        {
            if (ip.Contains(':'))
                cmbLocalEndpointV6.Items.Add(ip);
            else
                cmbLocalEndpoint.Items.Add(ip);
        }

        if (cmbLocalEndpoint.Items.Count > 0)
            cmbLocalEndpoint.SelectedIndex = 0;
        if (cmbLocalEndpointV6.Items.Count > 0)
            cmbLocalEndpointV6.SelectedIndex = 0;

        var ports = System.IO.Ports.SerialPort.GetPortNames();
        foreach (var port in ports)
        {
            cmbSerialPort.Items.Add(port);
            cmbPtpPort.Items.Add(port);
        }
        if (cmbSerialPort.Items.Count > 0)
            cmbSerialPort.SelectedIndex = 0;
        if (cmbPtpPort.Items.Count > 0)
            cmbPtpPort.SelectedIndex = 0;

        try { FillEthernetInterfaces(); }
        catch { }
    }

    private void OnAddIp(object? sender, RoutedEventArgs e)
    {
        var localEp = cmbLocalEndpoint.SelectedItem?.ToString() ?? "";
        int port = 47808;
        try { port = Convert.ToInt32(txtPortHex.Text?.Trim() ?? "BAC0", 16); }
        catch { }
        var timeout = (int)(nudTimeout.Value ?? 3000);
        var retries = (int)(nudRetries.Value ?? 3);

        var transport = new BacnetIpUdpProtocolTransport(port, false, false, 1472, localEp);
        Result = new BacnetClient(transport, timeout, retries);
        Close();
    }

    private void OnAddIpV6(object? sender, RoutedEventArgs e)
    {
        var localEp = cmbLocalEndpointV6.SelectedItem?.ToString() ?? "";
        var port = (int)(nudPortV6.Value ?? 47808);
        var timeout = (int)(nudTimeout.Value ?? 3000);
        var retries = (int)(nudRetries.Value ?? 3);

        var transport = new BacnetIpV6UdpProtocolTransport(port, 0, false, false, 1472, localEp);
        Result = new BacnetClient(transport, timeout, retries);
        Close();
    }

    private void OnAddMstp(object? sender, RoutedEventArgs e)
    {
        var portName = cmbSerialPort.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(portName)) return;

        var baud = (int)(nudBaud.Value ?? 38400);
        var srcAddr = (short)(nudSourceAddr.Value ?? 0);
        var maxMaster = (byte)(nudMaxMaster.Value ?? 127);
        var maxFrames = (byte)(nudMaxFrames.Value ?? 1);
        var timeout = (int)(nudTimeout.Value ?? 3000);
        var retries = (int)(nudRetries.Value ?? 3);

        var transport = new BacnetMstpProtocolTransport(portName, baud, srcAddr, maxMaster, maxFrames);
        Result = new BacnetClient(transport, timeout, retries);
        Close();
    }

    private void OnAddPtp(object? sender, RoutedEventArgs e)
    {
        var portName = cmbPtpPort.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(portName)) return;

        var baud = (int)(nudPtpBaud.Value ?? 38400);
        var timeout = (int)(nudTimeout.Value ?? 3000);
        var retries = (int)(nudRetries.Value ?? 3);

        var transport = new BacnetPtpProtocolTransport(portName, baud, false);
        Result = new BacnetClient(transport, timeout, retries);
        Close();
    }

    private void OnAddEthernet(object? sender, RoutedEventArgs e)
    {
        var iface = cmbEthernetInterface.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(iface)) return;

        var ifaceName = iface.Split(':')[0];
        var timeout = (int)(nudTimeout.Value ?? 3000);
        var retries = (int)(nudRetries.Value ?? 3);
        var transport = new BacnetEthernetProtocolTransport(ifaceName);
        Result = new BacnetClient(transport, timeout, retries);
        Close();
    }

    private void FillEthernetInterfaces()
    {
        try
        {
            var devices = SharpPcap.LibPcap.LibPcapLiveDeviceList.Instance
                .Where(d => d.Interface != null);

            foreach (var device in devices)
            {
                if (!device.Opened)
                {
                    try
                    {
                        device.Open(new SharpPcap.DeviceConfiguration());
                        if (device.LinkType == PacketDotNet.LinkLayers.Ethernet
                            && device.Interface?.MacAddress != null)
                        {
                            cmbEthernetInterface.Items.Add(
                                device.Interface.FriendlyName + ": " + device.Interface.Description);
                        }
                        device.Close();
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private static string[] GetAvailableIps()
    {
        var ips = new List<string>();
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        foreach (var inf in interfaces)
        {
            if (!inf.IsReceiveOnly
                && inf.OperationalStatus == OperationalStatus.Up
                && inf.SupportsMulticast
                && inf.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            {
                var ipinfo = inf.GetIPProperties();
                foreach (var addr in ipinfo.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork
                        || addr.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        ips.Add(addr.Address.ToString());
                    }
                }
            }
        }
        return ips.ToArray();
    }
}
