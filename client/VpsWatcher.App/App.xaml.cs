using System.Windows;
using VpsWatcher.App.Configuration;
using VpsWatcher.App.Services;
using VpsWatcher.App.ViewModels;

namespace VpsWatcher.App;

/// <summary>
/// Application entry point. Phase 3a wiring: load the (single) connection target from args/env or
/// the user-local servers.json, build the <see cref="MainViewModel"/> (which owns and starts the
/// <c>SshConnectionService</c>), and show the normal window. Real connection values are never read
/// from the repo (CLAUDE.md) — see <see cref="AppServerConfigLoader"/>.
/// </summary>
public partial class App : Application
{
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dispatcher = new WpfUiDispatcher(Dispatcher);
        var config = AppServerConfigLoader.Load(e.Args, out var configError);

        _mainViewModel = new MainViewModel(config, configError, dispatcher);

        var window = new MainWindow { DataContext = _mainViewModel };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Dispose();
        base.OnExit(e);
    }
}
