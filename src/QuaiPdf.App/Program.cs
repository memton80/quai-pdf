using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using QuaiPdf.App.Services;

namespace QuaiPdf.App;

/// <summary>
/// Point d'entrée (remplace celui généré par le XAML, <c>DISABLE_XAML_GENERATED_MAIN</c>) : avant de créer
/// l'application, un second lancement transmet ses fichiers au Quai PDF déjà ouvert et s'arrête (instance unique).
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Les tests du CI isolent leurs données dès maintenant : la clé de l'instance unique en dépend.
        if (SmokeTest.TryGetOutputDirectory(out var smokeDirectory))
        {
            SmokeTest.IsolateData(smokeDirectory);
        }
        else if (SmokeTest.TryGetUpdateTest(out var updateDirectory, out _))
        {
            SmokeTest.IsolateData(updateDirectory);
        }

        if (SingleInstance.RedirectToMainInstance())
        {
            return 0;
        }

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }
}
