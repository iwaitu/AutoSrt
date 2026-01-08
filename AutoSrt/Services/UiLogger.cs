using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace AutoSrt.Services;

public sealed class UiLogger : INotifyPropertyChanged
{
    private readonly StringBuilder _sb = new();
    private string _logText = string.Empty;

    public string LogText
    {
        get => _logText;
        private set
        {
            if (_logText == value) return;
            _logText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? LogAdded;

    public void Clear()
    {
        _sb.Clear();
        LogText = string.Empty;
    }

    public void Info(string message) => Append("INFO", message);

    public void Warn(string message) => Append("WARN", message);

    public void Error(string message) => Append("ERROR", message);

    public void Error(Exception ex, string message) => Append("ERROR", $"{message}{Environment.NewLine}{ex}");

    private void Append(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {level}: {message}";
        _sb.AppendLine(line);
        LogText = _sb.ToString();
        LogAdded?.Invoke(line);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
