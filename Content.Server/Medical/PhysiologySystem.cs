using System.Text;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Medical;
using Content.Shared.MedicalScanner;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Content.Shared.Chemistry.EntitySystems;
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
    [Dependency] private MedicalLifeSupportSystem _lifeSupport = default!;
    [Dependency] private MedicalResuscitationSystem _resuscitation = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(RespiratorSystem));
        UpdatesAfter.Add(typeof(BloodstreamSystem));
        UpdatesAfter.Add(typeof(MedicalWoundSystem));
        SubscribeLocalEvent<PhysiologyComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<CerebralHealthComponent, RejuvenateEvent>(OnBrainRejuvenate);
        SubscribeLocalEvent<MedicalOrganComponent, RejuvenateEvent>(OnOrganRejuvenate);
        SubscribeLocalEvent<PhysiologyComponent, MobStateChangeAttemptEvent>(OnStateChangeAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<PhysiologyComponent, PhysiologyStateComponent, BodyComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out _, out var state, out var body, out _))
            Simulate(uid, body, state, frameTime);

        // Brains outside a supported patient have no simulated perfusion, including unsupported transplants.
        var brains = EntityQueryEnumerator<CerebralHealthComponent, OrganComponent>();
        while (brains.MoveNext(out _, out var brain, out var organ))
        {
            if (organ.Body is { } host && HasComp<PhysiologyComponent>(host)
                && HasComp<PhysiologyStateComponent>(host) && HasComp<BodyComponent>(host)
                && HasComp<MobStateComponent>(host))
                continue;
            (brain.OxygenDebt, brain.Damage) = PhysiologyModel.Advance(
                brain.OxygenDebt, brain.Damage, 0f, frameTime);
        }
    }

    private void Simulate(EntityUid uid, BodyComponent body, PhysiologyStateComponent state, float seconds)
    {
        // Persistent shockable rhythm is an overdose consequence, not a scanner side effect.
        if (InotropeDose(uid) >= 10f)
        {
            foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>((uid, body)))
            {
                if (organ.Comp2.Kind == MedicalOrganKind.Heart && !organ.Comp2.Fibrillating)
                {
                    organ.Comp2.Fibrillating = true;
                    Record(state, "INOTROPE overdose: ventricular fibrillation");
                }
            }
        }
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
        state.CirculatoryFunction = 0f;
        state.LungFunction = 0f;
        var support = _lifeSupport.GetSupport(uid);
        state.LifeSupport = support != null;
        state.CprActive = _resuscitation.IsCompressing(uid, state);
        state.InotropeDose = InotropeDose(uid);
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>((uid, body)))
        {
            var function = organ.Comp2.EffectiveFunction;
            switch (organ.Comp2.Kind)
            {
                case MedicalOrganKind.Heart:
                    state.HeartFunction = Math.Max(state.HeartFunction, function);
                    // A pump assists the existing organ, not an absent or structurally destroyed one.
                    var assistance = support != null && float.IsFinite(support.PumpFunction)
                        ? Math.Clamp(support.PumpFunction, 0f, 1f) : 0f;
                    if (state.CprActive)
                        assistance = Math.Max(assistance, 0.6f);
                    var structure = 1f - Math.Clamp(organ.Comp2.Trauma / 100f, 0f, 1f);
                    var stimulated = !organ.Comp2.Fibrillating && organ.Comp2.Function > 0f
                        && state.InotropeDose >= 2f && state.InotropeDose < 10f
                        ? Math.Clamp(organ.Comp2.Function + 0.3f, 0f, 1f) * structure : function;
                    state.CirculatoryFunction = Math.Max(state.CirculatoryFunction, Math.Max(stimulated, assistance * structure));
                    break;
                case MedicalOrganKind.Lungs:
                    state.LungFunction = Math.Max(state.LungFunction, function);
                    break;
            }
        }

        // Upstream saturation is a reserve measured in seconds, NOT SpO2.
        state.RespiratorySupply = TryComp<RespiratorComponent>(uid, out var respirator)
            && respirator.Saturation > respirator.SuffocationThreshold ? 1f : 0f;
        state.OxygenDelivery = PhysiologyModel.Delivery(state.BloodFraction, state.CirculatoryFunction,
            state.LungFunction, state.RespiratorySupply);

        state.LimitingFactor = state.OxygenDelivery >= PhysiologyModel.AdequateDelivery ? "Adequate delivery"
            : state.CirculatoryFunction == 0f ? "No functional heart"
            : state.LungFunction == 0f ? "No functional lungs"
            : state.RespiratorySupply == 0f ? "Respiratory reserve exhausted"
            : state.BloodFraction < PhysiologyModel.AdequateDelivery ? "Insufficient usable blood"
            : "Combined reduction of blood and organ function";
    }

    public PhysiologyScan? Scan(EntityUid uid)
    {
        if (!HasComp<PhysiologyComponent>(uid) || !TryComp<BodyComponent>(uid, out var body)
            || !TryComp<PhysiologyStateComponent>(uid, out var state))
            return null;

        Sample(uid, body, state);
        var result = new PhysiologyScan
        {
            HeartFunction = state.HeartFunction,
            CirculatoryFunction = state.CirculatoryFunction,
            LifeSupport = state.LifeSupport,
            CprActive = state.CprActive,
            InotropeDose = state.InotropeDose,
            LungFunction = state.LungFunction,
            OxygenDelivery = state.OxygenDelivery,
            Mobility = Comp<PhysiologyComponent>(uid).Mobility,
            BrainMissing = true,
        };
        foreach (var organ in _body.EnumerateOrgans<CerebralHealthComponent>((uid, body)))
        {
            result.BrainMissing = false;
            result.OxygenDebt = organ.Comp2.OxygenDebt;
            result.BrainDamage = organ.Comp2.Damage;
            break;
        }
        foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>((uid, body)))
        {
            if (part.Comp2.Necrotic)
                result.NecroticParts.Add(part.Comp1.Category?.Id ?? "Unknown");
            if (part.Comp2.Prosthetic)
                result.ProstheticParts.Add(part.Comp1.Category?.Id ?? "Unknown");
            if (part.Comp2.Fractured)
                result.Fractures.Add(part.Comp1.Category?.Id ?? "Unknown");
            if (part.Comp2.VascularDamage > 0f)
                result.VascularInjuries.Add(new VascularInjuryScan(part.Comp1.Category?.Id ?? "Unknown",
                    part.Comp2.VascularDamage, MedicalWoundSystem.InternalBleeding(part.Comp2)));
            if (part.Comp2.SurgicalAccess > 0)
                result.SurgicalSites.Add(new SurgicalAccessScan(part.Comp1.Category?.Id ?? "Unknown", part.Comp2.SurgicalAccess));
            if (part.Comp2.Injury.GetTotal() > 0 || part.Comp2.Bleeding > 0f)
                result.Injuries.Add(new LocalizedInjuryScan(part.Comp1.Category?.Id ?? "Unknown",
                    part.Comp2.Injury.GetTotal().Float(), part.Comp2.Bleeding));
        }
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>((uid, body)))
        {
            result.ShockableRhythm |= organ.Comp2.Kind == MedicalOrganKind.Heart && organ.Comp2.Fibrillating;
            if (organ.Comp2.Trauma > 0f)
                result.OrganInjuries.Add(new InternalOrganInjuryScan(organ.Comp1.Category?.Id ?? "Unknown", organ.Comp2.Trauma));
        }
        return result;
    }

    private float InotropeDose(EntityUid uid)
    {
        return TryComp<BloodstreamComponent>(uid, out var blood)
            && _solutions.TryGetSolution(uid, blood.BloodSolutionName, out _, out var solution)
            ? solution.GetTotalPrototypeQuantity("MedicalTestInotrope").Float() : 0f;
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
        text.AppendLine($"Locomotor support modifier: {Comp<PhysiologyComponent>(uid).Mobility:F3}");
        text.AppendLine($"Life support: {state.LifeSupport}; effective circulation: {state.CirculatoryFunction:F3}");
        text.AppendLine($"Respiratory supply: {state.RespiratorySupply:F3}; delivery = blood * circulation * lungs * supply = {state.OxygenDelivery:F3}");
        text.AppendLine($"Limiting factor: {state.LimitingFactor}");
        var hasBrain = false;
        foreach (var organ in _body.EnumerateOrgans<CerebralHealthComponent>((uid, body)))
        {
            hasBrain = true;
            text.AppendLine($"Brain {GetNetEntity(organ.Owner)}: debt={organ.Comp2.OxygenDebt:F2}s; injury={organ.Comp2.Damage:F2}/100; irreversible={organ.Comp2.Damage >= PhysiologyModel.MaximumBrainDamage}");
        }
        if (!hasBrain)
            text.AppendLine("No supported brain: body cannot be revived without transplantation.");
        text.AppendLine("Localized injuries (unlocated attacks use Torso):");
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>((uid, body)))
            text.AppendLine($"{organ.Comp2.Kind} {GetNetEntity(organ.Owner)}: trauma={organ.Comp2.Trauma:F2}/100; effective function={organ.Comp2.EffectiveFunction:F3}");
        foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>((uid, body)))
        {
            if (part.Comp2.Injury.GetTotal() > 0 || part.Comp2.Bleeding > 0f)
                text.AppendLine($"{part.Comp1.Category} {GetNetEntity(part.Owner)}: {part.Comp2.Injury}; bleeding={part.Comp2.Bleeding:F3}/bloodstream tick");
        }
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

    internal void Record(PhysiologyStateComponent state, string entry)
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

    private void OnOrganRejuvenate(Entity<MedicalOrganComponent> ent, ref RejuvenateEvent args)
    {
        ent.Comp.Function = 1f;
        ent.Comp.Trauma = 0f;
        ent.Comp.Fibrillating = false;
    }

    private void OnStateChangeAttempt(Entity<PhysiologyComponent> ent, ref MobStateChangeAttemptEvent args)
    {
        var expected = MobState.Dead;
        foreach (var organ in _body.EnumerateOrgans<CerebralHealthComponent>(ent.Owner))
        {
            if (organ.Comp2.Damage < PhysiologyModel.MaximumBrainDamage)
                expected = organ.Comp2.OxygenDebt >= PhysiologyModel.UnconsciousDebt
                    ? MobState.Critical
                    : MobState.Alive;
            break;
        }

        // Reject incompatible transitions before death/revival side effects, not on the next frame.
        args.Cancelled |= args.NewState != expected;
    }

    private void OnRejuvenate(Entity<PhysiologyComponent> ent, ref RejuvenateEvent args)
    {
        foreach (var organ in _body.EnumerateOrgans<CerebralHealthComponent>(ent.Owner))
        {
            organ.Comp2.Damage = 0f;
            organ.Comp2.OxygenDebt = 0f;
        }
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(ent.Owner))
        {
            organ.Comp2.Function = 1f;
            organ.Comp2.Trauma = 0f;
            organ.Comp2.Fibrillating = false;
        }
        if (TryComp<PhysiologyStateComponent>(ent, out var state))
            Record(state, "Administrative rejuvenation");
    }
}
