using System.Runtime.InteropServices;
using EnvKit.Core.Variables;

namespace EnvKit.Windows.Variables;

/// <summary>Sends WM_SETTINGCHANGE("Environment") to all top-level windows.</summary>
public sealed partial class SettingChangeBroadcaster : IEnvChangeBroadcaster
{
    private const nint HwndBroadcast = 0xFFFF;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    // Applies per window, so one hung window cannot stall the broadcast for long.
    private const uint TimeoutMilliseconds = 1000;

    public bool Broadcast() =>
        SendMessageTimeout(HwndBroadcast, WmSettingChange, 0, "Environment", SmtoAbortIfHung, TimeoutMilliseconds, out _) != 0;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeout(
        nint hWnd, uint msg, nuint wParam, string lParam, uint flags, uint timeout, out nuint result);
}
