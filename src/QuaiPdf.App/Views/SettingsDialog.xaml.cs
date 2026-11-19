using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.App.Views;

/// <summary>Réglages : appliqués et enregistrés immédiatement, sans bouton « Enregistrer ».</summary>
public sealed partial class SettingsDialog : ContentDialog
{
    public SettingsDialog(SettingsViewModel settings)
    {
        Settings = settings;
        InitializeComponent();
    }

    public SettingsViewModel Settings { get; }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Settings.LogsDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Settings.LogsDirectory}\"") { UseShellExecute = true });
    }
}
