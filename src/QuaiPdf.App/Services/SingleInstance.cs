using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Windows.AppLifecycle;
using QuaiPdf.Core.Storage;

namespace QuaiPdf.App.Services;

/// <summary>
/// Une seule fenêtre de Quai PDF (cycle de vie du Windows App SDK, <see cref="AppInstance"/>) : un PDF ouvert alors que
/// Quai PDF tourne déjà (« Ouvrir avec », double-clic, glisser sur l'icône) est transmis à la fenêtre existante, qui
/// l'ouvre dans un onglet ; le second lancement s'arrête aussitôt. Les tests du CI, qui isolent leurs données, ont leur
/// propre clé : ils ne parlent jamais à un Quai PDF ouvert par l'utilisateur.
/// </summary>
internal static class SingleInstance
{
    private const string BaseKey = "QuaiPdf";

    private static readonly Lock Gate = new();
    private static readonly List<IReadOnlyList<string>> Pending = [];
    private static Action<IReadOnlyList<string>>? _handler;

    /// <summary>Clé de l'instance principale : une par dossier de données (celui de l'utilisateur, ou celui d'un test).</summary>
    internal static string Key
    {
        get
        {
            var isolated = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable);
            if (string.IsNullOrWhiteSpace(isolated))
            {
                return BaseKey;
            }

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(isolated).ToUpperInvariant()));
            return $"{BaseKey}-{Convert.ToHexString(hash)[..16]}";
        }
    }

    /// <summary>
    /// Au démarrage, avant toute fenêtre : si une instance principale existe déjà, lui transmet ce lancement et
    /// retourne vrai (ce processus doit s'arrêter). Sinon, ce processus devient l'instance principale.
    /// </summary>
    public static bool RedirectToMainInstance()
    {
        AppInstance main;
        try
        {
            main = AppInstance.FindOrRegisterForKey(Key);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Sans cycle de vie (environnement inhabituel), Quai PDF reste utilisable : une fenêtre par lancement.
            return false;
        }

        if (main.IsCurrent)
        {
            main.Activated += OnActivated;
            return false;
        }

        Redirect(AppInstance.GetCurrent().GetActivatedEventArgs(), main);
        return true;
    }

    /// <summary>
    /// Fichiers reçus d'un autre lancement, transmis à <paramref name="handler"/> (sur un fil quelconque) ; ceux reçus
    /// avant l'appel (fenêtre pas encore prête) le sont tout de suite.
    /// </summary>
    public static void Listen(Action<IReadOnlyList<string>> handler)
    {
        List<IReadOnlyList<string>> pending;
        lock (Gate)
        {
            _handler = handler;
            pending = [.. Pending];
            Pending.Clear();
        }

        foreach (var files in pending)
        {
            handler(files);
        }
    }

    /// <summary>PDF existants désignés par une ligne de commande (le programme lui-même et les options sont ignorés).</summary>
    internal static List<string> PdfFiles(IEnumerable<string> arguments) =>
        [.. arguments
            .Where(arg => !arg.StartsWith("--", StringComparison.Ordinal))
            .Where(arg => string.Equals(Path.GetExtension(arg), ".pdf", StringComparison.OrdinalIgnoreCase) && File.Exists(arg))
            .Select(Path.GetFullPath)];

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        var files = FilesOf(args);
        Action<IReadOnlyList<string>>? handler;
        lock (Gate)
        {
            handler = _handler;
            if (handler is null)
            {
                Pending.Add(files);
                return;
            }
        }

        handler(files);
    }

    private static List<string> FilesOf(AppActivationArguments args)
    {
        try
        {
            return args.Data switch
            {
                Windows.ApplicationModel.Activation.IFileActivatedEventArgs file =>
                    PdfFiles(file.Files.Select(item => item.Path)),
                Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch =>
                    PdfFiles(SplitCommandLine(launch.Arguments)),
                _ => [],
            };
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>Découpe une ligne de commande selon les règles de Windows (chemins entre guillemets).</summary>
    private static List<string> SplitCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        var argv = NativeMethods.CommandLineToArgvW(commandLine, out var count);
        if (argv == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            var arguments = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                if (Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) is { } argument)
                {
                    arguments.Add(argument);
                }
            }

            return arguments;
        }
        finally
        {
            NativeMethods.LocalFree(argv);
        }
    }

    /// <summary>
    /// Transmet ce lancement à l'instance principale et attend la fin de l'envoi en laissant tourner les messages COM
    /// (méthode recommandée par la documentation du Windows App SDK), puis passe la main à sa fenêtre.
    /// </summary>
    private static void Redirect(AppActivationArguments args, AppInstance main)
    {
        // Ce lancement vient de l'utilisateur : il a le droit de mettre une fenêtre au premier plan, et le cède.
        NativeMethods.AllowSetForegroundWindow(main.ProcessId);

        var done = NativeMethods.CreateEventW(IntPtr.Zero, bManualReset: true, bInitialState: false, null);
        try
        {
            Task.Run(() =>
            {
                try
                {
                    main.RedirectActivationToAsync(args).AsTask().Wait(TimeSpan.FromSeconds(30));
                }
                catch (Exception)
                {
                    // Instance principale disparue entre-temps : rien de plus à faire, ce lancement s'arrête.
                }
                finally
                {
                    NativeMethods.SetEvent(done);
                }
            });

            _ = NativeMethods.CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, [done], out _);
        }
        finally
        {
            NativeMethods.CloseHandle(done);
        }

        try
        {
            using var process = Process.GetProcessById((int)main.ProcessId);
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(process.MainWindowHandle);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Instance principale déjà fermée.
        }
    }
}
