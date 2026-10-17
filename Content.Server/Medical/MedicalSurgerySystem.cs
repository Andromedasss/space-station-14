using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Body;
using Content.Shared.Buckle.Components;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Medical;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Examine;
using Content.Server.Power.EntitySystems;
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
    [Dependency] private MedicalOrganViabilitySystem _viability = default!;
    [Dependency] private ISharedAdminLogManager _admin = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MedicalTargetingSystem _targeting = default!;
    [Dependency] private MedicalNeurologySystem _neurology = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PhysiologyComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<PhysiologyComponent, MedicalSurgeryDoAfterEvent>(OnComplete);
        SubscribeLocalEvent<PhysiologyComponent, DoAfterAttemptEvent<MedicalSurgeryDoAfterEvent>>(OnAttempt);
        SubscribeLocalEvent<MedicalSurgeryToolComponent, AfterInteractEvent>(OnToolInteract);
        SubscribeLocalEvent<OrganComponent, AfterInteractEvent>(OnOrganInteract);
        SubscribeLocalEvent<MedicalSterilizerComponent, InteractUsingEvent>(OnSterilize);
        SubscribeLocalEvent<MedicalSurgeryToolComponent, ExaminedEvent>(OnToolExamined);
        SubscribeLocalEvent<MedicalSurgeryToolComponent, DroppedEvent>(OnToolDropped);
    }

    private void OnToolDropped(Entity<MedicalSurgeryToolComponent> ent, ref DroppedEvent args)
    {
        ent.Comp.Sterile = false;
    }

    private void OnToolExamined(Entity<MedicalSurgeryToolComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.Sterile ? "medical-tool-sterile" : "medical-tool-contaminated"));
    }

    private void OnSterilize(Entity<MedicalSterilizerComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !this.IsPowered(ent.Owner, EntityManager)
            || !_interaction.InRangeUnobstructed(args.User, ent.Owner) || !_hands.IsHolding(args.User, args.Used, out _)
            || !TryComp<MedicalSurgeryToolComponent>(args.Used, out var tool))
            return;
        tool.Sterile = true;
        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("medical-tool-sterilized"), ent, args.User);
    }

    private void OnToolInteract(Entity<MedicalSurgeryToolComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } patient
            || !CanOperate(patient, args.User, ent, out var table)
            || _targeting.SelectedPart(args.User, patient) is not { } part
            || !TryComp<MedicalWoundComponent>(part, out var wound))
            return;

        var action = ent.Comp.Action;
        if (action == MedicalSurgeryAction.Retract)
            action = wound.BoneAccess == 2 ? MedicalSurgeryAction.ApproximateBone
                : wound.RequiresBoneAccess && wound.BoneAccess == 1 ? MedicalSurgeryAction.SpreadBone
                : wound.SurgicalAccess == 3 ? MedicalSurgeryAction.Approximate : MedicalSurgeryAction.Retract;
        if (action == MedicalSurgeryAction.RepairFracture && wound.BoneAccess == 1)
            action = MedicalSurgeryAction.RepairBone;

        // Extraction of several exposed organs is deliberately explicit in the debug panel.
        EntityUid? selected = null;
        if (action is MedicalSurgeryAction.Extract or MedicalSurgeryAction.RehabilitateNeurology)
        {
            foreach (var organ in _body.EnumerateOrgans<OrganComponent>(patient))
            {
                if (!TryComp<ChildOrganComponent>(organ.Owner, out var child) || child.Parent != part)
                    continue;
                if (action == MedicalSurgeryAction.RehabilitateNeurology && !HasComp<CerebralHealthComponent>(organ.Owner))
                    continue;
                if (selected != null)
                {
                    _popup.PopupEntity(Loc.GetString("medical-surgery-select-organ"), patient, args.User);
                    args.Handled = true;
                    return;
                }
                selected = organ.Owner;
            }
        }
        var ev = new MedicalSurgeryDoAfterEvent(GetNetEntity(part), GetNetEntity(table),
            selected is { } uid ? GetNetEntity(uid) : null, action, wound.SurgeryRevision);
        args.Handled = true;
        if (Validate(patient, args.User, ent, ev))
            Start(patient, args.User, ent, ev);
        else
            _popup.PopupEntity(PanelRequirement(patient, args.User, ent, ev) ?? Loc.GetString("medical-panel-require-changed"), patient, args.User);
    }

    private void OnOrganInteract(Entity<OrganComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } patient
            || !CanOperate(patient, args.User, ent, out var table)
            || _targeting.SelectedPart(args.User, patient) is not { } part
            || !TryComp<MedicalWoundComponent>(part, out var wound))
            return;
        var ev = new MedicalSurgeryDoAfterEvent(GetNetEntity(part), GetNetEntity(table), GetNetEntity(ent.Owner),
            MedicalSurgeryAction.Insert, wound.SurgeryRevision);
        args.Handled = true;
        Start(patient, args.User, ent, ev);
    }

    private static bool ToolSupports(MedicalSurgeryToolComponent tool, MedicalSurgeryAction action)
        => tool.Action == action || tool.Action == MedicalSurgeryAction.Retract
            && action is MedicalSurgeryAction.Approximate or MedicalSurgeryAction.SpreadBone or MedicalSurgeryAction.ApproximateBone
            || tool.Action == MedicalSurgeryAction.RepairFracture && action == MedicalSurgeryAction.RepairBone;

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
                if (surgical.Action is MedicalSurgeryAction.Extract or MedicalSurgeryAction.RehabilitateNeurology)
                {
                    foreach (var organ in _body.EnumerateOrgans<OrganComponent>(ent.Owner))
                    {
                        if (!TryComp<ChildOrganComponent>(organ.Owner, out var child) || child.Parent != part.Owner)
                            continue;
                        if (surgical.Action == MedicalSurgeryAction.RehabilitateNeurology && !HasComp<CerebralHealthComponent>(organ.Owner))
                            continue;
                        AddVerb(ent, user, tool, table, part, surgical.Action, organ.Owner, args.Verbs);
                    }
                }
                else
                {
                    foreach (var action in Enum.GetValues<MedicalSurgeryAction>())
                        if (ToolSupports(surgical, action))
                            AddVerb(ent, user, tool, table, part, action, null, args.Verbs);
                }
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
            && (!TryComp<MedicalSurgeryToolComponent>(used, out var surgical) || !ToolSupports(surgical, ev.Action)
                || surgical.Delay <= TimeSpan.Zero))
            return false;

        switch (ev.Action)
        {
            case MedicalSurgeryAction.Disinfect:
                return !wound.Prosthetic;
            case MedicalSurgeryAction.Incise:
                return wound.SurgicalAccess == 0;
            case MedicalSurgeryAction.Clamp:
                return wound.SurgicalAccess == 1 || wound.SurgicalAccess > 1 && wound.Bleeding > 0f;
            case MedicalSurgeryAction.Retract:
                return wound.SurgicalAccess == 2 && wound.Bleeding <= 0f;
            case MedicalSurgeryAction.Close:
                return wound.SurgicalAccess is 1 or 2 && wound.BoneAccess == 0 && wound.Bleeding <= 0f;
            case MedicalSurgeryAction.Approximate:
                return wound.SurgicalAccess == 3 && wound.BoneAccess == 0 && wound.Bleeding <= 0f;
            case MedicalSurgeryAction.SawBone:
                return wound.RequiresBoneAccess && wound.SurgicalAccess == 3 && wound.BoneAccess == 0 && wound.Bleeding <= 0f;
            case MedicalSurgeryAction.SpreadBone:
                return wound.SurgicalAccess == 3 && wound.BoneAccess == 1 && wound.Bleeding <= 0f;
            case MedicalSurgeryAction.ApproximateBone:
                return wound.SurgicalAccess == 3 && wound.BoneAccess == 2 && wound.Bleeding <= 0f;
            case MedicalSurgeryAction.RepairBone:
                return wound.SurgicalAccess == 3 && wound.BoneAccess == 1 && wound.Bleeding <= 0f;
            case MedicalSurgeryAction.RepairFracture:
                return wound.SurgicalAccess == 3 && wound.Bleeding <= 0f && wound.Fractured;
            case MedicalSurgeryAction.RepairVessel:
                return wound.SurgicalAccess == 3 && wound.Bleeding <= 0f && wound.VascularDamage > 0f;
            case MedicalSurgeryAction.RepairProsthetic:
                return wound.Prosthetic && wound.Injury.GetTotal() > 0;
            case MedicalSurgeryAction.RehabilitateNeurology:
                return wound.InternalsExposed && wound.Bleeding <= 0f && site.Category?.Id == "Head"
                    && ev.Organ is { } brainEntity && !TerminatingOrDeleted(GetEntity(brainEntity))
                    && TryComp<OrganComponent>(GetEntity(brainEntity), out var brainOrgan) && brainOrgan.Body == patient
                    && TryComp<ChildOrganComponent>(GetEntity(brainEntity), out var brainChild) && brainChild.Parent == GetEntity(ev.Part)
                    && TryComp<CerebralHealthComponent>(GetEntity(brainEntity), out var brain)
                    && brain.Damage < PhysiologyModel.MaximumBrainDamage && brain.OxygenDebt < PhysiologyModel.UnconsciousDebt
                    && _neurology.HasPersistentCondition(brain)
                    && _neurology.Dose(patient, "MedicalTestNeurorestorative") >= NeurologicalTrauma.TherapeuticDose
                    && TryComp<PhysiologyStateComponent>(patient, out var state)
                    && state.OxygenDelivery / state.MetabolicDemand >= PhysiologyModel.AdequateDelivery;
            case MedicalSurgeryAction.Extract:
                return wound.InternalsExposed && wound.Bleeding <= 0f && ev.Organ is { } extraction
                    && TryComp<OrganComponent>(GetEntity(extraction), out var organ) && organ.Body == patient
                    && !TerminatingOrDeleted(GetEntity(extraction))
                    && TryComp<ChildOrganComponent>(GetEntity(extraction), out var child) && child.Parent == GetEntity(ev.Part);
            case MedicalSurgeryAction.Insert:
                if (!wound.InternalsExposed || wound.Bleeding > 0f || ev.Organ != GetNetEntity(used)
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

    public void AddPanelOperations(EntityUid patient, EntityUid user, EntityUid part, EntityUid? tool,
        List<MedicalPanelOperation> operations)
    {
        if (!TryComp<MedicalWoundComponent>(part, out var wound))
            return;
        foreach (var action in Enum.GetValues<MedicalSurgeryAction>())
        {
            if (action is MedicalSurgeryAction.Extract or MedicalSurgeryAction.RehabilitateNeurology)
            {
                if (!wound.InternalsExposed)
                    continue;
                foreach (var organ in _body.EnumerateOrgans<OrganComponent>(patient))
                {
                    if (action == MedicalSurgeryAction.RehabilitateNeurology && !HasComp<CerebralHealthComponent>(organ.Owner))
                        continue;
                    if (TryComp<ChildOrganComponent>(organ.Owner, out var child) && child.Parent == part)
                        Add(action, organ.Owner);
                }
            }
            else
                Add(action, action == MedicalSurgeryAction.Insert ? tool : null);
        }

        void Add(MedicalSurgeryAction action, EntityUid? organ)
        {
            var table = TryComp<BuckleComponent>(patient, out var buckle) ? buckle.BuckledTo : null;
            var ev = new MedicalSurgeryDoAfterEvent(GetNetEntity(part), table is { } t ? GetNetEntity(t) : default,
                organ is { } o ? GetNetEntity(o) : null, action, wound.SurgeryRevision);
            operations.Add(new MedicalPanelOperation
            {
                Label = Loc.GetString("medical-surgery-step", ("action", action.ToString()),
                    ("part", Comp<OrganComponent>(part).Category?.Id ?? "Unknown"),
                    ("organ", organ is { } uid && TryComp<OrganComponent>(uid, out var component) ? component.Category?.Id ?? "Unknown" : "")),
                Tool = tool is { } used ? GetNetEntity(used) : default,
                Surgery = ev,
                Requirement = PanelRequirement(patient, user, tool, ev),
            });
        }
    }

    private string? PanelRequirement(EntityUid patient, EntityUid user, EntityUid? tool, MedicalSurgeryDoAfterEvent ev)
    {
        string Reason(string key) => Loc.GetString("medical-panel-require-" + key);
        if (patient == user)
            return Reason("other");
        if (!TryComp<BuckleComponent>(patient, out var buckle) || buckle.BuckledTo is not { } table
            || !HasComp<MedicalOperatingTableComponent>(table))
            return Reason("table");
        if (tool is not { } used || !_hands.IsHolding(user, used, out _))
            return Reason("tool");
        if (ev.Action == MedicalSurgeryAction.Insert)
        {
            if (!HasComp<OrganComponent>(used))
                return Reason("replacement");
        }
        else if (!TryComp<MedicalSurgeryToolComponent>(used, out var surgical) || !ToolSupports(surgical, ev.Action))
            return Loc.GetString("medical-panel-require-instrument", ("action", ev.Action.ToString()));
        if (!CanOperate(patient, user, used, out _))
            return Reason("interact");
        if (Validate(patient, user, used, ev))
            return null;
        if (ev.Action == MedicalSurgeryAction.RehabilitateNeurology)
            return Loc.GetString("medical-neurology-surgery-requirements");
        var wound = Comp<MedicalWoundComponent>(GetEntity(ev.Part));
        if (ev.Action == MedicalSurgeryAction.RepairProsthetic)
            return Reason("prosthetic");
        if (ev.Action == MedicalSurgeryAction.Incise)
            return Reason("closed");
        if (ev.Action == MedicalSurgeryAction.Clamp)
            return Reason("incised");
        if (wound.Bleeding > 0f)
            return Reason("bleeding");
        if (ev.Action is MedicalSurgeryAction.SawBone or MedicalSurgeryAction.SpreadBone
            or MedicalSurgeryAction.ApproximateBone or MedicalSurgeryAction.RepairBone or MedicalSurgeryAction.Approximate
            || ev.Action == MedicalSurgeryAction.Close && (wound.SurgicalAccess == 3 || wound.BoneAccess != 0))
            return Reason("layers");
        if (ev.Action == MedicalSurgeryAction.Retract)
            return Reason("clamped");
        if (ev.Action == MedicalSurgeryAction.Close)
            return Reason("open");
        if (wound.SurgicalAccess != 3)
            return Reason("exposed");
        if (ev.Action == MedicalSurgeryAction.Insert && tool is { } replacement && !_viability.Viable(replacement))
            return Reason("nonviable");
        return Reason(ev.Action switch
        {
            MedicalSurgeryAction.RepairFracture => "fracture",
            MedicalSurgeryAction.RepairVessel => "vessel",
            MedicalSurgeryAction.Insert => "compatible",
            _ => "changed",
        });
    }

    public void Start(EntityUid patient, EntityUid user, EntityUid tool, MedicalSurgeryDoAfterEvent ev)
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
            case MedicalSurgeryAction.Disinfect:
                wound.Contamination = 0f;
                wound.AntisepticSeconds = 120f;
                break;
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
            case MedicalSurgeryAction.Approximate:
                wound.SurgicalAccess = 2;
                break;
            case MedicalSurgeryAction.SawBone:
                wound.BoneAccess = 1;
                break;
            case MedicalSurgeryAction.SpreadBone:
                wound.BoneAccess = 2;
                break;
            case MedicalSurgeryAction.ApproximateBone:
                wound.BoneAccess = 1;
                break;
            case MedicalSurgeryAction.RepairBone:
                wound.BoneAccess = 0;
                break;
            case MedicalSurgeryAction.RepairFracture:
                _wounds.RepairFracture(ent, wound);
                break;
            case MedicalSurgeryAction.RepairVessel:
                _wounds.RepairVessel(wound);
                break;
            case MedicalSurgeryAction.RepairProsthetic:
                _wounds.RepairProsthetic(ent, wound);
                break;
            case MedicalSurgeryAction.RehabilitateNeurology:
                if (!_neurology.Rehabilitate(Comp<CerebralHealthComponent>(GetEntity(args.Organ!.Value))))
                    return;
                _neurology.ConsumeTreatment(ent);
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
        if (args.Action != MedicalSurgeryAction.Disinfect && !wound.Prosthetic && args.Used is { } instrument)
        {
            var sterile = TryComp<MedicalSurgeryToolComponent>(instrument, out var tool) && tool.Sterile;
            if (!sterile)
                wound.Contamination = Math.Clamp(wound.Contamination + 15f, 0f, 100f);
            if (tool != null)
                tool.Sterile = false;
        }
        wound.SurgeryRevision++;
        args.Handled = true;
        _physiology.Record(Comp<PhysiologyStateComponent>(ent), $"SURGERY {args.Action} {Comp<OrganComponent>(part).Category}");
        _admin.Add(LogType.Healed, $"{ToPrettyString(args.User):user} performed {args.Action} on {ToPrettyString(ent):target} at {ToPrettyString(part)}");
        _popup.PopupEntity(Loc.GetString("medical-surgery-complete", ("action", args.Action.ToString())), ent, args.User);
    }
}
