using CommunityToolkit.Mvvm.ComponentModel;

namespace VpsWatcher.App.ViewModels;

/// <summary>
/// One mount point's gauge (design §5.3). Updated in place across 1Hz samples so only the changed
/// percentage notifies (§13.2) — the panel's disk rows are not rebuilt every second.
/// </summary>
public sealed partial class DiskGaugeViewModel : ObservableObject
{
    public DiskGaugeViewModel(string mount) => Mount = mount;

    /// <summary>Mount path, e.g. "/" or "/var/www". Identity for in-place reconciliation; set once.</summary>
    public string Mount { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsedText))]
    private double _usedPct;

    [ObservableProperty]
    private double _usedGb;

    [ObservableProperty]
    private double _totalGb;

    /// <summary>Display text for the disk row, e.g. "48.1% (24.0 / 50.0 GB)".</summary>
    public string UsedText => $"{UsedPct:0.0}% ({UsedGb:0.0} / {TotalGb:0.0} GB)";
}
