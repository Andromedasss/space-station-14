using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Body;
using Content.Shared.Buckle.Components;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Medical;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Containers;

namespace Content.Server.Medical;

/// <summary>
/// Explicit experimental surgery verbs, with a physical access site and revalidated timed steps.
/// </summary>
public sealed class MedicalSurgerySystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private InitialBodySystem _initialBody = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private MedicalWoundSystem _wounds = default!;
    [Dependency] private PhysiologySystem _physiology = default!;
    [Dependency] private ISharedAdminLogManager _admin = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PhysiologyComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<PhysiologyComponent, MedicalSurgeryDoAfterEvent>(OnComplete);
        SubscribeLocalEvent<PhysiologyComponent, DoAfterAttemptEvent<MedicalSurgeryDoAfterEvent>>(OnAttempt);
    }

    private void OnVerbs(Entity<PhysiologyComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract || args.Using is not { } tool)
            return;
        var user = args.User;
        if (!CanOperate(ent, user, tool, out var table))
            return;

        foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>(ent.Owner))
        {
            if (TryComp<MedicalSurgeryToolComponent>(tool, out var surgical))
            {
                if (surgical.Action == MedicalSurgeryAction.Extract)
                {
                    foreach (var organ in _body.EnumerateOrgans<OrganComponent>(ent.Owner))
                    {
                        if (!TryComp<ChildOrganComponent>(organ.Owner, out var child) || child.Parent != part.Owner)
                            continue;
                        AddVerb(ent, user, tool, table, part, surgical.Action, organ.Owner, args.Verbs);
                    }
                }
                else
                    AddVerb(ent, user, tool, table, part, surgical.Action, null, args.Verbs);
            }
            else if (HasComp<OrganComponent>(tool))
                AddVerb(ent, user, tool, table, part, MedicalSurgeryAction.Insert, tool, args.Verbs);
        }
    }

    private void AddVerb(EntityUid patient, EntityUid user, EntityUid tool, EntityUid table,
        Entity<OrganComponent, MedicalWoundComponent> part, MedicalSurgeryAction action, EntityUid? organ,
        SortedSet<AlternativeVerb> verbs)
    {
        var ev = new MedicalSurgeryDoAfterEvent(GetNetEntity(part.Owner), GetNetEntity(table),
            organ is { } target ? GetNetEntity(target) : null, action, part.Comp2.SurgeryRevision);
        if (!Validate(patient, user, tool, ev))
            return;
        var label = Loc.GetString("medical-surgery-step", ("action", action.ToString()),
            ("part", part.Comp1.Category?.Id ?? "Unknown"),
            ("organ", organ is { } uid ? Comp<OrganComponent>(uid).Category?.Id ?? "Unknown" : ""));
        verbs.Add(new AlternativeVerb
        {
            Text = label,
            Act = () => Start(patient, user, tool, ev),
        });
    }

    private bool CanOperate(EntityUid patient, EntityUid user, EntityUid tool, out EntityUid table)
    {
        table = default;
        if (patient == user || TerminatingOrDeleted(patient) || TerminatingOrDeleted(user) || TerminatingOrDeleted(tool)
            || !HasComp<PhysiologyComponent>(patient) || !HasComp<PhysiologyStateComponent>(patient)
            || !TryComp<BuckleComponent>(patient, out var buckle) || buckle.BuckledTo is not { } strapped
            || !HasComp<MedicalOperatingTableComponent>(strapped) || TerminatingOrDeleted(strapped)
            || !_interaction.InRangeUnobstructed(user, patient) || !_blocker.CanInteract(user, patient)
            || !_blocker.CanComplexInteract(user) || !_hands.IsHolding(user, tool, out _))
            return false;
        table = strapped;
        return true;
    }

    private bool Validate(EntityUid patient, EntityUid user, EntityUid? tool, MedicalSurgeryDoAfterEvent ev)
    {
        if (tool is not { } used || !CanOperate(patient, user, used, out var table) || table != GetEntity(ev.Table)
            || !TryComp<OrganComponent>(GetEntity(ev.Part), out var site) || site.Body != patient
            || TerminatingOrDeleted(GetEntity(ev.Part)) || !TryComp<MedicalWoundComponent>(GetEntity(ev.Part), out var wound)
            || wound.SurgeryRevision != ev.Revision)
            return false;

        if (ev.Action != MedicalSurgeryAction.Insert
            && (!TryComp<MedicalSurgeryToolComponent>(used, out var surgical) || surgical.Action != ev.Action
                || surgical.Delay <= TimeSpan.Zero))
            return false;

        switch (ev.Action)
        {
            case MedicalSurgeryAction.Incise:
                return wound.SurgicalAccess == 0;
            case MedicalSurgeryAction.Clamp:
                return wound.SurgicalAccess == 1 || wound.SurgicalAccess > 1 && wound.Bleeding > 0f;
            case MedicalSurgeryAction.Retract:
                return wound.SurgicalAccess == 2 && wound.Bleeding <= 0f;
            case MedicalSurgeryAction.Close:
                return wound.SurgicalAccess > 0 && wound.Bleeding <= 0f;
            case MedicalSurgeryAction.Extract:
                return wound.SurgicalAccess == 3 && wound.Bleeding <= 0f && ev.Organ is { } extraction
                    && TryComp<OrganComponent>(GetEntity(extraction), out var organ) && organ.Body == patient
                    && !TerminatingOrDeleted(GetEntity(extraction))
                    && TryComp<ChildOrganComponent>(GetEntity(extraction), out var child) && child.Parent == GetEntity(ev.Part);
            case MedicalSurgeryAction.Insert:
                if (wound.SurgicalAccess != 3 || wound.Bleeding > 0f || ev.Organ != GetNetEntity(used)
                    || !TryComp<OrganComponent>(used, out var replacement) || replacement.Body != null
                    || site.Category is not { } category || replacement.Category is not { } slot
                    || !_initialBody.SupportsRelationship(patient, category, slot)
                    || !_containers.TryGetContainer(patient, BodyComponent.ContainerID, out var container))
                    return false;
                return _containers.CanInsert(used, container);
            default:
                return false;
        }
    }

    private void Start(EntityUid patient, EntityUid user, EntityUid tool, MedicalSurgeryDoAfterEvent ev)
    {
        if (!Validate(patient, user, tool, ev))
            return;
        var delay = TryComp<MedicalSurgeryToolComponent>(tool, out var surgical) ? surgical.Delay : TimeSpan.FromSeconds(8);
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, delay, ev, patient, target: patient, used: tool)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            AttemptFrequency = AttemptFrequency.EveryTick,
        });
    }

    private void OnAttempt(Entity<PhysiologyComponent> ent, ref DoAfterAttemptEvent<MedicalSurgeryDoAfterEvent> args)
    {
        if (!Validate(ent, args.DoAfter.Args.User, args.DoAfter.Args.Used, args.Event))
            args.Cancel();
    }

    private void OnComplete(Entity<PhysiologyComponent> ent, ref MedicalSurgeryDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || !Validate(ent, args.User, args.Used, args))
            return;
        var part = GetEntity(args.Part);
        var wound = Comp<MedicalWoundComponent>(part);
        switch (args.Action)
        {
            case MedicalSurgeryAction.Incise:
                _wounds.TryInjure(ent, Comp<OrganComponent>(part).Category!.Value.Id, "Slash", 5f);
                wound.SurgicalAccess = 1;
                break;
            case MedicalSurgeryAction.Clamp:
                _wounds.ClampSurgicalBleeding(ent, wound);
                wound.SurgicalAccess = Math.Max(2, wound.SurgicalAccess);
                break;
            case MedicalSurgeryAction.Retract:
                wound.SurgicalAccess = 3;
                break;
            case MedicalSurgeryAction.Close:
                wound.SurgicalAccess = 0;
                break;
            case MedicalSurgeryAction.Extract:
                var removed = GetEntity(args.Organ!.Value);
                if (!_containers.Remove(removed, _containers.GetContainer(ent, BodyComponent.ContainerID)))
                    return;
                // BodySystem/BrainSystem own the physiological and mind-transfer effects.
                break;
            case MedicalSurgeryAction.Insert:
                var replacement = args.Used!.Value;
                if (!_hands.TryDrop(args.User, replacement)
                    || !_containers.Insert(replacement, _containers.GetContainer(ent, BodyComponent.ContainerID)))
                    return;
                break;
        }
        wound.SurgeryRevision++;
        args.Handled = true;
        _physiology.Record(Comp<PhysiologyStateComponent>(ent), $"SURGERY {args.Action} {Comp<OrganComponent>(part).Category}");
        _admin.Add(LogType.Healed, $"{ToPrettyString(args.User):user} performed {args.Action} on {ToPrettyString(ent):target} at {ToPrettyString(part)}");
        _popup.PopupEntity(Loc.GetString("medical-surgery-complete", ("action", args.Action.ToString())), ent, args.User);
    }
}
