using System.Windows;
using TaskMgrClone.Core;

namespace TaskMgrClone;

public partial class PropertiesWindow : Window
{
    private readonly ProcessRow _row;

    public PropertiesWindow(ProcessRow row)
    {
        InitializeComponent();
        _row = row;
        Title = row.Name + " Properties";

        var (description, company, product, version, fileVersion) = FileInfo(row.Path);
        NameText.Text = string.IsNullOrEmpty(product) ? row.Name : product;
        PublisherText.Text = string.IsNullOrEmpty(company) ? row.Publisher : company;
        DescriptionText.Text = string.IsNullOrEmpty(description) ? row.Description : description;
        VersionText.Text = string.IsNullOrEmpty(fileVersion) ? version : fileVersion;
        PidText.Text = row.Pid.ToString();
        UserText.Text = row.UserName;
        StartedText.Text = row.StartTime?.ToString("g") ?? "N/A";
        PathText.Text = row.Path;
        CommandText.Text = row.CommandLine;
        TypeText.Text = row.Platform + (row.IsPackaged ? " • Packaged app" : "") + (row.Elevated ? " • Elevated" : "");
    }

    private static (string Description, string Company, string Product, string Version, string FileVersion) FileInfo(string path)
    {
        try
        {
            var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            return (v.FileDescription ?? "", v.CompanyName ?? "", v.ProductName ?? "", v.ProductVersion ?? "", v.FileVersion ?? "");
        }
        catch { return ("", "", "", "", ""); }
    }

    private void OnOpenLocation(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_row.Path)) return;
        try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + _row.Path + "\""); } catch { }
    }

    private void OnSearchOnline(object sender, RoutedEventArgs e)
    {
        var term = string.IsNullOrEmpty(_row.Description) ? _row.Name : _row.Description;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://www.bing.com/search?q=" + Uri.EscapeDataString(term)) { UseShellExecute = true });
        }
        catch { }
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
