using System;
using System.Collections.Generic;
using System.IO;
using System.IO.BACnet;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Yabe;

public partial class FileTransferDialog : Window
{
    private readonly BacnetClient _client;
    private readonly BacnetAddress _adr;
    private readonly BacnetObjectId _fileObject;
    private readonly bool _isDownload;

    public FileTransferDialog(BacnetClient client, BacnetAddress adr, BacnetObjectId fileObject, bool download)
    {
        InitializeComponent();
        _client = client;
        _adr = adr;
        _fileObject = fileObject;
        _isDownload = download;

        lblTitle.Text = download ? "Download File from Device" : "Upload File to Device";
        txtFileObject.Text = fileObject.ToString();
        btnStart.Content = download ? "Download" : "Upload";

        btnStart.Click += OnStart;
        btnCancel.Click += (_, _) => Close();
        btnBrowse.Click += OnBrowse;
    }

    private async void OnBrowse(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isDownload)
        {
            var sfd = new SaveFileDialog { Title = "Save file as" };
            var path = await sfd.ShowAsync(this);
            if (!string.IsNullOrEmpty(path))
                txtLocalPath.Text = path;
        }
        else
        {
            var ofd = new OpenFileDialog { Title = "Select file to upload", AllowMultiple = false };
            var files = await ofd.ShowAsync(this);
            if (files != null && files.Length > 0)
                txtLocalPath.Text = files[0];
        }
    }

    private void OnStart(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var localPath = txtLocalPath.Text;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            lblStatus.Text = "Select a local path first";
            return;
        }

        btnStart.IsEnabled = false;
        lblStatus.Text = "Transferring...";

        if (_isDownload)
            ThreadPool.QueueUserWorkItem(_ => DoDownload(localPath));
        else
            ThreadPool.QueueUserWorkItem(_ => DoUpload(localPath));
    }

    private void DoDownload(string localPath)
    {
        try
        {
            using var fs = new FileStream(localPath, FileMode.Create);
            int position = 0;
            uint count = 1476;
            bool endOfFile = false;

            // Read file size for progress
            IList<BacnetValue> sizeVal;
            long fileSize = 0;
            if (_client.ReadPropertyRequest(_adr, _fileObject,
                BacnetPropertyIds.PROP_FILE_SIZE, out sizeVal))
            {
                fileSize = Convert.ToInt64(sizeVal[0].Value);
            }

            while (!endOfFile)
            {
                byte[] buffer;
                int bufferOffset;
                if (!_client.ReadFileRequest(_adr, _fileObject, ref position,
                    ref count, out endOfFile, out buffer, out bufferOffset))
                {
                    UpdateStatus("Read error");
                    return;
                }

                fs.Write(buffer, bufferOffset, (int)count);
                position += (int)count;

                if (fileSize > 0)
                {
                    var pct = Math.Min(100, (int)(position * 100 / fileSize));
                    Dispatcher.UIThread.Post(() => pbProgress.Value = pct);
                }
            }

            UpdateStatus($"Downloaded {position} bytes to {Path.GetFileName(localPath)}");
            Dispatcher.UIThread.Post(() =>
            {
                pbProgress.Value = 100;
                btnStart.IsEnabled = true;
            });
        }
        catch (Exception ex)
        {
            UpdateStatus($"Error: {ex.Message}");
            Dispatcher.UIThread.Post(() => btnStart.IsEnabled = true);
        }
    }

    private void DoUpload(string localPath)
    {
        try
        {
            var fileData = File.ReadAllBytes(localPath);
            int position = 0;
            int blockSize = 1476;
            long total = fileData.Length;

            while (position < total)
            {
                int count = Math.Min(blockSize, (int)(total - position));
                var block = new byte[count];
                Array.Copy(fileData, position, block, 0, count);

                if (!_client.WriteFileRequest(_adr, _fileObject, ref position, count, block))
                {
                    UpdateStatus("Write error");
                    return;
                }

                position += count;
                var pct = Math.Min(100, (int)(position * 100 / total));
                Dispatcher.UIThread.Post(() => pbProgress.Value = pct);
            }

            UpdateStatus($"Uploaded {total} bytes from {Path.GetFileName(localPath)}");
            Dispatcher.UIThread.Post(() =>
            {
                pbProgress.Value = 100;
                btnStart.IsEnabled = true;
            });
        }
        catch (Exception ex)
        {
            UpdateStatus($"Error: {ex.Message}");
            Dispatcher.UIThread.Post(() => btnStart.IsEnabled = true);
        }
    }

    private void UpdateStatus(string text)
    {
        Dispatcher.UIThread.Post(() => lblStatus.Text = text);
    }
}
