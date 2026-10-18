using Content.Shared.Administration;
using Content.Shared.Medical;
using Robust.Client.Console;
using Robust.Client.Player;
using Robust.Shared.Network;

namespace Content.Client.Medical;

/// <summary>
/// Client-side system for the medical debug window.
/// Provides admin command to open the window and handles network communication.
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

        _consoleHost.RegisterCommand("meddebug",
            Loc.GetString("medical-debug-command-desc"),
            Loc.GetString("medical-debug-command-help"),
            (shell, argStr, cmdArgs) =>
            {
                if (cmdArgs.Length == 0)
                {
                    OpenOrClose();
                    return;
                }

                if (int.TryParse(cmdArgs[0], out var id))
                {
                    OpenForTarget(new NetEntity(id));
                    return;
                }
            });
    }

    private void OpenOrClose()
    {
        if (_window != null)
        {
            CloseWindow();
            return;
        }

        if (_players.LocalSession?.AttachedEntity is not { } local)
        {
            return;
        }

        OpenForTarget(EntityManager.GetNetEntity(local));
    }

    private void OpenForTarget(NetEntity target)
    {
        CloseWindow();
        _window = new MedicalDebugWindow();
        _window.SetTarget(target);
        _window.OnRequest += OnRequest;
        _window.OnClose += () => CloseWindow();
        _window.OpenCentered();
    }

    private void CloseWindow()
    {
        _window?.Close();
        _window?.Dispose();
        _window = null;
    }

    private void OnRequest(MedicalDebugRequestMessage msg)
    {
        RaiseNetworkEvent(msg);
    }

    private void OnState(MedicalDebugStateMessage msg)
    {
        _window?.Populate(msg.Physiology, msg.Temperature, msg.BloodLevel, msg.Error);
    }

    public override void Shutdown()
    {
        CloseWindow();
        base.Shutdown();
    }
}
