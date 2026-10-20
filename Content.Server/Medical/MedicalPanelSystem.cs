using System.Linq;
using Content.Shared.Body;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.ActionBlocker;
using Content.Shared.Medical;
using Robust.Shared.Player;

namespace Content.Server.Medical;

public sealed class MedicalPanelSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private MedicalSurgerySystem _surgery = default!;
    [Dependency] private InternalTraumaTreatmentSystem _repair = default!;

    private readonly Dictionary<EntityUid, (EntityUid Patient, int Request)> _open = new();

    public override void Update(float frameTime)
    {
        foreach (var (user, current) in _open.ToArray())
            if (!HasComp<ActorComponent>(user) || !Accessible(user, current.Patient))
                _open.Remove(user);
    }

    public override void Initialize()
    {
        SubscribeNetworkEvent<MedicalPanelRequestEvent>(OnRequest);
        SubscribeNetworkEvent<MedicalPanelCloseEvent>((ev, session) =>
        {
            if (session.SenderSession.AttachedEntity is { } user && _open.TryGetValue(user, out var current)
                && current.Request == ev.Request)
                _open.Remove(user);
        });
        SubscribeNetworkEvent<MedicalPanelSelectEvent>((ev, session) =>
        {
            if (session.SenderSession.AttachedEntity is not { } user || !TryPatient(user, ev.Request, out var patient))
                return;
            if (!_body.EnumerateOrgans<MedicalWoundComponent>(patient).Any(p => p.Comp1.Category?.Id == ev.Category))
                return;
            var selection = EnsureComp<MedicalTargetingComponent>(user);
            selection.Category = ev.Category;
            Dirty(user, selection);
        });
        SubscribeNetworkEvent<MedicalPanelOperateEvent>((ev, session) =>
        {
            if (session.SenderSession.AttachedEntity is not { } user || !TryPatient(user, ev.Request, out var patient)
                || _hands.GetActiveItem(user) is not { } tool || GetNetEntity(tool) != ev.Operation.Tool)
                return;
            if (ev.Operation.Surgery is { } surgery)
                _surgery.Start(patient, user, tool, surgery);
            else if (ev.Operation.Repair is { } repair)
                _repair.StartFromPanel(patient, user, tool, repair);
        });
    }

    private bool Accessible(EntityUid user, EntityUid patient) => !TerminatingOrDeleted(user)
        && !TerminatingOrDeleted(patient) && HasComp<PhysiologyComponent>(patient)
        && HasComp<PhysiologyStateComponent>(patient) && _interaction.InRangeAndAccessible(user, patient)
        && _blocker.CanInteract(user, patient);

    private bool TryPatient(EntityUid user, int request, out EntityUid patient)
    {
        patient = default;
        if (!_open.TryGetValue(user, out var current) || current.Request != request || !Accessible(user, current.Patient))
            return false;
        patient = current.Patient;
        return true;
    }

    private void OnRequest(MedicalPanelRequestEvent ev, EntitySessionEventArgs session)
    {
        if (session.SenderSession.AttachedEntity is not { } user)
            return;
        var state = new MedicalPanelStateEvent { Request = ev.Request, Patient = ev.Patient };
        if (!TryGetEntity(ev.Patient, out var patient) || !Accessible(user, patient.Value))
        {
            _open.Remove(user);
            RaiseNetworkEvent(state, session.SenderSession);
            return;
        }
        _open[user] = (patient.Value, ev.Request);
        state.Accessible = true;
        state.Name = Name(patient.Value);
        state.SelectedZone = TryComp<MedicalTargetingComponent>(user, out var selection) ? selection.Category : "Torso";
        foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>(patient.Value))
        {
            if (part.Comp1.Category is not { } category)
                continue;
            var organs = new List<string>();
            // Internal anatomy is visible only through an exposed access site.
            if (part.Comp2.InternalsExposed)
            {
                foreach (var organ in _body.EnumerateOrgans<OrganComponent>(patient.Value))
                    if (TryComp<ChildOrganComponent>(organ.Owner, out var child) && child.Parent == part.Owner)
                        organs.Add(organ.Comp1.Category?.Id ?? "Unknown");
            }
            state.Parts.Add(new MedicalPanelPart(category.Id, part.Comp2.SurgicalAccess, organs, part.Comp2.BoneAccess));
            if (category.Id != state.SelectedZone)
                continue;
            var tool = _hands.GetActiveItem(user);
            _surgery.AddPanelOperations(patient.Value, user, part.Owner, tool, state.Operations);
            if (part.Comp2.InternalsExposed)
                _repair.AddPanelOperations(patient.Value, user, part.Owner, tool, state.Operations);
        }
        RaiseNetworkEvent(state, session.SenderSession);
    }
}
