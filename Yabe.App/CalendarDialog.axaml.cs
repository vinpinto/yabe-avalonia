using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Yabe;

public partial class CalendarDialog : Window
{
    private readonly BacnetClient _comm;
    private readonly BacnetAddress _adr;
    private readonly BacnetObjectId _objId;
    private BACnetCalendarEntry _calendarEntries;

    public CalendarDialog(BacnetClient comm, BacnetAddress adr, BacnetObjectId objId)
    {
        InitializeComponent();
        _comm = comm;
        _adr = adr;
        _objId = objId;
        _calendarEntries.Entries = new List<object>();

        btnRefresh.Click += (_, _) => LoadCalendar();
        btnWrite.Click += OnWrite;
        btnAddDate.Click += OnAddDate;
        btnAddRange.Click += OnAddRange;
        btnRemove.Click += OnRemoveEntry;

        Loaded += (_, _) => LoadCalendar();
    }

    private void LoadCalendar()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                _comm.ReadPropertyRequest(_adr, _objId,
                    BacnetPropertyIds.PROP_DATE_LIST, out IList<BacnetValue> values);

                Dispatcher.UIThread.Post(() =>
                {
                    lstEntries.Items.Clear();

                    if (values != null && values.Count == 1 && values[0].Value is BACnetCalendarEntry cal)
                    {
                        _calendarEntries = cal;
                        if (cal.Entries != null)
                        {
                            foreach (var entry in cal.Entries)
                                lstEntries.Items.Add(entry.ToString());
                        }
                        lblStatus.Text = $"{lstEntries.Items.Count} entries loaded";
                    }
                    else
                    {
                        _calendarEntries = new BACnetCalendarEntry { Entries = new List<object>() };
                        lblStatus.Text = "No entries";
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => lblStatus.Text = $"Error: {ex.Message}");
            }
        });
    }

    private static BacnetDate ToBacnetDate(DateTime dt)
    {
        return new BacnetDate((byte)(dt.Year - 1900), (byte)dt.Month, (byte)dt.Day);
    }

    private void OnAddDate(object? sender, RoutedEventArgs e)
    {
        var date = calPicker.SelectedDate;
        if (date == null) return;

        var dt = date.Value.Date;
        var bd = ToBacnetDate(dt);
        _calendarEntries.Entries ??= new List<object>();
        _calendarEntries.Entries.Add(bd);
        lstEntries.Items.Add(dt.ToString("yyyy-MM-dd"));
        lblStatus.Text = "Date added (not yet written)";
    }

    private void OnAddRange(object? sender, RoutedEventArgs e)
    {
        var date = calPicker.SelectedDate;
        if (date == null) return;

        var start = date.Value.Date;
        var end = start.AddDays(7);
        var range = new BacnetDateRange(ToBacnetDate(start), ToBacnetDate(end));
        _calendarEntries.Entries ??= new List<object>();
        _calendarEntries.Entries.Add(range);
        lstEntries.Items.Add($"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
        lblStatus.Text = "Range added (not yet written)";
    }

    private void OnRemoveEntry(object? sender, RoutedEventArgs e)
    {
        var idx = lstEntries.SelectedIndex;
        if (idx < 0 || _calendarEntries.Entries == null) return;

        _calendarEntries.Entries.RemoveAt(idx);
        lstEntries.Items.RemoveAt(idx);
        lblStatus.Text = "Entry removed (not yet written)";
    }

    private void OnWrite(object? sender, RoutedEventArgs e)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var v = new List<BacnetValue> { new BacnetValue(_calendarEntries) };
                _comm.WritePropertyRequest(_adr, _objId, BacnetPropertyIds.PROP_DATE_LIST, v);

                Dispatcher.UIThread.Post(() =>
                {
                    lblStatus.Text = "Calendar written successfully";
                    LoadCalendar();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => lblStatus.Text = $"Write error: {ex.Message}");
            }
        });
    }
}
