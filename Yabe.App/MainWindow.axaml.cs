using Avalonia.Controls;

namespace Yabe;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}

public class DeviceTreeItem
{
    public string Header { get; set; }
    public System.Collections.ObjectModel.ObservableCollection<DeviceTreeItem> Children { get; } = new();
    public object? Tag { get; set; }
    public System.Collections.Generic.KeyValuePair<System.IO.BACnet.BacnetAddress, uint>? DeviceEntry { get; set; }
    public System.IO.BACnet.BacnetObjectId? ObjectId { get; set; }

    public DeviceTreeItem(string header) { Header = header; }
    public override string ToString() => Header;
}

public class PropertyItem
{
    public string Property { get; set; }
    public string Value { get; set; }
    public uint PropertyId { get; set; }
    public PropertyItem(string property, string value, uint propertyId = 0)
    {
        Property = property;
        Value = value;
        PropertyId = propertyId;
    }
}

public class SubscriptionItem
{
    public string Device { get; set; } = "";
    public string ObjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Time { get; set; } = "";
    public string Status { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
