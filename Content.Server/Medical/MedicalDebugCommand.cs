using System.Globalization;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;

namespace Content.Server.Medical;

[AdminCommand(AdminFlags.Debug)]
public sealed class MedicalDebugCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    public string Command => "medical";
    public string Description => "Inspect experimental physiology or apply logged test interventions.";
    public string Help => "medical <entity> [heart|lungs|kidneys|liver|intestines|blood|bleeding|temperature <value>]\n"
        + "organ functions/blood: 0..1; bleeding: 0..10; temperature: 250..350 K. Inspect with medical <entity>.\n"
        + "medical <entity> wound <part category, e.g. LegLeft> <Blunt|Slash|Piercing|Heat> <damage 0.01..1000>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is not (1 or 3 or 5) || !NetEntity.TryParse(args[0], out var net)
            || !_entities.TryGetEntity(net, out var uid))
        {
            shell.WriteError(Help);
            return;
        }

        var physiology = _entities.System<PhysiologySystem>();
        if (args.Length == 5)
        {
            if (args[1] != "wound"
                || !float.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
                || !_entities.System<MedicalWoundSystem>().TryInjure(uid.Value, args[2], args[3], amount))
            {
                shell.WriteError(Help);
                return;
            }
            _adminLog.Add(LogType.Action, LogImpact.High,
                $"{shell.Player?.Name ?? "Server console"} applied medical debug wound {args[2]} {args[3]}={amount} to {_entities.ToPrettyString(uid.Value):target}");
        }
        if (args.Length == 3)
        {
            if (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !physiology.TryIntervene(uid.Value, args[1], value))
            {
                shell.WriteError($"Invalid intervention or unsupported patient.\n{Help}");
                return;
            }
            _adminLog.Add(LogType.Action, LogImpact.High,
                $"{shell.Player?.Name ?? "Server console"} applied medical debug intervention {args[1]}={value} to {_entities.ToPrettyString(uid.Value):target}");
        }
        shell.WriteLine(physiology.Inspect(uid.Value));
    }
}
