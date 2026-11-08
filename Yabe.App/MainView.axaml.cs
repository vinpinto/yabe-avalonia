using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.BACnet;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;

namespace Yabe;

public partial class MainView : UserControl
{
    private readonly Dictionary<BacnetClient, BacnetDeviceLine> _devices = new();
    private readonly ObservableCollection<SubscriptionItem> _subscriptions = new();
    private readonly ObservableCollection<PropertyItem> _properties = new();
    private readonly Dictionary<string, List<double>> _covDataX = new();
    private readonly Dictionary<string, List<double>> _covDataY = new();
    private uint _nextSubscriptionId;
    private int _asyncRequestId;
    private DeviceTreeItem? _selectedDevice;
    private bool _plotPaused;

    public MainView()
    {
        InitializeComponent();

#if ANDROID_BUILD
        Padding = new Avalonia.Thickness(0, 48, 0, 0);
#endif

        dgSubscriptions.ItemsSource = _subscriptions;
        dgProperties.ItemsSource = _properties;

        btnSearch.Click += OnAddDevice;
        btnWhoIs.Click += OnSendWhoIs;
        btnRemove.Click += OnRemoveDevice;
        mnuAddDevice.Click += OnAddDevice;
        mnuWhoIs.Click += OnSendWhoIs;
        mnuRemoveDevice.Click += OnRemoveDevice;
        mnuQuit.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window w) w.Close();
        };
        mnuAbout.Click += OnAbout;
        mnuSubscribe.Click += OnSubscribe;
        mnuAlarmSummary.Click += OnAlarmSummary;
        mnuSchedule.Click += OnShowSchedule;
        mnuTimeSync.Click += OnTimeSync;
        mnuTrendLog.Click += OnShowTrendLog;
        mnuNotification.Click += OnShowNotification;
        mnuCalendar.Click += OnShowCalendar;
        mnuCreateObject.Click += OnCreateObject;
        mnuCommControl.Click += OnCommControl;
        mnuDownloadFile.Click += OnDownloadFile;
        mnuUploadFile.Click += OnUploadFile;
        mnuForeignRegistry.Click += OnForeignRegistry;
        mnuSettings.Click += OnSettings;

        btnPlayPause.Click += OnPlayPause;
        btnClearPlot.Click += OnClearPlot;
        btnExport.Click += OnExportCsv;

        treeDevices.SelectionChanged += OnDeviceTreeSelectionChanged;
        treeAddressSpace.SelectionChanged += OnAddressSpaceSelectionChanged;
        dgProperties.CellEditEnding += OnPropertyCellEditEnding;

        ctxDeviceWhoIs.Click += OnSendWhoIs;
        ctxDeviceRemove.Click += OnRemoveDevice;
        ctxDeviceRefresh.Click += (_, _) => OnDeviceTreeSelectionChanged(null, null!);
        ctxAddrSubscribe.Click += OnSubscribe;
        ctxAddrReadAll.Click += (_, _) => OnAddressSpaceSelectionChanged(null, null!);
        ctxAddrTrendLog.Click += OnShowTrendLog;
        ctxAddrSchedule.Click += OnShowSchedule;
        ctxAddrNotification.Click += OnShowNotification;
        ctxAddrCalendar.Click += OnShowCalendar;
        ctxAddrDownload.Click += OnDownloadFile;
        ctxAddrUpload.Click += OnUploadFile;

        var rootNode = new DeviceTreeItem("Devices");
        treeDevices.ItemsSource = new ObservableCollection<DeviceTreeItem> { rootNode };

        covPlot.Plot.Title("COV Values");
        covPlot.Plot.YLabel("Value");
        covPlot.Plot.XLabel("Sample");
        covPlot.Plot.Axes.Title.Label.FontSize = 10;
        covPlot.Plot.Axes.Left.Label.FontSize = 10;
        covPlot.Plot.Axes.Bottom.Label.FontSize = 10;
        covPlot.Plot.Axes.Left.TickLabelStyle.FontSize = 10;
        covPlot.Plot.Axes.Bottom.TickLabelStyle.FontSize = 10;
        covPlot.Refresh();

        AppendLog("YABE Avalonia - Ready");
        AppendLog("Click 'Add Device' to start BACnet discovery");
    }

    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    private DeviceTreeItem RootNode =>
        ((ObservableCollection<DeviceTreeItem>)treeDevices.ItemsSource!)[0];

    private void AppendLog(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            txtLog.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            txtLog.CaretIndex = txtLog.Text?.Length ?? 0;
        });
    }

    // --- Device Discovery ---

    private async void OnAddDevice(object? sender, RoutedEventArgs e)
    {
        var dialog = new AddDeviceDialog();
        var owner = OwnerWindow;
        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        var comm = dialog.Result;
        if (comm == null) return;

        try
        {
            _devices.Add(comm, new BacnetDeviceLine(comm));
        }
        catch
        {
            AppendLog("Transport already added");
            return;
        }

        var transportName = comm.GetType().Name;
        if (comm.Transport is BacnetIpUdpProtocolTransport ipTransport)
            transportName = $"BACnet/IP ({ipTransport.SharedPort})";
        else if (comm.Transport is BacnetIpV6UdpProtocolTransport)
            transportName = "BACnet/IPv6";
#if !ANDROID_BUILD
        else if (comm.Transport is BacnetMstpProtocolTransport)
            transportName = "BACnet/MSTP";
        else if (comm.Transport is BacnetEthernetProtocolTransport)
            transportName = "BACnet/Ethernet";
#endif

        var transportNode = new DeviceTreeItem(transportName)
        {
            Tag = comm
        };
        RootNode.Children.Add(transportNode);

        comm.OnIam += OnIam;
        comm.OnCOVNotification += OnCOVNotification;
        comm.Start();

        AppendLog($"Started {transportName}");

        if (dialog.SendWhoIs)
        {
            var whoIsLow = dialog.WhoIsLow;
            var whoIsHigh = dialog.WhoIsHigh;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                for (int i = 0; i < comm.Retries; i++)
                {
                    comm.WhoIs(whoIsLow, whoIsHigh);
                    Thread.Sleep(comm.Timeout);
                }
                AppendLog("WhoIs discovery complete");
            });
        }
    }

    private void OnSendWhoIs(object? sender, RoutedEventArgs e)
    {
        foreach (var entry in _devices)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                entry.Key.WhoIs();
            });
        }
        AppendLog("Sent WhoIs on all transports");
    }

    private void OnRemoveDevice(object? sender, RoutedEventArgs e)
    {
        if (treeDevices.SelectedItem is not DeviceTreeItem selected) return;
        if (selected.Tag is BacnetClient comm)
        {
            _devices.Remove(comm);
            comm.Dispose();
            RootNode.Children.Remove(selected);
            AppendLog("Removed transport");
        }
    }

    private void OnIam(BacnetClient sender, BacnetAddress adr, uint deviceId,
        uint maxApdu, BacnetSegmentations segmentation, ushort vendorId)
    {
        var newEntry = new KeyValuePair<BacnetAddress, uint>(adr, deviceId);
        lock (_devices)
        {
            if (!_devices.ContainsKey(sender)) return;
            if (_devices[sender].Devices.Any(d => d.Key.Equals(adr) && d.Value == deviceId))
                return;
            _devices[sender].Devices.Add(newEntry);
        }

        Dispatcher.UIThread.Post(() =>
        {
            var parent = RootNode.Children.FirstOrDefault(n => n.Tag as BacnetClient == sender);
            if (parent == null) return;

            foreach (var existing in parent.Children)
            {
                if (existing.DeviceEntry?.Key.Equals(adr) == true)
                {
                    existing.Header = $"Device {deviceId} - {adr}";
                    return;
                }
            }

            var node = new DeviceTreeItem($"Device {deviceId} - {adr}")
            {
                DeviceEntry = newEntry,
                Tag = sender
            };
            parent.Children.Add(node);

            AppendLog($"Found device {deviceId} at {adr}");
        });
    }

    // --- Address Space Browsing ---

    private void OnDeviceTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (treeDevices.SelectedItem is not DeviceTreeItem selected) return;
        if (selected.DeviceEntry == null) return;

        _asyncRequestId++;
        var currentRequestId = _asyncRequestId;
        _selectedDevice = selected;

        var comm = FindCommForDevice(selected);
        if (comm == null) return;

        var adr = selected.DeviceEntry.Value.Key;
        var deviceId = selected.DeviceEntry.Value.Value;

        var addressItems = new ObservableCollection<DeviceTreeItem>();
        treeAddressSpace.ItemsSource = addressItems;
        _properties.Clear();

        AppendLog($"Reading object list from device {deviceId}...");

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                IList<BacnetValue>? valueList;
                if (!comm.ReadPropertyRequest(adr,
                    new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceId),
                    BacnetPropertyIds.PROP_OBJECT_LIST, out valueList))
                {
                    AppendLog("Couldn't read object list - trying one by one");
                    ReadObjectListOneByOne(comm, adr, deviceId, addressItems, currentRequestId);
                    return;
                }

                if (currentRequestId != _asyncRequestId) return;

                var sorted = SortBacnetObjects(valueList);
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var objId in sorted)
                    {
                        if (currentRequestId != _asyncRequestId) return;
                        var typeName = objId.ToString();
                        if (typeName.StartsWith("OBJECT_"))
                            typeName = typeName.Substring(7);
                        addressItems.Add(new DeviceTreeItem(typeName) { ObjectId = objId });
                    }
                    AppendLog($"Loaded {sorted.Count} objects from device {deviceId}");
                });

                ResolveObjectNames(comm, adr, sorted, addressItems, currentRequestId);

                try
                {
                    IList<BacnetValue> nameValues;
                    if (comm.ReadPropertyRequest(adr,
                        new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceId),
                        BacnetPropertyIds.PROP_OBJECT_NAME, out nameValues))
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            selected.Header = $"{nameValues[0]} [{deviceId}]";
                        });
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                AppendLog($"Error reading objects: {ex.Message}");
            }
        });
    }

    private void ReadObjectListOneByOne(BacnetClient comm, BacnetAddress adr,
        uint deviceId, ObservableCollection<DeviceTreeItem> items, int requestId)
    {
        try
        {
            IList<BacnetValue> countList;
            if (!comm.ReadPropertyRequest(adr,
                new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceId),
                BacnetPropertyIds.PROP_OBJECT_LIST, out countList, 0, 0))
            {
                AppendLog("Couldn't read object list count");
                return;
            }

            if (countList.Count == 1 && countList[0].Value is ulong count)
            {
                for (uint i = 1; i <= (uint)count; i++)
                {
                    if (requestId != _asyncRequestId) return;

                    IList<BacnetValue> objValue;
                    if (comm.ReadPropertyRequest(adr,
                        new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceId),
                        BacnetPropertyIds.PROP_OBJECT_LIST, out objValue, 0, i))
                    {
                        foreach (var val in objValue)
                        {
                            if (val.Value is BacnetObjectId objId)
                            {
                                var name = objId.ToString();
                                if (name.StartsWith("OBJECT_"))
                                    name = name.Substring(7);
                                Dispatcher.UIThread.Post(() =>
                                    items.Add(new DeviceTreeItem(name) { ObjectId = objId }));
                            }
                        }
                    }
                }
                AppendLog($"Loaded {count} objects (one by one) from device {deviceId}");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
        }
    }

    // --- Property Reading ---

    private void OnAddressSpaceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (treeAddressSpace.SelectedItem is not DeviceTreeItem selected) return;
        if (selected.ObjectId == null || _selectedDevice == null) return;

        var comm = FindCommForDevice(_selectedDevice);
        if (comm == null) return;

        var adr = _selectedDevice.DeviceEntry!.Value.Key;
        var objId = selected.ObjectId.Value;

        _properties.Clear();

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                IList<BacnetValue> values;
                var propList = new List<BacnetPropertyIds>
                {
                    BacnetPropertyIds.PROP_OBJECT_IDENTIFIER,
                    BacnetPropertyIds.PROP_OBJECT_NAME,
                    BacnetPropertyIds.PROP_OBJECT_TYPE,
                    BacnetPropertyIds.PROP_PRESENT_VALUE,
                    BacnetPropertyIds.PROP_DESCRIPTION,
                    BacnetPropertyIds.PROP_STATUS_FLAGS,
                    BacnetPropertyIds.PROP_EVENT_STATE,
                    BacnetPropertyIds.PROP_OUT_OF_SERVICE,
                    BacnetPropertyIds.PROP_UNITS,
                    BacnetPropertyIds.PROP_RELIABILITY,
                    BacnetPropertyIds.PROP_PRIORITY_ARRAY,
                    BacnetPropertyIds.PROP_RELINQUISH_DEFAULT,
                    BacnetPropertyIds.PROP_COV_INCREMENT,
                    BacnetPropertyIds.PROP_MIN_PRES_VALUE,
                    BacnetPropertyIds.PROP_MAX_PRES_VALUE,
                };

                var propRefs = new List<BacnetPropertyReference>
                {
                    new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_ALL, uint.MaxValue)
                };

                IList<BacnetReadAccessResult> multiValues;
                if (comm.ReadPropertyMultipleRequest(adr, objId, propRefs, out multiValues))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var result in multiValues)
                        {
                            if (result.values == null) continue;
                            foreach (var prop in result.values)
                            {
                                var propName = ((BacnetPropertyIds)prop.property.propertyIdentifier).ToString();
                                if (propName.StartsWith("PROP_"))
                                    propName = propName.Substring(5);

                                var valText = ConvertToText(prop.value);
                                _properties.Add(new PropertyItem(propName, valText, prop.property.propertyIdentifier));
                            }
                        }
                    });
                    return;
                }

                foreach (var propId in propList)
                {
                    try
                    {
                        if (comm.ReadPropertyRequest(adr, objId, propId, out values))
                        {
                            var propName = propId.ToString();
                            if (propName.StartsWith("PROP_"))
                                propName = propName.Substring(5);

                            var valText = ConvertToText(values);
                            Dispatcher.UIThread.Post(() =>
                                _properties.Add(new PropertyItem(propName, valText, (uint)propId)));
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Error reading properties: {ex.Message}");
            }
        });
    }

    // --- COV Subscriptions ---

    private void OnSubscribe(object? sender, RoutedEventArgs e)
    {
        if (treeAddressSpace.SelectedItem is not DeviceTreeItem selected) return;
        if (selected.ObjectId == null || _selectedDevice == null) return;

        var comm = FindCommForDevice(_selectedDevice);
        if (comm == null) return;

        var adr = _selectedDevice.DeviceEntry!.Value.Key;
        var objId = selected.ObjectId.Value;
        var deviceId = _selectedDevice.DeviceEntry!.Value.Value;
        var subId = _nextSubscriptionId++;
        var useCov = rbCov.IsChecked == true;

        var subItem = new SubscriptionItem
        {
            Device = deviceId.ToString(),
            ObjectId = objId.ToString().Replace("OBJECT_", ""),
            Value = "...",
            Status = "Subscribing",
            Time = DateTime.Now.ToString("HH:mm:ss")
        };
        _subscriptions.Add(subItem);

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                bool ok;
                if (useCov)
                {
                    ok = comm.SubscribeCOVRequest(adr, objId, subId, false, false, 600);
                }
                else
                {
                    ok = false;
                    var period = (uint)(nudPollRate.Value ?? 1000);
                    StartPolling(comm, adr, objId, subItem, period);
                    Dispatcher.UIThread.Post(() =>
                    {
                        subItem.Status = "Polling";
                    });
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    subItem.Status = ok ? "COV OK" : "Failed";
                    subItem.IsActive = ok;
                    subItem.Time = DateTime.Now.ToString("HH:mm:ss");
                });

                if (ok)
                    AppendLog($"COV subscription active for {objId}");
                else
                    AppendLog($"COV subscription failed for {objId}");
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    subItem.Status = "Error";
                    subItem.Description = ex.Message;
                    subItem.IsActive = false;
                });
            }
        });
    }

    private void StartPolling(BacnetClient comm, BacnetAddress adr,
        BacnetObjectId objId, SubscriptionItem sub, uint periodMs)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            while (sub.IsActive)
            {
                try
                {
                    IList<BacnetValue> values;
                    if (comm.ReadPropertyRequest(adr, objId,
                        BacnetPropertyIds.PROP_PRESENT_VALUE, out values))
                    {
                        var text = ConvertToText(values);
                        Dispatcher.UIThread.Post(() =>
                        {
                            sub.Value = text;
                            sub.Time = DateTime.Now.ToString("HH:mm:ss");
                            sub.Status = "OK";
                        });
                        if (double.TryParse(text, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double dVal))
                            AddCovDataPoint(sub.ObjectId, dVal);
                    }
                }
                catch { }
                Thread.Sleep((int)periodMs);
            }
        });
    }

    private void OnCOVNotification(BacnetClient sender, BacnetAddress adr,
        byte invokeId, uint subscriberProcessId,
        BacnetObjectId initiatingDevice, BacnetObjectId monitoredObject,
        uint timeRemaining, bool needConfirm,
        ICollection<BacnetPropertyValue> values, BacnetMaxSegments maxSegments)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var sub = _subscriptions.FirstOrDefault(s =>
                s.ObjectId == monitoredObject.ToString().Replace("OBJECT_", ""));

            if (sub != null)
            {
                var presentValue = values.FirstOrDefault(v =>
                    v.property.propertyIdentifier == (uint)BacnetPropertyIds.PROP_PRESENT_VALUE);

                if (presentValue.value != null)
                {
                    var valText = ConvertToText(presentValue.value.ToList());
                    sub.Value = valText;
                    sub.Time = DateTime.Now.ToString("HH:mm:ss");
                    sub.Status = "COV";
                    if (double.TryParse(valText, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double dVal))
                        AddCovDataPoint(sub.ObjectId, dVal);
                }
            }
        });
    }

    // --- Chart ---

    private void AddCovDataPoint(string key, double value)
    {
        if (_plotPaused) return;

        if (!_covDataX.ContainsKey(key))
        {
            _covDataX[key] = new List<double>();
            _covDataY[key] = new List<double>();
        }

        _covDataX[key].Add(_covDataX[key].Count);
        _covDataY[key].Add(value);

        RefreshPlot();
    }

    private void RefreshPlot()
    {
        Dispatcher.UIThread.Post(() =>
        {
            covPlot.Plot.Clear();
            foreach (var key in _covDataX.Keys)
            {
                if (_covDataX[key].Count > 0)
                {
                    var sig = covPlot.Plot.Add.Scatter(
                        _covDataX[key].ToArray(),
                        _covDataY[key].ToArray());
                    sig.LegendText = key;
                }
            }
            covPlot.Plot.ShowLegend();
            covPlot.Refresh();
        });
    }

    private void OnPlayPause(object? sender, RoutedEventArgs e)
    {
        _plotPaused = !_plotPaused;
        btnPlayPause.Content = _plotPaused ? "Resume Plotter" : "Pause Plotter";
    }

    private void OnClearPlot(object? sender, RoutedEventArgs e)
    {
        _covDataX.Clear();
        _covDataY.Clear();
        covPlot.Plot.Clear();
        covPlot.Refresh();
    }

    private async void OnExportCsv(object? sender, RoutedEventArgs e)
    {
        var owner = OwnerWindow;
        if (owner == null) { AppendLog("Export not available on this platform"); return; }

        var sfd = new SaveFileDialog
        {
            Title = "Export COV data",
            Filters = new List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "CSV", Extensions = { "csv" } }
            },
            DefaultExtension = "csv"
        };

        var path = await sfd.ShowAsync(owner);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            using var writer = new System.IO.StreamWriter(path);
            writer.WriteLine("Object,Sample,Value");
            foreach (var key in _covDataX.Keys)
            {
                for (int i = 0; i < _covDataX[key].Count; i++)
                    writer.WriteLine($"{key},{_covDataX[key][i]},{_covDataY[key][i]}");
            }
            AppendLog($"Exported COV data to {path}");
        }
        catch (Exception ex)
        {
            AppendLog($"Export error: {ex.Message}");
        }
    }

    // --- Menu Handlers ---

    private void OnAlarmSummary(object? sender, RoutedEventArgs e)
    {
        if (_selectedDevice?.DeviceEntry == null) { AppendLog("Select a device first"); return; }
        var comm = FindCommForDevice(_selectedDevice);
        if (comm == null) return;

        var adr = _selectedDevice.DeviceEntry.Value.Key;
        var deviceId = _selectedDevice.DeviceEntry.Value.Value;

        var dlg = new AlarmSummaryDialog(comm, adr, deviceId);
        var owner = OwnerWindow;
        if (owner != null) dlg.Show(owner); else dlg.Show();
    }

    private void OnShowSchedule(object? sender, RoutedEventArgs e)
    {
        if (treeAddressSpace.SelectedItem is not DeviceTreeItem selected) { AppendLog("Select a schedule object first"); return; }
        if (selected.ObjectId == null || _selectedDevice == null) return;

        var comm = FindCommForDevice(_selectedDevice);
        if (comm == null) return;

        var adr = _selectedDevice.DeviceEntry!.Value.Key;
        var dlg = new ScheduleEditorDialog(comm, adr, selected.ObjectId.Value);
        var owner = OwnerWindow;
        if (owner != null) dlg.Show(owner); else dlg.Show();
    }

    private void OnTimeSync(object? sender, RoutedEventArgs e)
    {
        if (_selectedDevice?.DeviceEntry == null) { AppendLog("Select a device first"); return; }
        var comm = FindCommForDevice(_selectedDevice);
        if (comm == null) return;
        var adr = _selectedDevice.DeviceEntry.Value.Key;

        try
        {
            comm.SynchronizeTime(adr, DateTime.Now, false);
            AppendLog("Time sync sent (local time)");
        }
        catch (Exception ex)
        {
            AppendLog($"Time sync error: {ex.Message}");
        }
    }

    private void OnShowTrendLog(object? sender, RoutedEventArgs e)
    {
        if (treeAddressSpace.SelectedItem is not DeviceTreeItem selected) { AppendLog("Select a trend log object first"); return; }
        if (selected.ObjectId == null || _selectedDevice == null) return;

        var objId = selected.ObjectId.Value;
        if (objId.type != BacnetObjectTypes.OBJECT_TRENDLOG &&
            objId.type != BacnetObjectTypes.OBJECT_TREND_LOG_MULTIPLE)
        {
            AppendLog("Selected object is not a Trend Log");
            return;
        }

        var comm = FindCommForDevice(_selectedDevice);
        if (comm == null) return;

        var adr = _selectedDevice.DeviceEntry!.Value.Key;
        var dlg = new TrendLogDialog(comm, adr, objId);
        var owner = OwnerWindow;
        if (owner != null) dlg.Show(owner); else dlg.Show();
    }

    private void OnShowNotification(object? sender, RoutedEventArgs e)
    {
        if (treeAddressSpace.SelectedItem is not DeviceTreeItem selected) { AppendLog("Select a notification class object first"); return; }
        if (selected.ObjectId == null || _selectedDevice == null) return;

        var comm = FindCommForDevice(_selectedDevice);
        if (comm == null) return;

        var adr = _selectedDevice.DeviceEntry!.Value.Key;
        var dlg = new NotificationDialog(comm, adr, selected.ObjectId.Value);
        var owner = OwnerWindow;
        if (owner != null) dlg.Show(owner); else dlg.Show();
    }

    private void OnShowCalendar(object? sender, RoutedEventArgs e)
    {
        if (treeAddressSpace.SelectedItem is not DeviceTreeItem selected) { AppendLog("Select a calendar object first"); return; }
        if (selected.ObjectId == null || _selectedDevice == null) return;

        var comm = FindCommForDevice(_selectedDevice);
        if (comm == null) return;

        var adr = _selectedDevice.DeviceEntry!.Value.Key;
        var dlg = new CalendarDialog(comm, adr, selected.ObjectId.Value);
        var owner = OwnerWindow;
        if (owner != null) dlg.Show(owner); else dlg.Show();
    }

    private async void OnCreateObject(object? sender, RoutedEventArgs e)
    {
        if (_selectedDevice?.DeviceEntry == null) { AppendLog("Select a device first"); return; }
        var comm = FindCommForDevice(_selectedDevice);
        if (comm == null) return;
        var adr = _selectedDevice.DeviceEntry.Value.Key;

        var dlg = new CreateObjectDialog();
        var owner = OwnerWindow;
        if (owner != null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();

        if (!dlg.Confirmed) return;

        try
        {
            var objId = new BacnetObjectId(dlg.SelectedObjectType, dlg.InstanceNumber);
            comm.CreateObjectRequest(adr, objId);
            AppendLog($"Created {objId}");
        }
        catch (Exception ex)
        {
            AppendLog($"Create object error: {ex.Message}");
        }
    }

    private async void OnCommControl(object? sender, RoutedEventArgs e)
    {
        if (_selectedDevice?.DeviceEntry == null) { AppendLog("Select a device first"); return; }
        var comm = FindCommForDevice(_selectedDevice);
        if (comm == null) return;
        var adr = _selectedDevice.DeviceEntry.Value.Key;

        var dlg = new DeviceCommControlDialog();
        var owner = OwnerWindow;
        if (owner != null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();

        if (!dlg.Confirmed) return;

        try
        {
            if (dlg.IsReinitialize)
            {
                comm.ReinitializeRequest(adr, dlg.ReinitializeState, dlg.Password);
                AppendLog($"Reinitialize sent: {dlg.ReinitializeState}");
            }
            else
            {
                var enable = !dlg.DisableCommunication;
                comm.DeviceCommunicationControlRequest(adr, dlg.Duration, enable ? 0u : 1u, dlg.Password);
                AppendLog($"Communication control: {(enable ? "Enable" : "Disable")}, duration={dlg.Duration}");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
        }
    }

    private void OnDownloadFile(object? sender, RoutedEventArgs e)
    {
        if (treeAddressSpace.SelectedItem is not DeviceTreeItem selected) { AppendLog("Select a file object first"); return; }
        if (selected.ObjectId == null || _selectedDevice == null) return;

        if (selected.ObjectId.Value.type != BacnetObjectTypes.OBJECT_FILE)
        {
            AppendLog("Selected object is not a File object");
            return;
        }

        var comm = FindCommForDevice(_selectedDevice);
        if (comm == null) return;

        var adr = _selectedDevice.DeviceEntry!.Value.Key;
        var dlg = new FileTransferDialog(comm, adr, selected.ObjectId.Value, true);
        var owner = OwnerWindow;
        if (owner != null) dlg.Show(owner); else dlg.Show();
    }

    private void OnUploadFile(object? sender, RoutedEventArgs e)
    {
        if (treeAddressSpace.SelectedItem is not DeviceTreeItem selected) { AppendLog("Select a file object first"); return; }
        if (selected.ObjectId == null || _selectedDevice == null) return;

        if (selected.ObjectId.Value.type != BacnetObjectTypes.OBJECT_FILE)
        {
            AppendLog("Selected object is not a File object");
            return;
        }

        var comm = FindCommForDevice(_selectedDevice);
        if (comm == null) return;

        var adr = _selectedDevice.DeviceEntry!.Value.Key;
        var dlg = new FileTransferDialog(comm, adr, selected.ObjectId.Value, false);
        var owner = OwnerWindow;
        if (owner != null) dlg.Show(owner); else dlg.Show();
    }

    private void OnForeignRegistry(object? sender, RoutedEventArgs e)
    {
        if (_devices.Count == 0) { AppendLog("Add a BACnet/IP transport first"); return; }

        var ipClient = _devices.Keys.FirstOrDefault(c =>
            c.Transport is BacnetIpUdpProtocolTransport || c.Transport is BacnetIpV6UdpProtocolTransport);
        if (ipClient == null) { AppendLog("No BACnet/IP transport available"); return; }

        var dlg = new ForeignRegistryDialog(ipClient);
        var owner = OwnerWindow;
        if (owner != null) dlg.Show(owner); else dlg.Show();
    }

    // --- WriteProperty ---

    private void OnPropertyCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        if (e.Row.DataContext is not PropertyItem propItem) return;
        if (propItem.PropertyId == 0) return;

        if (_selectedDevice?.DeviceEntry == null) return;
        var selectedObj = treeAddressSpace.SelectedItem as DeviceTreeItem;
        if (selectedObj?.ObjectId == null) return;

        var comm = FindCommForDevice(_selectedDevice);
        if (comm == null) return;

        var adr = _selectedDevice.DeviceEntry.Value.Key;
        var objId = selectedObj.ObjectId.Value;
        var propId = (BacnetPropertyIds)propItem.PropertyId;

        var editElement = e.EditingElement as TextBox;
        var newValue = editElement?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(newValue)) return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                IList<BacnetValue> readValues;
                if (!comm.ReadPropertyRequest(adr, objId, propId, out readValues) || readValues.Count == 0)
                {
                    AppendLog($"Can't determine type for {propId} - read failed");
                    return;
                }

                var tag = readValues[0].Tag;
                var bacnetValues = new List<BacnetValue>();

                if (tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL)
                {
                    if (float.TryParse(newValue, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out float fVal))
                        bacnetValues.Add(new BacnetValue(tag, fVal));
                    else { AppendLog($"Invalid float value: {newValue}"); return; }
                }
                else if (tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_DOUBLE)
                {
                    if (double.TryParse(newValue, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double dVal))
                        bacnetValues.Add(new BacnetValue(tag, dVal));
                    else { AppendLog($"Invalid double value: {newValue}"); return; }
                }
                else if (tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_UNSIGNED_INT)
                {
                    if (uint.TryParse(newValue, out uint uVal))
                        bacnetValues.Add(new BacnetValue(tag, uVal));
                    else { AppendLog($"Invalid unsigned int value: {newValue}"); return; }
                }
                else if (tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_SIGNED_INT)
                {
                    if (int.TryParse(newValue, out int iVal))
                        bacnetValues.Add(new BacnetValue(tag, iVal));
                    else { AppendLog($"Invalid signed int value: {newValue}"); return; }
                }
                else if (tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_BOOLEAN)
                {
                    var lower = newValue.ToLower();
                    if (lower == "true" || lower == "1" || lower == "active")
                        bacnetValues.Add(new BacnetValue(tag, true));
                    else if (lower == "false" || lower == "0" || lower == "inactive")
                        bacnetValues.Add(new BacnetValue(tag, false));
                    else { AppendLog($"Invalid boolean: {newValue}"); return; }
                }
                else if (tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED)
                {
                    if (uint.TryParse(newValue, out uint eVal))
                        bacnetValues.Add(new BacnetValue(tag, eVal));
                    else { AppendLog($"Invalid enumeration: {newValue}"); return; }
                }
                else if (tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_CHARACTER_STRING)
                {
                    bacnetValues.Add(new BacnetValue(tag, newValue));
                }
                else
                {
                    bacnetValues.Add(new BacnetValue(tag, newValue));
                }

                comm.WritePriority = SettingsDialog.WritePriority;
                if (comm.WritePropertyRequest(adr, objId, propId, bacnetValues))
                {
                    AppendLog($"Written {propId} = {newValue}" +
                        (SettingsDialog.WritePriority > 0 ? $" (priority {SettingsDialog.WritePriority})" : ""));
                    Dispatcher.UIThread.Post(() => propItem.Value = newValue);
                }
                else
                {
                    AppendLog($"WriteProperty failed for {propId}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Write error: {ex.Message}");
            }
        });
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog();
        var owner = OwnerWindow;
        if (owner != null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();
    }

    // --- Helpers ---

    private BacnetClient? FindCommForDevice(DeviceTreeItem deviceNode)
    {
        if (deviceNode.Tag is BacnetClient comm)
            return comm;

        foreach (var transportNode in RootNode.Children)
        {
            if (transportNode.Children.Contains(deviceNode))
                return transportNode.Tag as BacnetClient;
        }
        return null;
    }

    private void ResolveObjectNames(BacnetClient comm, BacnetAddress adr,
        List<BacnetObjectId> objects, ObservableCollection<DeviceTreeItem> items, int requestId)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            for (int i = 0; i < objects.Count && i < items.Count; i++)
            {
                if (requestId != _asyncRequestId) return;
                try
                {
                    IList<BacnetValue> nameValues;
                    if (comm.ReadPropertyRequest(adr, objects[i],
                        BacnetPropertyIds.PROP_OBJECT_NAME, out nameValues) && nameValues.Count > 0)
                    {
                        var objName = nameValues[0].Value?.ToString();
                        if (!string.IsNullOrEmpty(objName))
                        {
                            var idx = i;
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (idx < items.Count)
                                    items[idx].Header = $"{objName} [{items[idx].Header}]";
                            });
                        }
                    }
                }
                catch { }
            }
        });
    }

    private static List<BacnetObjectId> SortBacnetObjects(IList<BacnetValue> rawList)
    {
        var sorted = new List<BacnetObjectId>();
        foreach (var value in rawList)
        {
            if (value.Value is BacnetObjectId objId)
                sorted.Add(objId);
        }
        sorted.Sort();
        return sorted;
    }

    private static string ConvertToText(IList<BacnetValue>? values)
    {
        if (values == null) return "[null]";
        if (values.Count == 0) return "";
        if (values.Count == 1) return values[0].Value?.ToString() ?? "[null]";

        return "{" + string.Join(",", values.Select(v => v.Value?.ToString() ?? "")) + "}";
    }

    private void OnAbout(object? sender, RoutedEventArgs e)
    {
        AppendLog("YABE - Yet Another BACnet Explorer (Avalonia port)");
        AppendLog("Original by Morten Kvistgaard & Frederic Chaxel");
        AppendLog("MIT License - http://sourceforge.net/projects/yetanotherbacnetexplorer/");
    }

    // --- Data Models ---

    public class BacnetDeviceLine
    {
        public BacnetClient Line;
        public List<KeyValuePair<BacnetAddress, uint>> Devices = new();
        public BacnetDeviceLine(BacnetClient comm) { Line = comm; }
    }
}
