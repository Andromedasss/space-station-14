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
    public string Help => "medical <entity> [heart|lungs|blood|bleeding <value>]\n"
        + "heart/lungs/blood: 0..1; bleeding: 0..10 (default patient). Inspect with medical <entity>.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is not (1 or 3) || !NetEntity.TryParse(args[0], out var net)
            || !_entities.TryGetEntity(net, out var uid))
        {
            shell.WriteError(Help);
            return;
        }

        var physiology = _entities.System<PhysiologySystem>();
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
