using System.Windows;
using System.Windows.Controls;
using TaskMgrClone.Core;

namespace TaskMgrClone;

public partial class AffinityWindow : Window
{
    private readonly ProcessRow _row;
    private readonly List<CheckBox> _boxes = new();

    public AffinityWindow(ProcessRow row)
    {
        InitializeComponent();
        _row = row;
        Title = row.Name + " processor affinity";
        CaptionText.Text = $"{row.Name} (PID {row.Pid}) processor affinity";

        var (processMask, systemMask) = ProcessActions.GetAffinity(row.Pid);
        long mask = processMask != 0 ? processMask : systemMask;
        int cores = Environment.ProcessorCount;
        for (int i = 0; i < cores; i++)
        {
            var box = new CheckBox
            {
                Content = "CPU " + i,
                Margin = new Thickness(0, 4, 18, 4),
                IsChecked = mask == 0 || (mask & (1L << i)) != 0,
                Tag = i,
            };
            _boxes.Add(box);
            CpuPanel.Children.Add(box);
        }
    }

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        foreach (var box in _boxes) box.IsChecked = true;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        long mask = 0;
        foreach (var box in _boxes)
            if (box.IsChecked == true) mask |= 1L << (int)box.Tag;

        if (mask == 0)
        {
            MessageBox.Show(this, "Select at least one processor.", "Task Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!ProcessActions.SetAffinity(_row.Pid, mask))
            MessageBox.Show(this, "The affinity could not be changed. Access is denied.", "Task Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            DialogResult = true;
    }
}
