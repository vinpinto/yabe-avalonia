using System;
using System.IO.BACnet;
using Avalonia.Controls;

namespace Yabe;

public partial class DeviceCommControlDialog : Window
{
    public bool IsReinitialize => rbReinitialize.IsChecked == true;
    public bool DisableCommunication => chkDisable.IsChecked == true;
    public uint Duration => (uint)(nudDuration.Value ?? 0);
    public string Password => txtPassword.Text ?? "";
    public bool Confirmed { get; private set; }

    public BacnetReinitializedStates ReinitializeState
    {
        get
        {
            var idx = cmbState.SelectedIndex;
            return idx >= 0 ? (BacnetReinitializedStates)idx : BacnetReinitializedStates.BACNET_REINIT_COLDSTART;
        }
    }

    public DeviceCommControlDialog()
    {
        InitializeComponent();

        var names = Enum.GetNames(typeof(BacnetReinitializedStates));
        for (int i = 0; i < names.Length - 1; i++)
            cmbState.Items.Add(names[i].Replace("BACNET_REINIT_", ""));
        cmbState.SelectedIndex = 0;

        btnOk.Click += (_, _) => { Confirmed = true; Close(); };
        btnCancel.Click += (_, _) => Close();
    }
}
