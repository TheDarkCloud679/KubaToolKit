using System.IO;

namespace KubaToolKit.Shared.Services;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Error = 2
}

/// Journalisation fichier minimale : un fichier par jour dans un dossier
/// "Logs" à côté de l'exécutable (donc sous bin\... en dev, déjà ignoré
/// par git ; à côté de l'exe dans un build publié). Le niveau minimum se
/// règle en éditant Logs\loglevel.txt (DEBUG, INFO ou ERROR) puis en
/// relançant l'application.
public static class Logger
{
    private static readonly object WriteLock = new();

    public static string LogsFolder { get; } =
        Path.Combine(AppContext.BaseDirectory, "Logs");

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    static Logger()
    {
        try
        {
            Directory.CreateDirectory(LogsFolder);
            MinimumLevel = LoadConfiguredLevel();
        }
        catch
        {
            // La journalisation ne doit jamais empêcher l'appli de démarrer.
        }
    }

    private static LogLevel
    LoadConfiguredLevel()
    {
        var configPath = Path.Combine(LogsFolder, "loglevel.txt");

        if (!File.Exists(configPath))
        {
            File.WriteAllText(
                configPath,
                """
                # Niveau minimum des journaux dans ce dossier : DEBUG, INFO ou ERROR.
                # (INFO par défaut. Modifier cette valeur puis relancer KubaToolKit.)
                INFO
                """);

            return LogLevel.Info;
        }

        var configuredLine =
            File.ReadAllLines(configPath)
                .FirstOrDefault(line =>
                    !line.TrimStart().StartsWith('#')
                    && !string.IsNullOrWhiteSpace(line));

        return Enum.TryParse<LogLevel>(configuredLine?.Trim(), true, out var level)
            ? level
            : LogLevel.Info;
    }

    public static void
    Debug(
        string message) =>
        Write(LogLevel.Debug, message);

    public static void
    Info(
        string message) =>
        Write(LogLevel.Info, message);

    public static void
    Error(
        string message,
        Exception? exception = null) =>
        Write(
            LogLevel.Error,
            exception == null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void
    Write(
        LogLevel level,
        string message)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var line =
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant()}] {message}";

        lock (WriteLock)
        {
            try
            {
                var filePath =
                    Path.Combine(LogsFolder, $"kubatoolkit-{DateTime.Now:yyyy-MM-dd}.log");

                File.AppendAllText(filePath, line + Environment.NewLine);
            }
            catch
            {
                // Idem : ne jamais planter l'appli à cause d'un problème
                // d'écriture de log (dossier verrouillé, disque plein...).
            }
        }
    }
}
