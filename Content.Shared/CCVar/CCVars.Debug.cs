using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    /// Bypass station power requirements for local playtesting. Does not override device power switches.
    /// </summary>
    public static readonly CVarDef<bool> DebugStationPower =
        CVarDef.Create("debug.station_power", false, CVar.SERVERONLY);

    /// <summary>
    /// Component to be inspected using the "Quick Inspect Component" keybind.
    /// Set by the "quickinspect" command.
    /// </summary>
    public static readonly CVarDef<string> DebugQuickInspect =
        CVarDef.Create("debug.quick_inspect", "", CVar.CLIENTONLY | CVar.ARCHIVE);
}
