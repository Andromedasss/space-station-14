using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Atmos;
using Content.Shared.Body;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Medical;
using Content.Shared.Popups;

namespace Content.Server.Medical;

/// <summary>One completed squeeze supplies one real breath. No persistent ventilation override.</summary>
public sealed class MedicalVentilationSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private RespiratorSystem _respirator = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MedicalOrganViabilitySystem _viability = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MedicalVentilationComponent, AfterInteractEvent>(OnUse);
        SubscribeLocalEvent<MedicalVentilationComponent, MedicalVentilationDoAfterEvent>(OnComplete);
        SubscribeLocalEvent<MedicalVentilationComponent, DoAfterAttemptEvent<MedicalVentilationDoAfterEvent>>(OnAttempt);
        SubscribeLocalEvent<MedicalVentilationComponent, ExaminedEvent>((uid, comp, args) =>
            args.PushMarkup(Loc.GetString("medical-gas-reservoir", ("moles", comp.GasSupply.TotalMoles.ToString("F2")))));
    }

    private bool CanVentilate(Entity<MedicalVentilationComponent> bag, EntityUid user, EntityUid patient)
    {
        if (user == patient || TerminatingOrDeleted(bag) || TerminatingOrDeleted(patient)
            || !HasComp<PhysiologyComponent>(patient) || !HasComp<PhysiologyStateComponent>(patient)
            || !HasComp<RespiratorComponent>(patient) || _hands.GetActiveItem(user) != bag.Owner
            || !_blocker.CanInteract(user, patient) || !_blocker.CanComplexInteract(user)
            || !_interaction.InRangeUnobstructed(user, patient) || bag.Comp.GasSupply.TotalMoles <= 0f)
            return false;
        var brain = false;
        var lungs = false;
        foreach (var organ in _body.EnumerateOrgans<CerebralHealthComponent>(patient))
            brain |= organ.Comp2.Damage < PhysiologyModel.MaximumBrainDamage;
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(patient))
            lungs |= organ.Comp2.Kind == MedicalOrganKind.Lungs && organ.Comp2.EffectiveFunction > 0f && _viability.Viable(organ.Owner);
        return brain && lungs;
    }

    private void OnUse(Entity<MedicalVentilationComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } patient || !args.CanReach || !CanVentilate(ent, args.User, patient))
            return;
        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, 2f,
            new MedicalVentilationDoAfterEvent(), ent, target: patient, used: ent)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            AttemptFrequency = AttemptFrequency.EveryTick,
        });
    }

    private void OnAttempt(Entity<MedicalVentilationComponent> ent, ref DoAfterAttemptEvent<MedicalVentilationDoAfterEvent> args)
    {
        if (args.DoAfter.Args.Target is not { } patient || !CanVentilate(ent, args.DoAfter.Args.User, patient))
            args.Cancel();
    }

    private void OnComplete(Entity<MedicalVentilationComponent> ent, ref MedicalVentilationDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } patient || !CanVentilate(ent, args.User, patient))
            return;
        args.Handled = true;
        var respirator = Comp<RespiratorComponent>(patient);
        // Empty the lungs through their normal exhalation path before supplying finite, regulated gas.
        _respirator.Exhale((patient, respirator));
        var gas = _atmos.RemoveVolumeAtPressure(ent.Comp.GasSupply, respirator.BreathVolume, Atmospherics.OneAtmosphere);
        gas.Volume = respirator.BreathVolume;
        var inhaled = new InhaledGasEvent(gas);
        RaiseLocalEvent(patient, ref inhaled);
        if (!inhaled.Succeeded)
            _atmos.Merge(ent.Comp.GasSupply, gas);
        _popup.PopupEntity(Loc.GetString(inhaled.Succeeded ? "medical-ventilation-breath" : "medical-ventilation-failed"), patient, args.User);
    }
}
