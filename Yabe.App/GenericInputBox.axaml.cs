using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Yabe;

public partial class GenericInputBox : Window
{
    public string? InputValue { get; private set; }
    public bool Confirmed { get; private set; }

    public GenericInputBox()
    {
        InitializeComponent();
        btnOk.Click += (_, _) => { Confirmed = true; InputValue = txtInput.Text; Close(); };
        btnCancel.Click += (_, _) => Close();
    }

    public GenericInputBox(string title, string label, string defaultValue = "") : this()
    {
        Title = title;
        lblPrompt.Text = label;
        txtInput.Text = defaultValue;
    }
}
