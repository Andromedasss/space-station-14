using System.Linq;
using Content.Shared.Input;
using Content.Shared.Medical;
using Content.Shared.Verbs;
using Robust.Client.Player;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;

namespace Content.Client.Medical;

public sealed class MedicalPanelSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    private MedicalPanelWindow? _window;
    private EntityUid? _patient;
    private int _request;
    private float _refresh;

    public void SelectHudZone(string category)
    {
        if (_players.LocalEntity != null && MedicalTargetingSystem.IsTargetZone(category))
            RaiseNetworkEvent(new MedicalTargetZoneEvent(category));
    }

    public override void Initialize()
    {
        UpdatesOutsidePrediction = true;
        SubscribeNetworkEvent<MedicalPanelStateEvent>(OnState);
        SubscribeLocalEvent<PhysiologyComponent, GetVerbsEvent<Verb>>(OnVerbs);
        CommandBinds.Builder.Bind(ContentKeyFunctions.OpenMedicalPanel,
            new PointerInputCmdHandler(OnKey, outsidePrediction: true)).Register<MedicalPanelSystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<MedicalPanelSystem>();
        _window?.Dispose();
        base.Shutdown();
    }

    private bool OnKey(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (args.State != BoundKeyState.Down)
            return false;
        var patient = args.EntityUid;
        if (!HasComp<PhysiologyComponent>(patient))
        {
            var xform = EntityManager.System<Robust.Client.GameObjects.TransformSystem>();
            var coordinates = xform.ToMapCoordinates(args.Coordinates);
            if (!EntityManager.System<Content.Client.Verbs.VerbSystem>().TryGetEntityMenuEntities(coordinates, out var entities))
                return false;
            patient = entities.FirstOrDefault(HasComp<PhysiologyComponent>);
        }
        if (!HasComp<PhysiologyComponent>(patient))
            return false;
        Open(patient);
        return true;
    }

    private void OnVerbs(Entity<PhysiologyComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;
        var patient = ent.Owner;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("medical-panel-open"),
            ClientExclusive = true,
            Act = () => Open(patient),
        });
    }

    public void Open(EntityUid patient)
    {
        _window?.Close();
        _patient = patient;
        var request = ++_request;
        var window = new MedicalPanelWindow();
        _window = window;
        window.OnClose += () =>
        {
            RaiseNetworkEvent(new MedicalPanelCloseEvent(request));
            window.Dispose();
            if (_window != window)
                return;
            _patient = null;
            _window = null;
        };
        window.OnOperate += operation =>
        {
            RaiseNetworkEvent(new MedicalPanelOperateEvent(request, operation));
            _refresh = 0f;
        };
        window.OpenCentered();
        Refresh();
    }

    private void Refresh()
    {
        if (_patient is not { } patient || _players.LocalEntity == null || TerminatingOrDeleted(patient))
        {
            _window?.Close();
            return;
        }
        RaiseNetworkEvent(new MedicalPanelRequestEvent(GetNetEntity(patient), _request));
        _refresh = 0.5f;
    }

    public override void Update(float frameTime)
    {
        if (_window == null)
            return;
        _refresh -= frameTime;
        if (_refresh <= 0f)
            Refresh();
    }

    private void OnState(MedicalPanelStateEvent state)
    {
        if (_window == null || state.Request != _request || _patient is not { } patient || GetNetEntity(patient) != state.Patient)
            return;
        _window.Populate(state);
    }
}
