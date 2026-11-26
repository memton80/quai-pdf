using QuaiPdf.Core.Models;
using QuaiPdf.Core.Storage;
using QuaiPdf.Core.Tests.Support;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.Core.Tests.Storage;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Defaults_apply_when_there_is_no_file()
    {
        using var temp = new TempDirectory();

        var settings = new JsonSettingsService(temp.File("settings.json")).Current;

        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.True(settings.ShowThumbnails);
    }

    [Fact]
    public void Changes_are_written_in_camel_case_and_read_back()
    {
        using var temp = new TempDirectory();
        var path = temp.File("settings.json");

        new JsonSettingsService(path).Update(s =>
        {
            s.Theme = AppTheme.Dark;
            s.ShowThumbnails = false;
        });

        Assert.Contains("\"theme\": \"dark\"", File.ReadAllText(path));
        var reloaded = new JsonSettingsService(path).Current;
        Assert.Equal(AppTheme.Dark, reloaded.Theme);
        Assert.False(reloaded.ShowThumbnails);
    }

    [Fact]
    public void An_unreadable_file_is_set_aside_and_defaults_apply()
    {
        using var temp = new TempDirectory();
        var path = temp.File("settings.json");
        File.WriteAllText(path, "{ pas du json");

        var settings = new JsonSettingsService(path).Current;

        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Single(Directory.GetFiles(temp.Path, "settings.json.corrompu-*"));
    }

    [Fact]
    public void An_unknown_theme_falls_back_to_system()
    {
        using var temp = new TempDirectory();
        var path = temp.File("settings.json");
        File.WriteAllText(path, """{ "theme": 42 }""");

        Assert.Equal(AppTheme.System, new JsonSettingsService(path).Current.Theme);
    }

    [Fact]
    public void Changed_is_raised_only_for_real_changes()
    {
        using var temp = new TempDirectory();
        var service = new JsonSettingsService(temp.File("settings.json"));
        var raised = 0;
        service.Changed += (_, _) => raised++;

        service.Update(s => s.Theme = AppTheme.System);
        service.Update(s => s.Theme = AppTheme.Light);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void SettingsViewModel_saves_each_change()
    {
        using var temp = new TempDirectory();
        var service = new JsonSettingsService(temp.File("settings.json"));
        var viewModel = new SettingsViewModel(service, new AppPaths(temp.Path));

        viewModel.ThemeIndex = 2;
        viewModel.ShowThumbnails = false;
        viewModel.SidePanel = SidePanel.Search;

        Assert.Equal(AppTheme.Dark, service.Current.Theme);
        Assert.False(service.Current.ShowThumbnails);
        Assert.Equal(SidePanel.Search, service.Current.SidePanel);
    }

    [Fact]
    public void Side_panel_is_written_in_camel_case_and_read_back()
    {
        using var temp = new TempDirectory();
        var path = temp.File("settings.json");

        new JsonSettingsService(path).Update(s => s.SidePanel = SidePanel.Outline);

        Assert.Contains("\"sidePanel\": \"outline\"", File.ReadAllText(path));
        Assert.Equal(SidePanel.Outline, new JsonSettingsService(path).Current.SidePanel);
    }

    [Fact]
    public void Unknown_side_panel_falls_back_to_pages()
    {
        using var temp = new TempDirectory();
        var path = temp.File("settings.json");
        File.WriteAllText(path, "{ \"sidePanel\": 42 }");

        Assert.Equal(SidePanel.Pages, new JsonSettingsService(path).Current.SidePanel);
    }

    [Fact]
    public void AtomicFile_replaces_content_without_leaving_temporary_files()
    {
        using var temp = new TempDirectory();
        var path = temp.File("doc.pdf");
        File.WriteAllBytes(path, [1, 2, 3]);

        AtomicFile.Write(path, [4, 5]);

        Assert.Equal([4, 5], File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(temp.Path));
    }
}
