using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.BACnet;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Yabe;

public partial class AlarmSummaryDialog : Window
{
    private readonly BacnetClient _comm;
    private readonly BacnetAddress _adr;
    private readonly uint _deviceId;
    private IList<BacnetGetEventInformationData> _alarms = new List<BacnetGetEventInformationData>();

    public AlarmSummaryDialog(BacnetClient comm, BacnetAddress adr, uint deviceId)
    {
        InitializeComponent();
        _comm = comm;
        _adr = adr;
        _deviceId = deviceId;

        Title = $"Active Alarms on Device {deviceId}";

        btnRefresh.Click += (_, _) => LoadAlarms();
        btnAck.Click += OnAcknowledge;

        Loaded += (_, _) => LoadAlarms();
    }

    private void LoadAlarms()
    {
        lblInfo.Text = "Loading alarms...";
        btnAck.IsEnabled = false;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            _alarms = new List<BacnetGetEventInformationData>();
            bool ok = _comm.GetAlarmSummaryOrEventRequest(_adr, true, ref _alarms);

            if (!ok)
                ok = _comm.GetAlarmSummaryOrEventRequest(_adr, false, ref _alarms);

            Dispatcher.UIThread.Post(() =>
            {
                if (ok && _alarms.Count > 0)
                {
                    lblInfo.Text = $"{_alarms.Count} alarm(s) found";
                    btnAck.IsEnabled = true;
                    FillTree();
                }
                else if (ok)
                {
                    lblInfo.Text = "No active alarms";
                    treeAlarms.ItemsSource = null;
                }
                else
                {
                    lblInfo.Text = "Service not available on this device";
                }
            });
        });
    }

    private void FillTree()
    {
        var items = new ObservableCollection<DeviceTreeItem>();
        foreach (var alarm in _alarms)
        {
            var state = alarm.eventState.ToString().Replace("EVENT_STATE_", "");
            var objName = alarm.objectIdentifier.ToString().Replace("OBJECT_", "");
            var node = new DeviceTreeItem($"{objName} - {state}");

            node.Children.Add(new DeviceTreeItem($"Notify type: {alarm.notifyType}"));
            node.Children.Add(new DeviceTreeItem($"Event enable: {string.Join(", ", alarm.eventEnable)}"));
            node.Children.Add(new DeviceTreeItem($"Event priorities: {string.Join(", ", alarm.eventPriorities)}"));

            if (alarm.eventTimeStamps != null)
            {
                foreach (var ts in alarm.eventTimeStamps)
                {
                    node.Children.Add(new DeviceTreeItem($"Timestamp: {ts}"));
                }
            }

            items.Add(node);
        }
        treeAlarms.ItemsSource = items;
    }

    private void OnAcknowledge(object? sender, RoutedEventArgs e)
    {
        if (treeAlarms.SelectedItem is not DeviceTreeItem selected) return;

        var index = 0;
        if (treeAlarms.ItemsSource is ObservableCollection<DeviceTreeItem> items)
            index = items.IndexOf(selected);

        if (index < 0 || index >= _alarms.Count) return;

        var alarm = _alarms[index];
        var ackText = txtAck.Text ?? "Acknowledged";

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var ts = new BacnetGenericTime(DateTime.Now, BacnetTimestampTags.TIME_STAMP_DATETIME);
                _comm.AlarmAcknowledgement(
                    _adr,
                    alarm.objectIdentifier,
                    alarm.eventState, ackText,
                    ts, ts);

                Dispatcher.UIThread.Post(() =>
                {
                    lblInfo.Text = $"Acknowledged: {alarm.objectIdentifier}";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    lblInfo.Text = $"Ack failed: {ex.Message}";
                });
            }
        });
    }
}
