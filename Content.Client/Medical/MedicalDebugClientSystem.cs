using Content.Shared.Input;
using Content.Shared.Medical;
using Robust.Client.Console;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Console;
using Robust.Shared.Input.Binding;
using Robust.Shared.IoC;

namespace Content.Client.Medical;

/// <summary>
/// Client side of the medical debug window: registers the <c>/meddebug</c> command
/// and forwards requests to the server.
/// </summary>
public sealed class MedicalDebugClientSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IClientConsoleHost _consoleHost = default!;

    private MedicalDebugWindow? _window;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<MedicalDebugStateMessage>(OnState);

        // The test harness creates several client pairs per process, so the command
        // may already be registered by a previous instance of this system.
        if (!_consoleHost.AvailableCommands.ContainsKey("meddebug"))
        {
            _consoleHost.RegisterCommand("meddebug",
                "Open the medical debug window.",
                "Usage: /meddebug [entity id]",
                (shell, argStr, args) =>
                {
                    // Resolve the live system: the test harness creates several client
                    // pairs per process, so the first registered instance may be gone.
                    var live = IoCManager.Resolve<IEntitySystemManager>()
                        .GetEntitySystem<MedicalDebugClientSystem>();
                    live.HandleCommand(shell, args);
                });
        }

        // Quick access for admins: toggles the window on the local entity.
        CommandBinds.Builder.Bind(
                ContentKeyFunctions.OpenMedicalDebug,
                InputCmdHandler.FromDelegate(_ => ToggleLocal()))
            .Register<MedicalDebugClientSystem>();
    }

    private void ToggleLocal()
    {
        if (_window != null)
        {
            CloseWindow();
            return;
        }

        if (_players.LocalSession?.AttachedEntity is { } local)
            OpenForTarget(EntityManager.GetNetEntity(local));
    }

    private void HandleCommand(IConsoleShell shell, string[] args)
    {
        if (args.Length > 0)
        {
            if (int.TryParse(args[0].TrimStart('n', 'N'), out var id))
            {
                OpenForTarget(new NetEntity(id));
                return;
            }

            shell.WriteError("Invalid entity id.");
            return;
        }

        if (_players.LocalSession?.AttachedEntity is { } local)
        {
            OpenForTarget(EntityManager.GetNetEntity(local));
            return;
        }

        shell.WriteError("No local entity.");
    }

    private void OpenForTarget(NetEntity target)
    {
        CloseWindow();
        _window = new MedicalDebugWindow();
        _window.SetTarget(target);
        _window.OnRequest += OnRequest;
        _window.OpenCentered();
    }

    private void CloseWindow()
    {
        if (_window == null)
            return;
        _window.OnRequest -= OnRequest;
        _window.Close();
        _window.Dispose();
        _window = null;
    }

    private void OnRequest(MedicalDebugRequestMessage msg)
    {
        RaiseNetworkEvent(msg);
    }

    private void OnState(MedicalDebugStateMessage msg)
    {
        _window?.Populate(msg);
    }

    public override void Shutdown()
    {
        CloseWindow();
        base.Shutdown();
    }
}
