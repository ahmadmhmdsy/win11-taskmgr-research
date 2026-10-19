using System.Windows;
using Microsoft.Win32;

namespace TaskMgrClone;

public partial class RunNewTaskWindow : Window
{
    public string Command { get; private set; } = "";
    public bool Elevated { get; private set; }

    public RunNewTaskWindow() => InitializeComponent();

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open",
            Filter = "Programs (*.exe;*.bat;*.cmd;*.com)|*.exe;*.bat;*.cmd;*.com|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true) CommandBox.Text = dialog.FileName;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var command = CommandBox.Text.Trim();
        if (command.Length == 0) return;
        Command = command;
        Elevated = ElevatedCheck.IsChecked == true;
        DialogResult = true;
    }
}
