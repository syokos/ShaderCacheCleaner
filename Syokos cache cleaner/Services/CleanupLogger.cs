using System.Collections.ObjectModel;
using System.Text;

namespace ShaderCacheCleaner.Services;

public sealed class CleanupLogger
{
    private readonly object _sync = new();
    private readonly StringBuilder _builder = new();
    private readonly string _logFilePath;

    public ObservableCollection<string> Lines { get; } = [];

    public CleanupLogger()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShaderCacheCleaner",
            "logs");
        Directory.CreateDirectory(dir);
        _logFilePath = Path.Combine(dir, $"cleanup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    public string LogFilePath => _logFilePath;

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
        lock (_sync)
        {
            _builder.AppendLine(line);
            try
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // ignore IO errors on log
            }
        }

        try
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Lines.Add(line);
                while (Lines.Count > 500)
                    Lines.RemoveAt(0);
            });
        }
        catch
        {
            // UI may not be ready
        }
    }

    public string GetFullText()
    {
        lock (_sync)
            return _builder.ToString();
    }
}
