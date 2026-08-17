using System.IO;
using System.Text;

namespace ArcadeLeverKeyConfig;

public sealed record InputConfiguration(
    string Character,
    Dictionary<string, IReadOnlyList<string>> Keyboard,
    Dictionary<string, IReadOnlyList<string>> Gamepad);

public sealed record CharacterDefinition(string Id, string DisplayName);

public sealed class IniConfigService
{
    private const string FallbackDefaultCharacter = "angelis";

    public static readonly IReadOnlyList<BindingDefinition> KeyboardDefinitions =
    [
        new("direction-up", "Lever", "Up", "上", "レバー", ["ArrowUp", "KeyW"]),
        new("direction-down", "Lever", "Down", "下", "レバー", ["ArrowDown", "KeyS"]),
        new("direction-left", "Lever", "Left", "左", "レバー", ["ArrowLeft", "KeyA"]),
        new("direction-right", "Lever", "Right", "右", "レバー", ["ArrowRight", "KeyD"]),
        new("button-4", "UpperButtons", "Button1", "1（左端）", "上段ボタン", ["KeyU"]),
        new("button-5", "UpperButtons", "Button2", "2", "上段ボタン", ["KeyI"]),
        new("button-6", "UpperButtons", "Button3", "3", "上段ボタン", ["KeyO"]),
        new("button-7", "UpperButtons", "Button4", "4（右端）", "上段ボタン", ["KeyP"]),
        new("button-0", "LowerButtons", "Button1", "1（左端）", "下段ボタン", ["KeyJ"]),
        new("button-1", "LowerButtons", "Button2", "2", "下段ボタン", ["KeyK"]),
        new("button-2", "LowerButtons", "Button3", "3", "下段ボタン", ["KeyL"]),
        new("button-3", "LowerButtons", "Button4", "4（右端）", "下段ボタン", ["Semicolon"]),
    ];

    public static readonly IReadOnlyList<GamepadBindingDefinition> GamepadDefinitions =
    [
        new("direction-up", "GamepadLever", "Up", "上", "レバー", ["Button12", "Axis1-"]),
        new("direction-down", "GamepadLever", "Down", "下", "レバー", ["Button13", "Axis1+"]),
        new("direction-left", "GamepadLever", "Left", "左", "レバー", ["Button14", "Axis0-"]),
        new("direction-right", "GamepadLever", "Right", "右", "レバー", ["Button15", "Axis0+"]),
        new("button-4", "GamepadUpperButtons", "Button1", "1（左端）", "上段ボタン", ["Button2"]),
        new("button-5", "GamepadUpperButtons", "Button2", "2", "上段ボタン", ["Button3"]),
        new("button-6", "GamepadUpperButtons", "Button3", "3", "上段ボタン", ["Button5"]),
        new("button-7", "GamepadUpperButtons", "Button4", "4（右端）", "上段ボタン", ["Button4"]),
        new("button-0", "GamepadLowerButtons", "Button1", "1（左端）", "下段ボタン", ["Button0"]),
        new("button-1", "GamepadLowerButtons", "Button2", "2", "下段ボタン", ["Button1"]),
        new("button-2", "GamepadLowerButtons", "Button3", "3", "下段ボタン", ["Button7"]),
        new("button-3", "GamepadLowerButtons", "Button4", "4（右端）", "下段ボタン", ["Button6"]),
    ];

    public IniConfigService(string configPath)
    {
        ConfigPath = configPath;
        RefreshCharacters();
    }

    public string ConfigPath { get; }
    public IReadOnlyList<CharacterDefinition> Characters { get; private set; } = [];
    public string DefaultCharacter =>
        Characters.FirstOrDefault(option => option.Id == FallbackDefaultCharacter)?.Id ??
        Characters.FirstOrDefault()?.Id ??
        FallbackDefaultCharacter;

    public void RefreshCharacters()
    {
        var configDirectory = Path.GetDirectoryName(ConfigPath) ?? AppContext.BaseDirectory;
        var characterDirectory = new[]
        {
            Path.Combine(configDirectory, "web", "characters"),
            Path.Combine(configDirectory, "assets", "characters"),
        }.FirstOrDefault(Directory.Exists);

        if (characterDirectory is null)
        {
            Characters = [new(FallbackDefaultCharacter, "Angelis")];
            return;
        }

        var knownDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["angelis"] = "Angelis",
            ["cherubim"] = "Cherubim",
        };
        Characters = Directory.EnumerateFiles(characterDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path).Equals(".pak", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name.ToLowerInvariant(), StringComparer.Ordinal)
            .Select(group =>
            {
                var fileName = group.First();
                var id = group.Key;
                return new CharacterDefinition(
                    id,
                    knownDisplayNames.TryGetValue(id, out var displayName) ? displayName : fileName);
            })
            .OrderBy(option => option.Id == FallbackDefaultCharacter ? 0 : 1)
            .ThenBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (Characters.Count == 0)
        {
            Characters = [new(FallbackDefaultCharacter, "Angelis")];
        }
    }

    public InputConfiguration Load()
    {
        var character = DefaultCharacter;
        var keyboard = KeyboardDefinitions.ToDictionary(
            definition => definition.Id,
            definition => definition.Defaults,
            StringComparer.Ordinal);
        var gamepad = GamepadDefinitions.ToDictionary(
            definition => definition.Id,
            definition => definition.Defaults,
            StringComparer.Ordinal);

        if (!File.Exists(ConfigPath))
        {
            Save(character, keyboard, gamepad);
            return new InputConfiguration(character, keyboard, gamepad);
        }

        var keyboardLookup = KeyboardDefinitions.ToDictionary(
            definition => $"{definition.Section}\0{definition.IniKey}",
            StringComparer.OrdinalIgnoreCase);
        var gamepadLookup = GamepadDefinitions.ToDictionary(
            definition => $"{definition.Section}\0{definition.IniKey}",
            StringComparer.OrdinalIgnoreCase);

        var currentSection = string.Empty;
        foreach (var rawLine in File.ReadAllLines(ConfigPath, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var lookupKey = $"{currentSection}\0{line[..separator].Trim()}";
            var value = line[(separator + 1)..].Trim();
            if (currentSection.Equals("Display", StringComparison.OrdinalIgnoreCase) &&
                line[..separator].Trim().Equals("Character", StringComparison.OrdinalIgnoreCase))
            {
                var selectedCharacter = value.ToLowerInvariant();
                if (Characters.Any(option => option.Id == selectedCharacter))
                {
                    character = selectedCharacter;
                }
                continue;
            }
            var values = ParseValues(value);
            if (keyboardLookup.TryGetValue(lookupKey, out var keyboardDefinition))
            {
                keyboard[keyboardDefinition.Id] = values;
            }
            else if (gamepadLookup.TryGetValue(lookupKey, out var gamepadDefinition))
            {
                gamepad[gamepadDefinition.Id] = values;
            }
        }

        return new InputConfiguration(character, keyboard, gamepad);
    }

    public void Save(
        string character,
        IReadOnlyDictionary<string, IReadOnlyList<string>> keyboard,
        IReadOnlyDictionary<string, IReadOnlyList<string>> gamepad)
    {
        var selectedCharacter = Characters.Any(option => option.Id == character)
            ? character
            : DefaultCharacter;
        var builder = new StringBuilder();
        builder.AppendLine("; Arcade display settings");
        builder.AppendLine("; KeyboardEvent.code, ButtonN and AxisN+/- values may be comma-separated.");
        builder.AppendLine();
        builder.AppendLine("[Meta]");
        builder.AppendLine("Version=3");
        builder.AppendLine();
        builder.AppendLine("[Display]");
        builder.Append("Character=").AppendLine(selectedCharacter);

        WriteSections(builder, KeyboardDefinitions, keyboard);
        WriteSections(builder, GamepadDefinitions, gamepad);

        var directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = ConfigPath + ".tmp";
        File.WriteAllText(temporaryPath, builder.ToString(), new UTF8Encoding(false));
        File.Move(temporaryPath, ConfigPath, true);
    }

    private static IReadOnlyList<string> ParseValues(string rawValue) => rawValue
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static void WriteSections<TDefinition>(
        StringBuilder builder,
        IReadOnlyList<TDefinition> definitions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values)
        where TDefinition : notnull
    {
        var rows = definitions.Select(definition => definition switch
        {
            BindingDefinition keyboard => (keyboard.Id, keyboard.Section, keyboard.IniKey, keyboard.Defaults),
            GamepadBindingDefinition gamepad => (gamepad.Id, gamepad.Section, gamepad.IniKey, gamepad.Defaults),
            _ => throw new InvalidOperationException("Unknown binding definition."),
        });

        foreach (var section in rows.Select(row => row.Section).Distinct(StringComparer.Ordinal))
        {
            builder.AppendLine();
            builder.Append('[').Append(section).AppendLine("]");
            foreach (var row in rows.Where(row => row.Section == section))
            {
                var configured = values.TryGetValue(row.Id, out var selected) ? selected : row.Defaults;
                builder.Append(row.IniKey).Append('=').AppendLine(string.Join(',', configured));
            }
        }
    }

    public static string LocateConfigPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AngelisDisplay.exe")) ||
                File.Exists(Path.Combine(directory.FullName, "SWcat Display.exe")))
            {
                return Path.Combine(directory.FullName, "config.ini");
            }
            if (File.Exists(Path.Combine(directory.FullName, "obs_input_server.py")))
            {
                return Path.Combine(directory.FullName, "keybindings.ini");
            }
            directory = directory.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "keybindings.ini");
    }
}
