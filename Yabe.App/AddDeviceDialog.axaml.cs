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
    public bool SendWhoIs => chkSendWhoIs.IsChecked == true;
    public int WhoIsLow { get; private set; } = -1;
    public int WhoIsHigh { get; private set; } = -1;

    public AddDeviceDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        btnAddIp.Click += OnAddIp;
        btnAddMstp.Click += OnAddMstp;
        btnAddPtp.Click += OnAddPtp;
        btnAddEthernet.Click += OnAddEthernet;
        btnAddSC.Click += OnAddSC;
        btnScSelect.Click += OnScSelect;
        btnScEdit.Click += (_, _) => { };
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var ips = GetAvailableIps();
        foreach (var ip in ips)
            cmbLocalEndpoint.Items.Add(ip);

        if (cmbLocalEndpoint.Items.Count > 0)
            cmbLocalEndpoint.SelectedIndex = 0;

#if !ANDROID_BUILD
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
#endif
    }

    private void ParseWhoIsLimits()
    {
        if (int.TryParse(txtWhoIsLow.Text?.Trim(), out int low))
            WhoIsLow = low;
        if (int.TryParse(txtWhoIsHigh.Text?.Trim(), out int high))
            WhoIsHigh = high;
    }

    private void OnAddIp(object? sender, RoutedEventArgs e)
    {
        var localEp = cmbLocalEndpoint.SelectedItem?.ToString() ?? "";
        int port = 47808;
        try { port = Convert.ToInt32(txtPortHex.Text?.Trim() ?? "BAC0", 16); }
        catch { }
        var timeout = (int)(nudTimeout.Value ?? 500);
        var retries = (int)(nudRetries.Value ?? 1);

        ParseWhoIsLimits();

        if (localEp.Contains(':'))
        {
            var transport = new BacnetIpV6UdpProtocolTransport(port, 0, false, false, 1472, localEp);
            Result = new BacnetClient(transport, timeout, retries);
        }
        else
        {
            var transport = new BacnetIpUdpProtocolTransport(port, false, false, 1472, localEp);
            Result = new BacnetClient(transport, timeout, retries);
        }
        Close();
    }

#if !ANDROID_BUILD
    private void OnAddMstp(object? sender, RoutedEventArgs e)
    {
        var portName = cmbSerialPort.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(portName)) return;

        var baud = (int)(nudBaud.Value ?? 38400);
        var srcAddr = (short)(nudSourceAddr.Value ?? -1);
        var maxMaster = (byte)(nudMaxMaster.Value ?? 127);
        var maxFrames = (byte)(nudMaxFrames.Value ?? 1);
        var timeout = (int)(nudTimeout.Value ?? 500);
        var retries = (int)(nudRetries.Value ?? 1);

        ParseWhoIsLimits();

        var transport = new BacnetMstpProtocolTransport(portName, baud, srcAddr, maxMaster, maxFrames);
        Result = new BacnetClient(transport, timeout, retries);
        Close();
    }

    private void OnAddPtp(object? sender, RoutedEventArgs e)
    {
        var portName = cmbPtpPort.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(portName)) return;

        var baud = (int)(nudPtpBaud.Value ?? 38400);
        var timeout = (int)(nudTimeout.Value ?? 500);
        var retries = (int)(nudRetries.Value ?? 1);

        ParseWhoIsLimits();

        var transport = new BacnetPtpProtocolTransport(portName, baud, false);
        Result = new BacnetClient(transport, timeout, retries);
        Close();
    }

    private void OnAddEthernet(object? sender, RoutedEventArgs e)
    {
        var iface = cmbEthernetInterface.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(iface)) return;

        var ifaceName = iface.Split(':')[0];
        var timeout = (int)(nudTimeout.Value ?? 500);
        var retries = (int)(nudRetries.Value ?? 1);

        ParseWhoIsLimits();

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
#else
    private void OnAddMstp(object? sender, RoutedEventArgs e) { }
    private void OnAddPtp(object? sender, RoutedEventArgs e) { }
    private void OnAddEthernet(object? sender, RoutedEventArgs e) { }
#endif

    private void OnAddSC(object? sender, RoutedEventArgs e)
    {
        // BACnet/SC not yet implemented in the BACnet stack
    }

    private async void OnScSelect(object? sender, RoutedEventArgs e)
    {
#pragma warning disable CS0618
        var ofd = new OpenFileDialog
        {
            Title = "Select SC configuration file",
            Filters = new List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "JSON", Extensions = { "json" } },
                new FileDialogFilter { Name = "All files", Extensions = { "*" } }
            }
        };
        var result = await ofd.ShowAsync(this);
        if (result?.Length > 0)
            txtScConfig.Text = result[0];
#pragma warning restore CS0618
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
