using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.BACnet;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Yabe;

public partial class ScheduleEditorDialog : Window
{
    private readonly BacnetClient _comm;
    private readonly BacnetAddress _adr;
    private readonly BacnetObjectId _objId;

    public ScheduleEditorDialog(BacnetClient comm, BacnetAddress adr, BacnetObjectId objId)
    {
        InitializeComponent();
        _comm = comm;
        _adr = adr;
        _objId = objId;

        lblObjectInfo.Text = $"Schedule: {objId}";
        btnRefresh.Click += (_, _) => LoadSchedule();
        Loaded += (_, _) => LoadSchedule();
    }

    private void LoadSchedule()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (_comm.ReadPropertyRequest(_adr, _objId,
                    BacnetPropertyIds.PROP_PRESENT_VALUE, out IList<BacnetValue> pvValues))
                {
                    Dispatcher.UIThread.Post(() =>
                        lblPresent.Text = pvValues.Count > 0 ? pvValues[0].Value?.ToString() ?? "--" : "--");
                }

                if (_comm.ReadPropertyRequest(_adr, _objId,
                    BacnetPropertyIds.PROP_SCHEDULE_DEFAULT, out IList<BacnetValue> defValues))
                {
                    Dispatcher.UIThread.Post(() =>
                        lblDefault.Text = defValues.Count > 0 ? defValues[0].Value?.ToString() ?? "--" : "--");
                }

                if (_comm.ReadPropertyRequest(_adr, _objId,
                    BacnetPropertyIds.PROP_EFFECTIVE_PERIOD, out IList<BacnetValue> periodValues))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (periodValues.Count >= 2)
                        {
                            txtStartDate.Text = periodValues[0].Value?.ToString() ?? "";
                            txtEndDate.Text = periodValues[1].Value?.ToString() ?? "";
                            lblPeriod.Text = $"{periodValues[0].Value} to {periodValues[1].Value}";
                        }
                    });
                }

                var treeItems = new ObservableCollection<TreeViewItem>();
                string[] days = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

                if (_comm.ReadPropertyRequest(_adr, _objId,
                    BacnetPropertyIds.PROP_WEEKLY_SCHEDULE, out IList<BacnetValue> weeklyValues))
                {
                    for (int i = 0; i < weeklyValues.Count && i < 7; i++)
                    {
                        var dayNode = new TreeViewItem { Header = days[i] };
                        var schedule = weeklyValues[i].Value?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(schedule))
                            dayNode.Items.Add(new TreeViewItem { Header = schedule });
                        treeItems.Add(dayNode);
                    }
                }

                if (_comm.ReadPropertyRequest(_adr, _objId,
                    BacnetPropertyIds.PROP_EXCEPTION_SCHEDULE, out IList<BacnetValue> exValues))
                {
                    if (exValues.Count > 0)
                    {
                        var excNode = new TreeViewItem { Header = "Exception Schedules" };
                        for (int i = 0; i < exValues.Count; i++)
                            excNode.Items.Add(new TreeViewItem { Header = exValues[i].Value?.ToString() ?? $"Exception {i + 1}" });
                        treeItems.Add(excNode);
                    }
                }

                if (_comm.ReadPropertyRequest(_adr, _objId,
                    BacnetPropertyIds.PROP_LIST_OF_OBJECT_PROPERTY_REFERENCES, out IList<BacnetValue> refValues))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        lstReferences.Items.Clear();
                        foreach (var v in refValues)
                            lstReferences.Items.Add(v.Value?.ToString() ?? "");
                    });
                }

                Dispatcher.UIThread.Post(() =>
                {
                    treeSchedule.Items.Clear();
                    foreach (var item in treeItems)
                        treeSchedule.Items.Add(item);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                    lblDefault.Text = $"Error: {ex.Message}");
            }
        });
    }
}
