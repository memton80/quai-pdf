using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using QuaiPdf.App.Localization;
using QuaiPdf.App.Views;
using QuaiPdf.Core.Services;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.App.Services;

/// <summary>
/// Boîtes de dialogue WinUI : une seule à la fois (contrainte de <see cref="ContentDialog"/>),
/// toujours affichées depuis le fil d'interface quel que soit le fil appelant.
/// </summary>
internal sealed class DialogService : IDialogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Racine de la fenêtre principale ; renseignée par celle-ci à sa création.</summary>
    public FrameworkElement? Root { get; set; }

    /// <summary>Dialogue actuellement affiché (utilisé par le test de fumée).</summary>
    public ContentDialog? Current { get; private set; }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false) =>
        OnUiAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = confirmText,
                CloseButtonText = Loc.Get("Common_Cancel"),
                // Action irréversible : « Annuler » reste le bouton par défaut.
                DefaultButton = isDestructive ? ContentDialogButton.Close : ContentDialogButton.Primary,
            };

            return await ShowAsync(dialog) == ContentDialogResult.Primary;
        });

    public Task ShowErrorAsync(string title, string message) =>
        OnUiAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                CloseButtonText = Loc.Get("Common_Close"),
                DefaultButton = ContentDialogButton.Close,
            };

            await ShowAsync(dialog);
            return true;
        });

    public Task<string?> PromptPasswordAsync(string fileName, bool isRetry) =>
        OnUiAsync(async () =>
        {
            var box = new PasswordBox();
            AutomationProperties.SetName(box, Loc.Get("Password_Field"));
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = Loc.Format(isRetry ? "Password_Retry" : "Password_Message", fileName),
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(box);

            var dialog = new ContentDialog
            {
                Title = Loc.Get("Password_Title"),
                Content = panel,
                PrimaryButtonText = Loc.Get("Password_Open"),
                CloseButtonText = Loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
            };
            dialog.Opened += (_, _) => box.Focus(FocusState.Programmatic);

            return await ShowAsync(dialog) == ContentDialogResult.Primary ? box.Password : null;
        });

    public Task<SaveChoice> AskSaveChangesAsync(string fileName) =>
        OnUiAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = Loc.Get("SaveChanges_Title"),
                Content = new TextBlock { Text = Loc.Format("SaveChanges_Message", fileName), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = Loc.Get("SaveChanges_Save"),
                SecondaryButtonText = Loc.Get("SaveChanges_Discard"),
                CloseButtonText = Loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
            };

            return await ShowAsync(dialog) switch
            {
                ContentDialogResult.Primary => SaveChoice.Save,
                ContentDialogResult.Secondary => SaveChoice.Discard,
                _ => SaveChoice.Cancel,
            };
        });

    public Task<bool> EditMetadataAsync(MetadataEditorViewModel editor) =>
        OnUiAsync(async () => await ShowAsync(new MetadataDialog(editor)) == ContentDialogResult.Primary);

    /// <summary>Réglages (thème, vignettes, emplacement des données).</summary>
    public Task ShowSettingsAsync(SettingsViewModel settings) =>
        OnUiAsync(async () =>
        {
            await ShowAsync(new SettingsDialog(settings));
            return true;
        });

    private Task<T> OnUiAsync<T>(Func<Task<T>> action)
    {
        var queue = (Root ?? throw new InvalidOperationException("La fenêtre principale n'est pas prête.")).DispatcherQueue;
        if (queue.HasThreadAccess)
        {
            return action();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = queue.TryEnqueue(async () =>
        {
            try
            {
                completion.TrySetResult(await action());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        if (!queued)
        {
            completion.TrySetException(new InvalidOperationException("Le fil d'interface n'accepte plus de travail."));
        }

        return completion.Task;
    }

    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        var root = Root ?? throw new InvalidOperationException("La fenêtre principale n'est pas prête.");

        // Thème changé pendant que le dialogue est ouvert (depuis les réglages eux-mêmes) : le dialogue suit la fenêtre.
        void FollowTheme(FrameworkElement sender, object args) => dialog.RequestedTheme = sender.ActualTheme;

        await _gate.WaitAsync();
        try
        {
            dialog.XamlRoot = root.XamlRoot;
            dialog.RequestedTheme = root.ActualTheme;
            root.ActualThemeChanged += FollowTheme;
            if (dialog.Style is null && Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style))
            {
                dialog.Style = style as Style;
            }

            Current = dialog;
            return await dialog.ShowAsync();
        }
        finally
        {
            root.ActualThemeChanged -= FollowTheme;
            Current = null;
            _gate.Release();
        }
    }
}
