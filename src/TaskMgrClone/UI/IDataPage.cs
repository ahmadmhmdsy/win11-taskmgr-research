using System.Windows;
using System.Windows.Controls;
using TaskMgrClone.Core;

namespace TaskMgrClone.UI;

/// <summary>Contract between the shell (MainWindow) and each navigable page.</summary>
public interface IDataPage
{
    string Title { get; }
    bool CanSearch { get; }
    bool SupportsEndTask { get; }
    bool SupportsEfficiencyMode { get; }
    string PrimaryButtonText { get; }

    void OnSample(SystemSnapshot system, Dictionary<int, ProcessRow> processes);
    void SetSearch(string text);
    void RefreshNow();
    void OnPrimaryAction();
    void ToggleEfficiencySelected();
}

public abstract class PageBase : UserControl, IDataPage
{
    protected PageBase()
    {
        // UserControl's default content alignment is Left; pages must fill the host.
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public virtual string Title => "";
    public virtual bool CanSearch => true;
    public virtual bool SupportsEndTask => false;
    public virtual bool SupportsEfficiencyMode => false;
    public virtual string PrimaryButtonText => "";

    public virtual void OnSample(SystemSnapshot system, Dictionary<int, ProcessRow> processes) { }
    public virtual void SetSearch(string text) { }
    public virtual void RefreshNow() { }
    public virtual void OnPrimaryAction() { }
    public virtual void ToggleEfficiencySelected() { }
}