using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Yabe;

public partial class NotificationDialog : Window
{
    private readonly BacnetClient _comm;
    private readonly BacnetAddress _adr;
    private readonly BacnetObjectId _objId;
    private readonly ObservableCollection<RecipientRow> _recipients = new();

    public NotificationDialog(BacnetClient comm, BacnetAddress adr, BacnetObjectId objId)
    {
        InitializeComponent();
        _comm = comm;
        _adr = adr;
        _objId = objId;

        lblTitle.Text = $"Recipient List: {objId.ToString().Replace("OBJECT_", "")}";
        dgRecipients.ItemsSource = _recipients;
        btnRefresh.Click += (_, _) => LoadProperties();
        Loaded += (_, _) => LoadProperties();
    }

    private void LoadProperties()
    {
        _recipients.Clear();

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var props = new List<BacnetPropertyReference>
                {
                    new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_RECIPIENT_LIST, ASN1.BACNET_ARRAY_ALL),
                    new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_PRIORITY, ASN1.BACNET_ARRAY_ALL)
                };

                if (!_comm.ReadPropertyMultipleRequest(_adr, _objId, props, out IList<BacnetReadAccessResult> results))
                    return;

                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var prop in results[0].values)
                    {
                        if (prop.property.propertyIdentifier == (uint)BacnetPropertyIds.PROP_PRIORITY)
                        {
                            if (prop.value.Count >= 3)
                            {
                                lblPriorityOff.Text = prop.value[0].Value?.ToString() ?? "--";
                                lblPriorityFault.Text = prop.value[1].Value?.ToString() ?? "--";
                                lblPriorityNormal.Text = prop.value[2].Value?.ToString() ?? "--";
                            }
                        }
                        else
                        {
                            for (int i = 0; i < prop.value.Count / 7; i++)
                            {
                                var recipientStr = prop.value[i * 7 + 2].Value?.ToString() ?? "Unknown";
                                var processId = prop.value[i * 7 + 1].Value?.ToString() ?? "";
                                var transitions = prop.value[i * 7 + 5].Value?.ToString() ?? "";

                                _recipients.Add(new RecipientRow
                                {
                                    Index = (i + 1).ToString(),
                                    Recipient = recipientStr,
                                    ProcessId = processId,
                                    Transitions = transitions
                                });
                            }
                        }
                    }
                });
            }
            catch { }
        });
    }
}

public class RecipientRow
{
    public string Index { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string ProcessId { get; set; } = "";
    public string Transitions { get; set; } = "";
}
