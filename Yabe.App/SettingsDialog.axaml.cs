using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Yabe;

public partial class SettingsDialog : Window
{
    public static uint WritePriority { get; set; }
    public static uint YabeDeviceId { get; set; } = 12345;
    public static bool UdpExclusive { get; set; }
    public static bool DontFragment { get; set; }
    public static bool ShowTimestamp { get; set; } = true;
    public static bool AutoScroll { get; set; } = true;

    public SettingsDialog()
    {
        InitializeComponent();
        nudDeviceId.Value = YabeDeviceId;
        nudWritePriority.Value = WritePriority;
        chkUdpExclusive.IsChecked = UdpExclusive;
        chkDontFragment.IsChecked = DontFragment;
        chkShowTimestamp.IsChecked = ShowTimestamp;
        chkAutoScroll.IsChecked = AutoScroll;

        btnOk.Click += OnOk;
        btnCancel.Click += (_, _) => Close();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        YabeDeviceId = (uint)(nudDeviceId.Value ?? 12345);
        WritePriority = (uint)(nudWritePriority.Value ?? 0);
        UdpExclusive = chkUdpExclusive.IsChecked == true;
        DontFragment = chkDontFragment.IsChecked == true;
        ShowTimestamp = chkShowTimestamp.IsChecked == true;
        AutoScroll = chkAutoScroll.IsChecked == true;
        Close();
    }
}
