using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ArcadeLeverKeyConfig;

public sealed class KeyBindingItem : INotifyPropertyChanged
{
    private IReadOnlyList<string> _codes;
    private bool _isListening;

    public KeyBindingItem(BindingDefinition definition, IReadOnlyList<string> codes)
    {
        Definition = definition;
        _codes = codes;
    }

    public BindingDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Label => Definition.Label;
    public IReadOnlyList<string> Codes
    {
        get => _codes;
        set
        {
            _codes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
        }
    }

    public bool IsListening
    {
        get => _isListening;
        set
        {
            _isListening = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
        }
    }

    public string DisplayValue => IsListening
        ? "キーを入力…"
        : Codes.Count == 0
            ? "未設定"
            : string.Join(" / ", Codes.Select(KeyNames.Display));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record BindingDefinition(
    string Id,
    string Section,
    string IniKey,
    string Label,
    string Group,
    IReadOnlyList<string> Defaults);

public sealed class GamepadBindingItem : INotifyPropertyChanged
{
    private IReadOnlyList<string> _controls;
    private bool _isListening;

    public GamepadBindingItem(GamepadBindingDefinition definition, IReadOnlyList<string> controls)
    {
        Definition = definition;
        _controls = controls;
    }

    public GamepadBindingDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Label => Definition.Label;

    public IReadOnlyList<string> Controls
    {
        get => _controls;
        set
        {
            _controls = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
        }
    }

    public bool IsListening
    {
        get => _isListening;
        set
        {
            _isListening = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
        }
    }

    public string DisplayValue => IsListening
        ? "入力を待機中…"
        : Controls.Count == 0
            ? "未設定"
            : string.Join(" / ", Controls.Select(GamepadControlNames.Display));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record GamepadBindingDefinition(
    string Id,
    string Section,
    string IniKey,
    string Label,
    string Group,
    IReadOnlyList<string> Defaults);
