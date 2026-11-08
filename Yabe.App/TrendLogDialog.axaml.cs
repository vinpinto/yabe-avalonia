using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Yabe;

public partial class TrendLogDialog : Window
{
    private readonly BacnetClient _comm;
    private readonly BacnetAddress _adr;
    private readonly BacnetObjectId _objId;
    private readonly ObservableCollection<TrendLogRecord> _records = new();
    private readonly List<double> _dataX = new();
    private readonly List<double> _dataY = new();
    private bool _stopDownload;
    private int _logSize;
    private int _curvesNumber = 1;

    public TrendLogDialog(BacnetClient comm, BacnetAddress adr, BacnetObjectId objId)
    {
        InitializeComponent();
        _comm = comm;
        _adr = adr;
        _objId = objId;

        dgRecords.ItemsSource = _records;
        btnStop.Click += (_, _) => _stopDownload = true;
        btnExportCsv.Click += OnExportCsv;

        Loaded += (_, _) => StartDownload();
    }

    private void StartDownload()
    {
        _logSize = ReadRecordCount();
        if (_logSize <= 0)
        {
            lblProgress.Text = "The trendlog is empty, nothing to display";
            progressBar.IsVisible = false;
            return;
        }

        var name = ReadObjectName();
        trendPlot.Plot.Title(name);
        trendPlot.Plot.YLabel("Values");
        trendPlot.Plot.XLabel("Sample");

        if (_objId.type == BacnetObjectTypes.OBJECT_TREND_LOG_MULTIPLE)
            _curvesNumber = ReadNumberOfCurves();

        progressBar.Maximum = _logSize;
        lblProgress.Text = $"Downloading {_logSize} records (0%)...";

        var th = new Thread(DownloadTrendLog) { IsBackground = true };
        th.Start();
    }

    private int ReadRecordCount()
    {
        try
        {
            if (_comm.ReadPropertyRequest(_adr, _objId,
                BacnetPropertyIds.PROP_RECORD_COUNT, out IList<BacnetValue> value))
                return (int)Convert.ChangeType(value[0].Value, typeof(int));
        }
        catch { }
        return -1;
    }

    private string ReadObjectName()
    {
        try
        {
            if (_comm.ReadPropertyRequest(_adr, _objId,
                BacnetPropertyIds.PROP_OBJECT_NAME, out IList<BacnetValue> value))
                return value[0].Value?.ToString() ?? "TrendLog";
        }
        catch { }
        return "TrendLog";
    }

    private int ReadNumberOfCurves()
    {
        try
        {
            if (_comm.ReadPropertyRequest(_adr, _objId,
                BacnetPropertyIds.PROP_LOG_DEVICE_OBJECT_PROPERTY, out IList<BacnetValue> value))
                return value?.Count ?? 1;
        }
        catch { }
        return 1;
    }

    private void DownloadTrendLog()
    {
        int nbRecordsByStep = Math.Max(10, 60 - 5 * _curvesNumber);
        int idx = 1;

        try
        {
            while (idx <= _logSize && !_stopDownload)
            {
                uint itemCount = (uint)Math.Min(nbRecordsByStep, _logSize - idx + 1);

                if (!_comm.ReadRangeRequest(_adr, _objId, (uint)idx, ref itemCount, out byte[] trendBuffer) || itemCount <= 0)
                {
                    Dispatcher.UIThread.Post(() => lblProgress.Text = "Error loading trend data");
                    return;
                }

                int len = 0;
                for (int itm = 0; itm < itemCount; itm++)
                {
                    int l = Services.DecodeLogRecord(trendBuffer, len, trendBuffer.Length - len, _curvesNumber, out BacnetLogRecord[] records);
                    if (l < 0)
                    {
                        Dispatcher.UIThread.Post(() => lblProgress.Text = "Error decoding log data");
                        return;
                    }
                    len += l;

                    for (int i = 0; i < records.Length; i++)
                    {
                        var rec = records[i];
                        double yVal = double.NaN;
                        if (rec.type == BacnetTrendLogValueType.TL_TYPE_UNSIGN ||
                            rec.type == BacnetTrendLogValueType.TL_TYPE_SIGN ||
                            rec.type == BacnetTrendLogValueType.TL_TYPE_REAL ||
                            rec.type == BacnetTrendLogValueType.TL_TYPE_ENUM)
                        {
                            try { yVal = (double)Convert.ChangeType(rec.Value, typeof(double)); } catch { }
                        }

                        _dataX.Add(idx);
                        _dataY.Add(yVal);

                        var seqNo = idx;
                        var typeName = rec.type.ToString().Replace("TL_TYPE_", "");
                        var valStr = rec.Value?.ToString() ?? "NULL";
                        var ts = rec.timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                        var status = rec.statusFlags.ConvertToInt().ToString();

                        Dispatcher.UIThread.Post(() =>
                        {
                            _records.Add(new TrendLogRecord
                            {
                                Sequence = seqNo,
                                Timestamp = ts,
                                Type = typeName,
                                Value = valStr,
                                StatusFlags = status
                            });
                        });

                        idx++;
                    }
                }

                int percent = idx * 100 / _logSize;
                Dispatcher.UIThread.Post(() =>
                {
                    progressBar.Value = idx;
                    lblProgress.Text = $"Downloading {_logSize} records ({percent}%)...";
                });
            }

            Dispatcher.UIThread.Post(() =>
            {
                progressBar.IsVisible = false;
                lblProgress.Text = _stopDownload ? "Download stopped" : "Download complete";
                RefreshChart();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => lblProgress.Text = $"Error: {ex.Message}");
        }
    }

    private void RefreshChart()
    {
        trendPlot.Plot.Clear();
        if (_dataX.Count > 0)
        {
            var validX = new List<double>();
            var validY = new List<double>();
            for (int i = 0; i < _dataX.Count; i++)
            {
                if (!double.IsNaN(_dataY[i]))
                {
                    validX.Add(_dataX[i]);
                    validY.Add(_dataY[i]);
                }
            }
            if (validX.Count > 0)
            {
                trendPlot.Plot.Add.Scatter(validX.ToArray(), validY.ToArray());
            }
        }
        trendPlot.Refresh();
    }

    private async void OnExportCsv(object? sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Title = "Export trend log",
            Filters = new List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "CSV", Extensions = { "csv" } }
            },
            DefaultExtension = "csv"
        };
        var path = await sfd.ShowAsync(this);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            using var writer = new StreamWriter(path);
            writer.WriteLine("Sequence;Timestamp;Type;Value;Status");
            foreach (var rec in _records)
                writer.WriteLine($"{rec.Sequence};{rec.Timestamp};{rec.Type};{rec.Value};{rec.StatusFlags}");
        }
        catch { }
    }
}

public class TrendLogRecord
{
    public int Sequence { get; set; }
    public string Timestamp { get; set; } = "";
    public string Type { get; set; } = "";
    public string Value { get; set; } = "";
    public string StatusFlags { get; set; } = "";
}
