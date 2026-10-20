using Content.Shared.ActionBlocker;
using Content.Shared.Body;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Medical;
using Content.Shared.Verbs;

namespace Content.Server.Medical;

/// <summary>
/// Compressions provide circulation only while another player is actively performing the timed action.
/// Defibrillation treats a shockable rhythm, never missing organs or irreversible brain injury.
/// </summary>
public sealed class MedicalResuscitationSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private PhysiologySystem _physiology = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PhysiologyStateComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<PhysiologyComponent, MedicalCprDoAfterEvent>(OnComplete);
        SubscribeLocalEvent<PhysiologyComponent, DoAfterAttemptEvent<MedicalCprDoAfterEvent>>(OnAttempt);
        SubscribeLocalEvent<PhysiologyComponent, MedicalDefibrillationEvent>(OnDefibrillation);
    }

    private bool CanCompress(EntityUid patient, EntityUid user)
    {
        if (patient == user || TerminatingOrDeleted(patient) || TerminatingOrDeleted(user)
            || !HasComp<PhysiologyComponent>(patient) || !HasComp<PhysiologyStateComponent>(patient)
            || !_blocker.CanInteract(user, patient)
            || !_blocker.CanComplexInteract(user) || !_interaction.InRangeUnobstructed(user, patient)
            || _hands.GetActiveHand(user) == null || _hands.GetActiveItem(user) != null)
            return false;

        var heart = false;
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(patient))
            heart |= organ.Comp2.Kind == MedicalOrganKind.Heart && organ.Comp2.Trauma < 100f;
        return heart && HasViableBrain(patient);
    }

    private bool HasViableBrain(EntityUid patient)
    {
        foreach (var brain in _body.EnumerateOrgans<CerebralHealthComponent>(patient))
            return brain.Comp2.Damage < PhysiologyModel.MaximumBrainDamage;
        return false;
    }

    private void OnVerbs(Entity<PhysiologyStateComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract || !CanCompress(ent, args.User))
            return;
        var user = args.User;
        var patient = ent.Owner;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("medical-cpr"),
            Act = () =>
            {
                if (!CanCompress(patient, user))
                    return;
                var state = Comp<PhysiologyStateComponent>(patient);
                if (_doAfter.IsRunning(state.Compressions))
                    return;
                if (_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, 30f,
                    new MedicalCprDoAfterEvent(), patient, target: patient)
                {
                    NeedHand = true,
                    NeedFreeHand = true,
                    BreakOnMove = true,
                    BreakOnDamage = true,
                    AttemptFrequency = AttemptFrequency.EveryTick,
                }, out var id))
                {
                    state.Compressions = id;
                    _physiology.Record(state, "CPR started");
                }
            },
        });
    }

    public bool IsCompressing(EntityUid patient, PhysiologyStateComponent state)
    {
        return state.Compressions is { } id && _doAfter.IsRunning(id) && CanCompress(patient, id.Uid);
    }

    private void OnAttempt(Entity<PhysiologyComponent> ent, ref DoAfterAttemptEvent<MedicalCprDoAfterEvent> args)
    {
        if (!CanCompress(ent, args.DoAfter.Args.User))
            args.Cancel();
    }

    private void OnComplete(Entity<PhysiologyComponent> ent, ref MedicalCprDoAfterEvent args)
    {
        if (!TryComp<PhysiologyStateComponent>(ent, out var state) || state.Compressions != args.DoAfter.Id)
            return;
        state.Compressions = null;
        _physiology.Record(state, "CPR stopped");
        args.Handled = true;
    }

    private void OnDefibrillation(Entity<PhysiologyComponent> ent, ref MedicalDefibrillationEvent args)
    {
        args.Handled = true;
        if (!HasViableBrain(ent))
            return;
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(ent.Owner))
        {
            if (organ.Comp2.Kind != MedicalOrganKind.Heart || !organ.Comp2.Fibrillating || organ.Comp2.Trauma >= 100f)
                continue;
            organ.Comp2.Fibrillating = false;
            args.Successful = true;
        }
        if (args.Successful && TryComp<PhysiologyStateComponent>(ent, out var state))
            _physiology.Record(state, "DEFIB shockable rhythm corrected; underlying injury unchanged");
    }
}
