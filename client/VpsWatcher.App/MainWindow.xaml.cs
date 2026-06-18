using System.Windows;

namespace VpsWatcher.App;

/// <summary>
/// Phase 3a main window: a normal window bound to <c>MainViewModel</c>. MVVM — no logic here;
/// the DataContext is assigned by <c>App.OnStartup</c>.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
