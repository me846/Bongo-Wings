using System.Runtime.InteropServices;

namespace ArcadeLeverKeyConfig;

public static class GamepadControlNames
{
    private static readonly string[] ButtonNames =
    [
        "A", "B", "X", "Y", "LB", "RB", "LT", "RT",
        "ビュー / Back", "メニュー / Start", "L3", "R3",
        "十字 上", "十字 下", "十字 左", "十字 右",
    ];

    public static string Display(string control)
    {
        if (control.StartsWith("Button", StringComparison.Ordinal) &&
            int.TryParse(control[6..], out var buttonIndex))
        {
            return buttonIndex >= 0 && buttonIndex < ButtonNames.Length
                ? $"{ButtonNames[buttonIndex]}  [B{buttonIndex}]"
                : $"ボタン {buttonIndex}";
        }

        if (control.StartsWith("Axis", StringComparison.Ordinal) && control.Length >= 6 &&
            int.TryParse(control[4..^1], out var axisIndex))
        {
            var direction = control[^1] == '-' ? "－" : "＋";
            var axisName = axisIndex switch
            {
                0 => "左スティック X",
                1 => "左スティック Y",
                2 => "右スティック X",
                3 => "右スティック Y",
                _ => $"軸 {axisIndex}",
            };
            return $"{axisName} {direction}";
        }

        return control;
    }
}

public sealed record GamepadSnapshot(
    bool Connected,
    int UserIndex,
    IReadOnlyList<double> Axes,
    IReadOnlyList<double> Buttons)
{
    public string DisplayName => Connected ? $"XInput Controller {UserIndex + 1}" : "未接続";

    public IReadOnlyList<string> ActiveControls(double threshold = 0.6)
    {
        var active = new List<string>();
        for (var index = 0; index < Buttons.Count; index += 1)
        {
            if (Buttons[index] >= threshold)
            {
                active.Add($"Button{index}");
            }
        }
        for (var index = 0; index < Axes.Count; index += 1)
        {
            if (Axes[index] <= -threshold)
            {
                active.Add($"Axis{index}-");
            }
            else if (Axes[index] >= threshold)
            {
                active.Add($"Axis{index}+");
            }
        }
        return active;
    }
}

public sealed class XInputReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint XInputGetStateDelegate(uint userIndex, out XInputState state);

    private readonly XInputGetStateDelegate? _getState;
    private readonly nint _libraryHandle;

    public XInputReader()
    {
        foreach (var libraryName in new[] { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" })
        {
            if (!NativeLibrary.TryLoad(libraryName, out _libraryHandle))
            {
                continue;
            }

            if (NativeLibrary.TryGetExport(_libraryHandle, "XInputGetState", out var address))
            {
                _getState = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(address);
                break;
            }

            NativeLibrary.Free(_libraryHandle);
            _libraryHandle = nint.Zero;
        }
    }

    public GamepadSnapshot Read()
    {
        if (_getState is null)
        {
            return Disconnected();
        }

        for (uint userIndex = 0; userIndex < 4; userIndex += 1)
        {
            if (_getState(userIndex, out var state) != 0)
            {
                continue;
            }

            var pad = state.Gamepad;
            var masks = new ushort[]
            {
                0x1000, 0x2000, 0x4000, 0x8000,
                0x0100, 0x0200,
            };
            var buttons = masks
                .Select(mask => (pad.Buttons & mask) != 0 ? 1.0 : 0.0)
                .ToList();
            buttons.Add(pad.LeftTrigger / 255.0);
            buttons.Add(pad.RightTrigger / 255.0);
            buttons.Add((pad.Buttons & 0x0020) != 0 ? 1.0 : 0.0);
            buttons.Add((pad.Buttons & 0x0010) != 0 ? 1.0 : 0.0);
            buttons.Add((pad.Buttons & 0x0040) != 0 ? 1.0 : 0.0);
            buttons.Add((pad.Buttons & 0x0080) != 0 ? 1.0 : 0.0);
            buttons.Add((pad.Buttons & 0x0001) != 0 ? 1.0 : 0.0);
            buttons.Add((pad.Buttons & 0x0002) != 0 ? 1.0 : 0.0);
            buttons.Add((pad.Buttons & 0x0004) != 0 ? 1.0 : 0.0);
            buttons.Add((pad.Buttons & 0x0008) != 0 ? 1.0 : 0.0);

            var axes = new[]
            {
                NormalizeAxis(pad.ThumbLX),
                -NormalizeAxis(pad.ThumbLY),
                NormalizeAxis(pad.ThumbRX),
                -NormalizeAxis(pad.ThumbRY),
            };
            return new GamepadSnapshot(true, (int)userIndex, axes, buttons);
        }

        return Disconnected();
    }

    private static GamepadSnapshot Disconnected() => new(false, -1, [], []);

    private static double NormalizeAxis(short value)
    {
        var divisor = value >= 0 ? 32767.0 : 32768.0;
        return Math.Clamp(value / divisor, -1.0, 1.0);
    }
}
