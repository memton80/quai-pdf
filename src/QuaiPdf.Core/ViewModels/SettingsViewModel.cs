using CommunityToolkit.Mvvm.ComponentModel;
using QuaiPdf.Core.Models;
using QuaiPdf.Core.Storage;

namespace QuaiPdf.Core.ViewModels;

/// <summary>Réglages : chaque modification est appliquée et enregistrée immédiatement.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private bool _loading;

    public SettingsViewModel(ISettingsService settings, AppPaths paths)
    {
        _settings = settings;
        DataDirectory = paths.Root;
        LogsDirectory = paths.LogsDirectory;
        Load();
        _settings.Changed += (_, _) => Load();
    }

    public string Version => AppInfo.Version;

    public string DataDirectory { get; }

    public string LogsDirectory { get; }

    /// <summary>Index dans <see cref="AppTheme"/> : 0 = système, 1 = clair, 2 = sombre.</summary>
    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    /// <summary>Volet latéral ouvert (vignettes, plan, recherche ou propriétés).</summary>
    [ObservableProperty]
    public partial bool ShowThumbnails { get; set; }

    /// <summary>Dernier volet choisi dans le rail.</summary>
    [ObservableProperty]
    public partial SidePanel SidePanel { get; set; }

    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    partial void OnThemeIndexChanged(int value)
    {
        if (!_loading && Enum.IsDefined((AppTheme)value))
        {
            _settings.Update(s => s.Theme = (AppTheme)value);
        }
    }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (!_loading)
        {
            _settings.Update(s => s.CheckForUpdates = value);
        }
    }

    partial void OnShowThumbnailsChanged(bool value)
    {
        if (!_loading)
        {
            _settings.Update(s => s.ShowThumbnails = value);
        }
    }

    partial void OnSidePanelChanged(SidePanel value)
    {
        if (!_loading && Enum.IsDefined(value))
        {
            _settings.Update(s => s.SidePanel = value);
        }
    }

    private void Load()
    {
        _loading = true;
        try
        {
            var current = _settings.Current;
            ThemeIndex = (int)current.Theme;
            ShowThumbnails = current.ShowThumbnails;
            SidePanel = current.SidePanel;
            CheckForUpdates = current.CheckForUpdates;
        }
        finally
        {
            _loading = false;
        }
    }
}
