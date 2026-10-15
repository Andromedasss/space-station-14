using Content.Shared.Administration.Logs;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Medical;
using Content.Shared.Popups;
using Content.Shared.Stacks;

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

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<InternalTraumaTreatmentComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<PhysiologyComponent, InternalTraumaTreatmentDoAfterEvent>(OnComplete);
        SubscribeLocalEvent<PhysiologyComponent, DoAfterAttemptEvent<InternalTraumaTreatmentDoAfterEvent>>(OnAttempt);
    }

    private bool CanTreat(EntityUid patient, EntityUid user, EntityUid tool, out EntityUid table)
    {
        table = default;
        if (patient == user || TerminatingOrDeleted(patient) || TerminatingOrDeleted(tool)
            || !HasComp<PhysiologyComponent>(patient) || !HasComp<PhysiologyStateComponent>(patient)
            || !HasComp<BodyComponent>(patient) || !TryComp<BuckleComponent>(patient, out var buckle)
            || buckle.BuckledTo is not { } strapped || !HasComp<MedicalOperatingTableComponent>(strapped)
            || TerminatingOrDeleted(strapped)
            || !TryComp<InternalTraumaTreatmentComponent>(tool, out var treatment)
            || !float.IsFinite(treatment.Repair) || treatment.Repair <= 0f || treatment.Delay <= TimeSpan.Zero
            || !TryComp<StackComponent>(tool, out var stack) || stack.Count <= 0
            || !TryComp<BloodstreamComponent>(patient, out var blood) || blood.BleedAmount > 0f
            || !_interaction.InRangeUnobstructed(user, patient))
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
        var worst = 0f;
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(patient))
        {
            if (!float.IsFinite(organ.Comp2.Trauma) || organ.Comp2.Trauma <= worst)
                continue;
            selected = organ.Owner;
            worst = organ.Comp2.Trauma;
        }
        if (selected is not { } target)
            return;

        var ev = new InternalTraumaTreatmentDoAfterEvent(GetNetEntity(target), GetNetEntity(table));
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay,
            ev, patient, target: patient, used: ent)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            AttemptFrequency = AttemptFrequency.EveryTick,
        });
    }

    private bool Validate(EntityUid patient, EntityUid user, EntityUid? tool,
        InternalTraumaTreatmentDoAfterEvent ev)
    {
        return tool is { } used && CanTreat(patient, user, used, out var table)
            && table == GetEntity(ev.Table) && TryComp<OrganComponent>(GetEntity(ev.Organ), out var organ)
            && !TerminatingOrDeleted(GetEntity(ev.Organ)) && organ.Body == patient
            && TryComp<MedicalOrganComponent>(GetEntity(ev.Organ), out var medical)
            && float.IsFinite(medical.Trauma) && medical.Trauma > 0f;
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
        _stacks.ReduceCount(args.Used.Value, 1);
        args.Handled = true;
        _physiology.Record(Comp<PhysiologyStateComponent>(ent), $"TREAT {organ.Kind}: trauma -{repaired:F2}");
        _admin.Add(LogType.Healed, $"{ToPrettyString(args.User):user} repaired {repaired} internal trauma on {ToPrettyString(ent):target} organ {ToPrettyString(GetEntity(args.Organ))}");
        _popup.PopupEntity(Loc.GetString("medical-internal-treatment-complete"), ent, args.User);
    }
}
