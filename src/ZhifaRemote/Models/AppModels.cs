using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZhifaRemote.Models;

public sealed class AudioDeviceInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsDefault { get; init; }
    public bool IsVirtual { get; init; }
}

public enum TransferDirection
{
    Sending,
    Receiving
}

public enum TransferState
{
    Waiting,
    Active,
    Completed,
    Failed,
    Cancelled
}

public sealed class TransferItem : INotifyPropertyChanged
{
    private double _progress;
    private TransferState _state;

    public int Id { get; init; }
    public string Name { get; init; } = "";
    public long Size { get; init; }
    public TransferDirection Direction { get; init; }

    public double Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            OnPropertyChanged();
        }
    }

    public TransferState State
    {
        get => _state;
        set
        {
            _state = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class LogEntry : INotifyPropertyChanged
{
    private string _text = "";

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ObservableCollectionEx<T> : System.Collections.ObjectModel.ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items) Add(item);
    }
}

public enum BackgroundMode
{
    Default,
    Static,
    Dynamic
}

public enum DynamicBackgroundKind
{
    Aurora,
    LightStream,
    Video
}

public sealed class AppSettings
{
    public BackgroundMode BackgroundMode { get; set; }
    public string StaticColorHex { get; set; } = "#EAF4FB";
    public string StaticImagePath { get; set; } = "";
    public DynamicBackgroundKind DynamicKind { get; set; } = DynamicBackgroundKind.Aurora;
    public string DynamicVideoPath { get; set; } = "";
    public double AnimationSpeed { get; set; } = 1.0;
}
