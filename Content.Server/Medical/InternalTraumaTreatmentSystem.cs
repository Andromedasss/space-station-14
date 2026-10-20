using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Medical;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Verbs;

namespace Content.Server.Medical;

public sealed class InternalTraumaTreatmentSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedStackSystem _stacks = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ISharedAdminLogManager _admin = default!;
    [Dependency] private PhysiologySystem _physiology = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private MedicalTargetingSystem _targeting = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<InternalTraumaTreatmentComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<InternalTraumaTreatmentComponent, GetVerbsEvent<UtilityVerb>>(OnVerbs);
        SubscribeLocalEvent<PhysiologyComponent, InternalTraumaTreatmentDoAfterEvent>(OnComplete);
        SubscribeLocalEvent<PhysiologyComponent, DoAfterAttemptEvent<InternalTraumaTreatmentDoAfterEvent>>(OnAttempt);
    }

    private bool CanTreat(EntityUid patient, EntityUid user, EntityUid tool, out EntityUid table)
    {
        table = default;
        if (patient == user || TerminatingOrDeleted(patient) || TerminatingOrDeleted(user) || TerminatingOrDeleted(tool)
            || !HasComp<PhysiologyComponent>(patient) || !HasComp<PhysiologyStateComponent>(patient)
            || !HasComp<BodyComponent>(patient) || !TryComp<BuckleComponent>(patient, out var buckle)
            || buckle.BuckledTo is not { } strapped || !HasComp<MedicalOperatingTableComponent>(strapped)
            || TerminatingOrDeleted(strapped)
            || !TryComp<InternalTraumaTreatmentComponent>(tool, out var treatment)
            || !float.IsFinite(treatment.Repair) || treatment.Repair <= 0f || treatment.Delay <= TimeSpan.Zero
            || !TryComp<StackComponent>(tool, out var stack) || stack.Count <= 0
            || !TryComp<BloodstreamComponent>(patient, out var blood) || blood.BleedAmount > 0f
            || !_interaction.InRangeUnobstructed(user, patient) || !_blocker.CanInteract(user, patient)
            || !_blocker.CanComplexInteract(user) || !_hands.IsHolding(user, tool, out _))
            return false;

        var hasBrain = false;
        foreach (var brain in _body.EnumerateOrgans<CerebralHealthComponent>(patient))
        {
            hasBrain = brain.Comp2.Damage < PhysiologyModel.MaximumBrainDamage;
            break;
        }
        table = strapped;
        return hasBrain;
    }

    private void OnInteract(Entity<InternalTraumaTreatmentComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } patient)
            return;
        if (!CanTreat(patient, args.User, ent, out var table))
        {
            _popup.PopupEntity(Loc.GetString("medical-internal-treatment-requirements"), ent, args.User);
            return;
        }

        EntityUid? selected = null;
        var site = _targeting.SelectedPart(args.User, patient);
        var worst = 0f;
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(patient))
        {
            if (!float.IsFinite(organ.Comp2.Trauma) || organ.Comp2.Trauma <= worst || !HasAccess(organ.Owner)
                || !TryComp<ChildOrganComponent>(organ.Owner, out var child) || child.Parent != site)
                continue;
            selected = organ.Owner;
            worst = organ.Comp2.Trauma;
        }
        if (selected is not { } target)
            return;

        args.Handled = Start(patient, args.User, ent, target, table);
    }

    private void OnVerbs(Entity<InternalTraumaTreatmentComponent> ent, ref GetVerbsEvent<UtilityVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract
            || !HasComp<PhysiologyComponent>(args.Target))
            return;

        var patient = args.Target;
        var user = args.User;
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(patient))
        {
            if (!float.IsFinite(organ.Comp2.Trauma) || organ.Comp2.Trauma <= 0f)
                continue;
            var target = organ.Owner;
            args.Verbs.Add(new UtilityVerb
            {
                Text = Loc.GetString("medical-internal-treatment-organ", ("organ", organ.Comp1.Category?.Id ?? "Unknown")),
                Disabled = !CanTreat(patient, user, ent, out _) || !HasAccess(target),
                Message = Loc.GetString("medical-internal-treatment-requirements"),
                Act = () =>
                {
                    if (CanTreat(patient, user, ent, out var table))
                        Start(patient, user, ent, target, table);
                },
            });
        }
    }

    public void AddPanelOperations(EntityUid patient, EntityUid user, EntityUid part, EntityUid? tool,
        List<MedicalPanelOperation> operations)
    {
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(patient))
        {
            if (!TryComp<ChildOrganComponent>(organ.Owner, out var child) || child.Parent != part)
                continue;
            var table = TryComp<BuckleComponent>(patient, out var buckle) ? buckle.BuckledTo : null;
            var wound = Comp<MedicalWoundComponent>(part);
            string? requirement;
            string Reason(string key) => Loc.GetString("medical-panel-require-" + key);
            if (patient == user)
                requirement = Reason("other");
            else if (table is not { } t || !HasComp<MedicalOperatingTableComponent>(t))
                requirement = Reason("table");
            else if (tool is not { } held || !HasComp<InternalTraumaTreatmentComponent>(held))
                requirement = Reason("repair-kit");
            else if (TryComp<BloodstreamComponent>(patient, out var blood) && blood.BleedAmount > 0f || wound.Bleeding > 0f)
                requirement = Reason("bleeding");
            else if (!wound.InternalsExposed)
                requirement = Reason("exposed");
            else if (!float.IsFinite(organ.Comp2.Trauma) || organ.Comp2.Trauma <= 0f)
                requirement = Reason("trauma");
            else if (!CanTreat(patient, user, tool.Value, out _))
                requirement = Reason("viable");
            else
                requirement = null;
            operations.Add(new MedicalPanelOperation
            {
                Label = Loc.GetString("medical-internal-treatment-organ", ("organ", organ.Comp1.Category?.Id ?? "Unknown")),
                Requirement = requirement,
                Tool = tool is { } used ? GetNetEntity(used) : default,
                Repair = new InternalTraumaTreatmentDoAfterEvent(GetNetEntity(organ.Owner),
                    table is { } strapped ? GetNetEntity(strapped) : default, GetNetEntity(part), wound.SurgeryRevision),
            });
        }
    }

    public bool Start(EntityUid patient, EntityUid user, EntityUid tool, EntityUid organ, EntityUid table)
    {
        if (!HasAccess(organ) || !TryComp<ChildOrganComponent>(organ, out var child) || child.Parent is not { } part)
            return false;
        var ev = new InternalTraumaTreatmentDoAfterEvent(GetNetEntity(organ), GetNetEntity(table),
            GetNetEntity(part), Comp<MedicalWoundComponent>(part).SurgeryRevision);
        if (!Validate(patient, user, tool, ev))
            return false;
        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, Comp<InternalTraumaTreatmentComponent>(tool).Delay,
            ev, patient, target: patient, used: tool)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            AttemptFrequency = AttemptFrequency.EveryTick,
        });
    }

    public void StartFromPanel(EntityUid patient, EntityUid user, EntityUid tool, InternalTraumaTreatmentDoAfterEvent ev)
    {
        if (Validate(patient, user, tool, ev))
            Start(patient, user, tool, GetEntity(ev.Organ), GetEntity(ev.Table));
    }

    private bool Validate(EntityUid patient, EntityUid user, EntityUid? tool,
        InternalTraumaTreatmentDoAfterEvent ev)
    {
        return tool is { } used && CanTreat(patient, user, used, out var table)
            && table == GetEntity(ev.Table) && TryComp<OrganComponent>(GetEntity(ev.Organ), out var organ)
            && !TerminatingOrDeleted(GetEntity(ev.Organ)) && organ.Body == patient
            && HasAccess(GetEntity(ev.Organ))
            && TryComp<ChildOrganComponent>(GetEntity(ev.Organ), out var child) && child.Parent == GetEntity(ev.Part)
            && TryComp<OrganComponent>(GetEntity(ev.Part), out var site) && site.Body == patient
            && TryComp<MedicalWoundComponent>(GetEntity(ev.Part), out var wound) && wound.SurgeryRevision == ev.Revision
            && TryComp<MedicalOrganComponent>(GetEntity(ev.Organ), out var medical)
            && float.IsFinite(medical.Trauma) && medical.Trauma > 0f;
    }

    private bool HasAccess(EntityUid organ)
    {
        return TryComp<ChildOrganComponent>(organ, out var child) && child.Parent is { } parent
            && !TerminatingOrDeleted(parent)
            && TryComp<MedicalWoundComponent>(parent, out var wound) && wound.InternalsExposed && wound.Bleeding <= 0f;
    }

    private void OnAttempt(Entity<PhysiologyComponent> ent, ref DoAfterAttemptEvent<InternalTraumaTreatmentDoAfterEvent> args)
    {
        if (!Validate(ent, args.DoAfter.Args.User, args.DoAfter.Args.Used, args.Event))
            args.Cancel();
    }

    private void OnComplete(Entity<PhysiologyComponent> ent, ref InternalTraumaTreatmentDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || !Validate(ent, args.User, args.Used, args))
            return;
        var organ = Comp<MedicalOrganComponent>(GetEntity(args.Organ));
        var repaired = Math.Min(organ.Trauma, Comp<InternalTraumaTreatmentComponent>(args.Used!.Value).Repair);
        organ.Trauma -= repaired;
        if (organ.Kind == MedicalOrganKind.Intestines && organ.Trauma < 60f)
            organ.Perforated = false;
        Comp<MedicalWoundComponent>(GetEntity(args.Part)).SurgeryRevision++;
        _stacks.ReduceCount(args.Used.Value, 1);
        args.Handled = true;
        _physiology.Record(Comp<PhysiologyStateComponent>(ent), $"TREAT {organ.Kind}: trauma -{repaired:F2}");
        _admin.Add(LogType.Healed, $"{ToPrettyString(args.User):user} repaired {repaired} internal trauma on {ToPrettyString(ent):target} organ {ToPrettyString(GetEntity(args.Organ))}");
        _popup.PopupEntity(Loc.GetString("medical-internal-treatment-complete"), ent, args.User);
    }
}
