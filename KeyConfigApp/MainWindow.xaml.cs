using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ArcadeLeverKeyConfig;

public partial class MainWindow : Window
{
    private readonly IniConfigService _configService;
    private readonly XInputReader _gamepadReader = new();
    private readonly DispatcherTimer _gamepadTimer;
    private KeyBindingItem? _listeningKeyboardItem;
    private GamepadBindingItem? _listeningGamepadItem;
    private readonly HashSet<string> _ignoredGamepadControls = new(StringComparer.Ordinal);
    private bool _isLoadingConfiguration = true;
    private bool _isDirty;
    private bool _lastGamepadConnected;
    private int _lastGamepadIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        _configService = new IniConfigService(IniConfigService.LocateConfigPath());
        RefreshCharacters();
        DataContext = this;
        LoadFromIni(showStatus: true);

        _gamepadTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(30),
        };
        _gamepadTimer.Tick += GamepadTimer_Tick;
        _gamepadTimer.Start();
    }

    public ObservableCollection<KeyBindingItem> LeverBindings { get; } = [];
    public ObservableCollection<KeyBindingItem> UpperButtonBindings { get; } = [];
    public ObservableCollection<KeyBindingItem> LowerButtonBindings { get; } = [];
    public ObservableCollection<GamepadBindingItem> GamepadLeverBindings { get; } = [];
    public ObservableCollection<GamepadBindingItem> GamepadUpperButtonBindings { get; } = [];
    public ObservableCollection<GamepadBindingItem> GamepadLowerButtonBindings { get; } = [];
    public ObservableCollection<CharacterDefinition> Characters { get; } = [];

    private IEnumerable<KeyBindingItem> AllKeyboardBindings =>
        LeverBindings.Concat(UpperButtonBindings).Concat(LowerButtonBindings);

    private IEnumerable<GamepadBindingItem> AllGamepadBindings =>
        GamepadLeverBindings.Concat(GamepadUpperButtonBindings).Concat(GamepadLowerButtonBindings);

    private void LoadFromIni(bool showStatus)
    {
        _isLoadingConfiguration = true;
        try
        {
            var configuration = _configService.Load();
            CharacterSelector.SelectedValue = configuration.Character;
            LeverBindings.Clear();
            UpperButtonBindings.Clear();
            LowerButtonBindings.Clear();
            GamepadLeverBindings.Clear();
            GamepadUpperButtonBindings.Clear();
            GamepadLowerButtonBindings.Clear();

            foreach (var definition in IniConfigService.KeyboardDefinitions)
            {
                var codes = configuration.Keyboard.TryGetValue(definition.Id, out var configured)
                    ? configured
                    : definition.Defaults;
                KeyboardCollectionFor(definition.Group).Add(new KeyBindingItem(definition, codes.ToArray()));
            }

            foreach (var definition in IniConfigService.GamepadDefinitions)
            {
                var controls = configuration.Gamepad.TryGetValue(definition.Id, out var configured)
                    ? configured
                    : definition.Defaults;
                GamepadCollectionFor(definition.Group).Add(new GamepadBindingItem(definition, controls.ToArray()));
            }

            StopListening();
            _isDirty = false;
            if (showStatus)
            {
                StatusText.Text = "設定を読み込みました。";
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "設定を読み込めませんでした。";
        }
        finally
        {
            _isLoadingConfiguration = false;
        }
    }

    private ObservableCollection<KeyBindingItem> KeyboardCollectionFor(string group) => group switch
    {
        "レバー" => LeverBindings,
        "上段ボタン" => UpperButtonBindings,
        _ => LowerButtonBindings,
    };

    private ObservableCollection<GamepadBindingItem> GamepadCollectionFor(string group) => group switch
    {
        "レバー" => GamepadLeverBindings,
        "上段ボタン" => GamepadUpperButtonBindings,
        _ => GamepadLowerButtonBindings,
    };

    private void KeyBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: KeyBindingItem selected })
        {
            return;
        }

        StopListening();
        _listeningKeyboardItem = selected;
        selected.IsListening = true;
        StatusText.Text = $"「{selected.Label}」の入力待ち（Esc: 中止 / Delete: 解除）";
        Keyboard.Focus(this);
        e.Handled = true;
    }

    private void GamepadBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GamepadBindingItem selected })
        {
            return;
        }

        StopListening();
        _listeningGamepadItem = selected;
        selected.IsListening = true;

        var snapshot = _gamepadReader.Read();
        _ignoredGamepadControls.UnionWith(snapshot.ActiveControls());
        StatusText.Text = snapshot.Connected
            ? $"「{selected.Label}」へ割り当てるレバー方向またはボタンを入力してください。"
            : "アケコンを接続してください。接続後の入力を待機します。";
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_listeningKeyboardItem is null && _listeningGamepadItem is null)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            e.Handled = true;
            StopListening();
            StatusText.Text = "入力の割り当てを中止しました。";
            return;
        }

        if (_listeningGamepadItem is not null)
        {
            if (key is Key.Delete or Key.Back)
            {
                e.Handled = true;
                _listeningGamepadItem.Controls = [];
                MarkDirty("アケコンの割り当てを解除しました。INIへ保存してください。");
                StopListening();
            }
            return;
        }

        e.Handled = true;
        if (key is Key.Delete or Key.Back)
        {
            _listeningKeyboardItem!.Codes = [];
            MarkDirty("キーボードの割り当てを解除しました。INIへ保存してください。");
            StopListening();
            return;
        }

        var code = KeyNames.ToCode(key);
        if (code is null)
        {
            StatusText.Text = "このキーには対応していません。別のキーを押してください。";
            return;
        }

        foreach (var binding in AllKeyboardBindings)
        {
            if (!ReferenceEquals(binding, _listeningKeyboardItem))
            {
                binding.Codes = binding.Codes.Where(existing => existing != code).ToArray();
            }
        }

        _listeningKeyboardItem!.Codes = [code];
        MarkDirty($"{KeyNames.Display(code)} を割り当てました。INIへ保存してください。");
        StopListening();
    }

    private void GamepadTimer_Tick(object? sender, EventArgs e)
    {
        var snapshot = _gamepadReader.Read();
        UpdateGamepadConnection(snapshot);

        if (_listeningGamepadItem is null || !snapshot.Connected)
        {
            return;
        }

        var active = snapshot.ActiveControls();
        _ignoredGamepadControls.IntersectWith(active);
        var selectedControl = active.FirstOrDefault(control => !_ignoredGamepadControls.Contains(control));
        if (selectedControl is null)
        {
            return;
        }

        foreach (var binding in AllGamepadBindings)
        {
            if (!ReferenceEquals(binding, _listeningGamepadItem))
            {
                binding.Controls = binding.Controls.Where(existing => existing != selectedControl).ToArray();
            }
        }

        _listeningGamepadItem.Controls = [selectedControl];
        MarkDirty($"{GamepadControlNames.Display(selectedControl)} を割り当てました。INIへ保存してください。");
        StopListening();
    }

    private void UpdateGamepadConnection(GamepadSnapshot snapshot)
    {
        if (snapshot.Connected == _lastGamepadConnected && snapshot.UserIndex == _lastGamepadIndex)
        {
            return;
        }

        _lastGamepadConnected = snapshot.Connected;
        _lastGamepadIndex = snapshot.UserIndex;
        GamepadConnectionText.Text = snapshot.Connected
            ? $"接続中 · {snapshot.DisplayName}"
            : "アケコンを接続してください";
        GamepadConnectionBadge.Background = snapshot.Connected
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 235, 220))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(236, 215, 223));
    }

    private void StopListening()
    {
        if (_listeningKeyboardItem is not null)
        {
            _listeningKeyboardItem.IsListening = false;
            _listeningKeyboardItem = null;
        }
        if (_listeningGamepadItem is not null)
        {
            _listeningGamepadItem.IsListening = false;
            _listeningGamepadItem = null;
        }
        _ignoredGamepadControls.Clear();
    }

    private void MarkDirty(string message)
    {
        _isDirty = true;
        StatusText.Text = message;
    }

    private Dictionary<string, IReadOnlyList<string>> CurrentKeyboardValues() =>
        AllKeyboardBindings.ToDictionary(binding => binding.Id, binding => binding.Codes, StringComparer.Ordinal);

    private Dictionary<string, IReadOnlyList<string>> CurrentGamepadValues() =>
        AllGamepadBindings.ToDictionary(binding => binding.Id, binding => binding.Controls, StringComparer.Ordinal);

    private string CurrentCharacter() =>
        CharacterSelector.SelectedValue as string ?? _configService.DefaultCharacter;

    private void RefreshCharacters()
    {
        _configService.RefreshCharacters();
        Characters.Clear();
        foreach (var character in _configService.Characters)
        {
            Characters.Add(character);
        }
    }

    private void CharacterSelector_DropDownOpened(object sender, EventArgs e)
    {
        var selectedCharacter = CurrentCharacter();
        _isLoadingConfiguration = true;
        try
        {
            RefreshCharacters();
            CharacterSelector.SelectedValue = Characters.Any(option => option.Id == selectedCharacter)
                ? selectedCharacter
                : _configService.DefaultCharacter;
        }
        finally
        {
            _isLoadingConfiguration = false;
        }
    }

    private void CharacterSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingConfiguration || CharacterSelector.SelectedItem is not CharacterDefinition selected)
        {
            return;
        }
        StopListening();
        MarkDirty($"表示キャラクターを {selected.DisplayName} に変更しました。INIへ保存してください。");
    }

    private bool SaveToIni()
    {
        StopListening();
        try
        {
            _configService.Save(CurrentCharacter(), CurrentKeyboardValues(), CurrentGamepadValues());
            _isDirty = false;
            StatusText.Text = "設定を保存しました。";
            return true;
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "保存エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "設定を保存できませんでした。";
            return false;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => SaveToIni();

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty && MessageBox.Show(
                this,
                "保存していない変更を破棄してINIを読み込みますか？",
                "INIを再読み込み",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        RefreshCharacters();
        LoadFromIni(showStatus: true);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        StopListening();
        CharacterSelector.SelectedValue = _configService.DefaultCharacter;
        foreach (var binding in AllKeyboardBindings)
        {
            binding.Codes = binding.Definition.Defaults.ToArray();
        }
        foreach (var binding in AllGamepadBindings)
        {
            binding.Controls = binding.Definition.Defaults.ToArray();
        }
        MarkDirty("表示キャラクターと入力割り当てを初期設定へ戻しました。INIへ保存してください。");
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _gamepadTimer.Stop();
        if (!_isDirty)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            "保存していない表示・入力設定があります。保存して終了しますか？",
            "表示・入力設定",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            _gamepadTimer.Start();
        }
        else if (result == MessageBoxResult.Yes && !SaveToIni())
        {
            e.Cancel = true;
            _gamepadTimer.Start();
        }
    }
}
