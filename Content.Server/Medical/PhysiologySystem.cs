using System.Text;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.Timing;

namespace Content.Server.Medical;

/// <summary>
/// First experimental physiology slice. Only explicitly opted-in patients are processed.
/// Existing blood and breathing remain the inputs; this system alone selects their mob state.
/// </summary>
public sealed class PhysiologySystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private BloodstreamSystem _blood = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(RespiratorSystem));
        UpdatesAfter.Add(typeof(BloodstreamSystem));
        SubscribeLocalEvent<PhysiologyComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<CerebralHealthComponent, RejuvenateEvent>(OnBrainRejuvenate);
        SubscribeLocalEvent<PhysiologyComponent, MobStateChangeAttemptEvent>(OnStateChangeAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<PhysiologyComponent, PhysiologyStateComponent, BodyComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out _, out var state, out var body, out _))
            Simulate(uid, body, state, frameTime);

        // Removed brains do not get free preservation by leaving the patient's organ container.
        var brains = EntityQueryEnumerator<CerebralHealthComponent, OrganComponent>();
        while (brains.MoveNext(out _, out var brain, out var organ))
        {
            if (organ.Body != null)
                continue;
            (brain.OxygenDebt, brain.Damage) = PhysiologyModel.Advance(
                brain.OxygenDebt, brain.Damage, 0f, frameTime);
        }
    }

    private void Simulate(EntityUid uid, BodyComponent body, PhysiologyStateComponent state, float seconds)
    {
        Sample(uid, body, state);
        CerebralHealthComponent? brain = null;
        foreach (var organ in _body.EnumerateOrgans<CerebralHealthComponent>((uid, body)))
        {
            brain = organ.Comp2;
            (brain.OxygenDebt, brain.Damage) = PhysiologyModel.Advance(
                brain.OxygenDebt, brain.Damage, state.OxygenDelivery, seconds);
            // The prototype provides one brain. Multiple brains are not supported yet.
            break;
        }

        var targetState = brain == null || brain.Damage >= PhysiologyModel.MaximumBrainDamage
            ? MobState.Dead
            : brain.OxygenDebt >= PhysiologyModel.UnconsciousDebt
                ? MobState.Critical
                : MobState.Alive;

        _mobState.ChangeMobState(uid, targetState);
        var transition = $"{targetState}; {state.LimitingFactor}";
        if (state.LastTransition != transition)
        {
            Record(state, transition);
            state.LastTransition = transition;
        }
    }

    private void Sample(EntityUid uid, BodyComponent body, PhysiologyStateComponent state)
    {
        state.BloodFraction = TryComp<BloodstreamComponent>(uid, out var blood) ? _blood.GetBloodLevel((uid, blood)) : 0f;
        state.HeartFunction = 0f;
        state.LungFunction = 0f;
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>((uid, body)))
        {
            var function = Math.Clamp(organ.Comp2.Function, 0f, 1f);
            switch (organ.Comp2.Kind)
            {
                case MedicalOrganKind.Heart:
                    state.HeartFunction = Math.Max(state.HeartFunction, function);
                    break;
                case MedicalOrganKind.Lungs:
                    state.LungFunction = Math.Max(state.LungFunction, function);
                    break;
            }
        }

        // Upstream saturation is a reserve measured in seconds, NOT SpO2.
        state.RespiratorySupply = TryComp<RespiratorComponent>(uid, out var respirator)
            && respirator.Saturation > respirator.SuffocationThreshold ? 1f : 0f;
        state.OxygenDelivery = PhysiologyModel.Delivery(state.BloodFraction, state.HeartFunction,
            state.LungFunction, state.RespiratorySupply);

        state.LimitingFactor = state.OxygenDelivery >= PhysiologyModel.AdequateDelivery ? "Adequate delivery"
            : state.HeartFunction == 0f ? "No functional heart"
            : state.LungFunction == 0f ? "No functional lungs"
            : state.RespiratorySupply == 0f ? "Respiratory reserve exhausted"
            : state.BloodFraction < PhysiologyModel.AdequateDelivery ? "Insufficient usable blood"
            : "Combined reduction of blood and organ function";
    }

    public string Inspect(EntityUid uid)
    {
        if (!HasComp<PhysiologyComponent>(uid) || !TryComp<BodyComponent>(uid, out var body)
            || !TryComp<PhysiologyStateComponent>(uid, out var state))
            return "Not an experimental medical patient. Spawn MobMedicalTestHuman.";

        Sample(uid, body, state);
        var text = new StringBuilder();
        text.AppendLine("EXPERIMENTAL physiology v1 (not clinical units)");
        if (TryComp<MobStateComponent>(uid, out var mob))
            text.AppendLine($"Mob state: {mob.CurrentState}");
        text.AppendLine($"Blood fraction: {state.BloodFraction:F3}; heart: {state.HeartFunction:F3}; lungs: {state.LungFunction:F3}");
        text.AppendLine($"Respiratory supply: {state.RespiratorySupply:F3}; delivery = blood * heart * lungs * supply = {state.OxygenDelivery:F3}");
        text.AppendLine($"Limiting factor: {state.LimitingFactor}");
        var hasBrain = false;
        foreach (var organ in _body.EnumerateOrgans<CerebralHealthComponent>((uid, body)))
        {
            hasBrain = true;
            text.AppendLine($"Brain {GetNetEntity(organ.Owner)}: debt={organ.Comp2.OxygenDebt:F2}s; injury={organ.Comp2.Damage:F2}/100; irreversible={organ.Comp2.Damage >= PhysiologyModel.MaximumBrainDamage}");
        }
        if (!hasBrain)
            text.AppendLine("No supported brain: body cannot be revived without transplantation.");
        text.AppendLine("Recent transitions / interventions:");
        foreach (var entry in state.History)
            text.AppendLine(entry);
        return text.ToString();
    }

    /// <summary>
    /// Debug interventions use real organ entities and bloodstream APIs. No hidden body overrides.
    /// </summary>
    public bool TryIntervene(EntityUid uid, string parameter, float value)
    {
        if (!float.IsFinite(value) || !HasComp<PhysiologyComponent>(uid)
            || !TryComp<BodyComponent>(uid, out var body) || !TryComp<PhysiologyStateComponent>(uid, out var state))
            return false;

        switch (parameter)
        {
            case "heart":
            case "lungs":
                if (value < 0f || value > 1f)
                    return false;
                var kind = parameter == "heart" ? MedicalOrganKind.Heart : MedicalOrganKind.Lungs;
                var found = false;
                foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>((uid, body)))
                {
                    if (organ.Comp2.Kind != kind)
                        continue;
                    organ.Comp2.Function = value;
                    found = true;
                }
                if (!found)
                    return false;
                break;
            case "blood":
                if (value < 0f || value > 1f || !TryComp<BloodstreamComponent>(uid, out var blood))
                    return false;
                if (!_blood.TryRegulateBloodLevel((uid, blood), blood.BloodReferenceSolution.Volume * 2, value))
                    return false;
                break;
            case "bleeding":
                if (!TryComp<BloodstreamComponent>(uid, out var bloodstream)
                    || value < 0f || value > bloodstream.MaxBleedAmount)
                    return false;
                _blood.TryModifyBleedAmount((uid, bloodstream), value - bloodstream.BleedAmount);
                break;
            default:
                return false;
        }

        Record(state, $"DEBUG {parameter}={value}");
        return true;
    }

    private void Record(PhysiologyStateComponent state, string entry)
    {
        while (state.History.Count >= 32)
            state.History.Dequeue();
        state.History.Enqueue($"{_timing.CurTime.TotalSeconds:F2}s: {entry}");
    }

    private void OnBrainRejuvenate(Entity<CerebralHealthComponent> ent, ref RejuvenateEvent args)
    {
        ent.Comp.Damage = 0f;
        ent.Comp.OxygenDebt = 0f;
    }

    private void OnStateChangeAttempt(Entity<PhysiologyComponent> ent, ref MobStateChangeAttemptEvent args)
    {
        if (args.NewState == MobState.Dead)
            return;

        foreach (var organ in _body.EnumerateOrgans<CerebralHealthComponent>(ent.Owner))
        {
            if (organ.Comp2.Damage < PhysiologyModel.MaximumBrainDamage
                && (args.NewState != MobState.Alive || organ.Comp2.OxygenDebt < PhysiologyModel.UnconsciousDebt))
                return;
        }

        // Defibrillators and healing damage must not revive a destroyed or absent brain.
        args.Cancelled = true;
    }

    private void OnRejuvenate(Entity<PhysiologyComponent> ent, ref RejuvenateEvent args)
    {
        foreach (var organ in _body.EnumerateOrgans<CerebralHealthComponent>(ent.Owner))
        {
            organ.Comp2.Damage = 0f;
            organ.Comp2.OxygenDebt = 0f;
        }
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(ent.Owner))
            organ.Comp2.Function = 1f;
        if (TryComp<PhysiologyStateComponent>(ent, out var state))
            Record(state, "Administrative rejuvenation");
    }
}
