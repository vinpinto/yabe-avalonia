using System;
using System.IO.BACnet;
using Avalonia.Controls;

namespace Yabe;

public partial class CreateObjectDialog : Window
{
    public BacnetObjectTypes SelectedObjectType { get; private set; }
    public uint InstanceNumber { get; private set; }
    public bool Confirmed { get; private set; }

    public CreateObjectDialog()
    {
        InitializeComponent();

        for (int i = 0; i <= (int)BacnetObjectTypes.OBJECT_BINARY_LIGHTING_OUTPUT; i++)
        {
            var name = Enum.GetName(typeof(BacnetObjectTypes), i);
            if (name != null)
                cmbObjectType.Items.Add(name.Replace("OBJECT_", ""));
        }
        cmbObjectType.SelectedIndex = 0;

        btnCreate.Click += (_, _) =>
        {
            Confirmed = true;
            SelectedObjectType = (BacnetObjectTypes)(cmbObjectType.SelectedIndex);
            InstanceNumber = (uint)(nudInstance.Value ?? 1);
            Close();
        };
        btnCancel.Click += (_, _) => Close();
    }
}
