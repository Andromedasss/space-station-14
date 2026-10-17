using Content.Shared.Body;
using Content.Shared.Medical;
using Content.Shared.Movement.Systems;
using Content.Shared.Speech;
using Content.Shared.Throwing;
using Content.Shared.Atmos.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Examine;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Fluids.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Body.Components;
using Robust.Shared.Containers;
using Robust.Shared.Random;

namespace Content.Server.Medical;

/// <summary>Persistent symptoms follow brain tissue. Only new tissue injury earns random rolls.</summary>
public sealed class MedicalNeurologySystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    private readonly HashSet<EntityUid> _nearby = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PhysiologyComponent, AccentGetEvent>(OnSpeech);
        SubscribeLocalEvent<PhysiologyComponent, BeforeThrowEvent>(OnThrow);
    }

    public void AddInjury(Entity<CerebralHealthComponent> brain, float injury)
    {
        if (!float.IsFinite(injury) || injury <= 0f)
            return;

        brain.Comp.NeurologicalInjuryRemainder += injury;
        while (brain.Comp.NeurologicalInjuryRemainder >= NeurologicalTrauma.DamagePerRoll)
        {
            brain.Comp.NeurologicalInjuryRemainder -= NeurologicalTrauma.DamagePerRoll;
            if (!_random.Prob(NeurologicalTrauma.SymptomChance))
                continue;

            // Each category can be acquired once; vision variants are mutually exclusive.
            var available = new List<int>();
            if (brain.Comp.ColorVision == MedicalColorVision.Normal)
                available.Add(0);
            if (!brain.Comp.Aphasia)
                available.Add(1);
            if (!brain.Comp.MuscleWeakness)
                available.Add(2);
            for (var i = 0; i < 5; i++)
                if (!brain.Comp.CognitiveSymptoms.HasFlag((MedicalCognitiveSymptoms) (1 << i)))
                    available.Add(3 + i);
            if (brain.Comp.Phobia == MedicalPhobia.None)
                available.Add(8);
            if (available.Count == 0)
                continue;
            var availableChoice = _random.Pick(available);
            switch (availableChoice)
            {
                case 0:
                    brain.Comp.ColorVision = (MedicalColorVision) _random.Next(1, 5);
                    break;
                case 1:
                    brain.Comp.Aphasia = true;
                    break;
                case 2:
                    brain.Comp.MuscleWeakness = true;
                    break;
                case 8:
                    brain.Comp.Phobia = (MedicalPhobia) _random.Next(1, 6);
                    break;
                default:
                    brain.Comp.CognitiveSymptoms |= (MedicalCognitiveSymptoms) (1 << (availableChoice - 3));
                    break;
            }
            if (_random.Prob(NeurologicalTrauma.TemporaryChance))
                brain.Comp.RecoveryRemaining[(MedicalNeurologicalCondition) availableChoice] = NeurologicalTrauma.RecoverySeconds;
        }
        if (TryComp<OrganComponent>(brain, out var organ) && organ.Body is { } host)
            Refresh(host);
    }

    public void Reset(Entity<CerebralHealthComponent> brain)
    {
        brain.Comp.NeurologicalInjuryRemainder = 0f;
        brain.Comp.ColorVision = MedicalColorVision.Normal;
        brain.Comp.Aphasia = false;
        brain.Comp.MuscleWeakness = false;
        brain.Comp.CognitiveSymptoms = MedicalCognitiveSymptoms.None;
        brain.Comp.Phobia = MedicalPhobia.None;
        brain.Comp.Fear = brain.Comp.FearUnconsciousSeconds = brain.Comp.FearRecoverySeconds = 0f;
        brain.Comp.FearSampleSeconds = 0f;
        brain.Comp.VisibleFearTriggers = 0;
        brain.Comp.RecoveryRemaining.Clear();
        if (TryComp<OrganComponent>(brain, out var organ) && organ.Body is { } host)
            Refresh(host);
    }

    public void Refresh(EntityUid uid)
    {
        if (!TryComp<PhysiologyComponent>(uid, out var physiology) || !HasComp<BodyComponent>(uid))
            return;
        CerebralHealthComponent? brain = null;
        foreach (var organ in _body.EnumerateOrgans<CerebralHealthComponent>(uid))
        {
            brain = organ.Comp2;
            break;
        }

        var vision = brain?.ColorVision ?? MedicalColorVision.Normal;
        var aphasia = brain?.Aphasia ?? false;
        var weakness = brain?.MuscleWeakness ?? false;
        var cognitive = brain?.CognitiveSymptoms ?? MedicalCognitiveSymptoms.None;
        var phobia = brain?.Phobia ?? MedicalPhobia.None;
        var fear = (int) MathF.Round(brain?.Fear ?? 0f);
        if (physiology.NeurologicalVision == vision && physiology.Aphasia == aphasia
            && physiology.MuscleWeakness == weakness && physiology.CognitiveSymptoms == cognitive
            && physiology.Phobia == phobia && physiology.Fear == fear)
            return;
        physiology.NeurologicalVision = vision;
        physiology.Aphasia = aphasia;
        physiology.MuscleWeakness = weakness;
        physiology.CognitiveSymptoms = cognitive;
        physiology.Phobia = phobia;
        physiology.Fear = fear;
        Dirty(uid, physiology);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    public static bool HasCondition(CerebralHealthComponent brain, MedicalNeurologicalCondition condition)
        => condition switch
        {
            MedicalNeurologicalCondition.ColorVision => brain.ColorVision != MedicalColorVision.Normal,
            MedicalNeurologicalCondition.Aphasia => brain.Aphasia,
            MedicalNeurologicalCondition.Weakness => brain.MuscleWeakness,
            MedicalNeurologicalCondition.Phobia => brain.Phobia != MedicalPhobia.None,
            _ => condition >= MedicalNeurologicalCondition.Prosopagnosia && condition <= MedicalNeurologicalCondition.Dysmetria
                && brain.CognitiveSymptoms.HasFlag((MedicalCognitiveSymptoms) (1 << ((int) condition - 3))),
        };

    public float Dose(EntityUid host, string reagent)
        => TryComp<BloodstreamComponent>(host, out var blood)
            && _solutions.TryGetSolution(host, blood.BloodSolutionName, out _, out var solution)
            ? solution.GetTotalPrototypeQuantity(reagent).Float() : 0f;

    public void ConsumeTreatment(EntityUid host)
    {
        if (TryComp<BloodstreamComponent>(host, out var blood)
            && _solutions.TryGetSolution(host, blood.BloodSolutionName, out var solution, out _))
            _solutions.RemoveReagent(solution.Value, "MedicalTestNeurorestorative", Content.Shared.FixedPoint.FixedPoint2.New(2));
    }

    /// <summary>Only supported, perfused brain tissue recovers. Detached organs never heal in storage.</summary>
    public void AdvanceRecovery(EntityUid host, CerebralHealthComponent brain, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f || brain.RecoveryRemaining.Count == 0 || !HasComp<PhysiologyComponent>(host)
            || brain.Damage >= PhysiologyModel.MaximumBrainDamage || brain.OxygenDebt >= PhysiologyModel.UnconsciousDebt
            || !TryComp<PhysiologyStateComponent>(host, out var state)
            || state.OxygenDelivery / state.MetabolicDemand < PhysiologyModel.AdequateDelivery
            || state.Temperature < 303.15f || state.Temperature > 313.15f)
            return;

        var rate = Dose(host, "MedicalTestNeurorestorative") >= NeurologicalTrauma.TherapeuticDose ? 4f : 1f;
        foreach (var condition in Enum.GetValues<MedicalNeurologicalCondition>())
        {
            if (!brain.RecoveryRemaining.TryGetValue(condition, out var remaining))
                continue;
            if (!HasCondition(brain, condition))
            {
                brain.RecoveryRemaining.Remove(condition);
                continue;
            }
            remaining -= seconds * rate;
            if (remaining > 0f)
            {
                brain.RecoveryRemaining[condition] = remaining;
                continue;
            }
            brain.RecoveryRemaining.Remove(condition);
            switch (condition)
            {
                case MedicalNeurologicalCondition.ColorVision:
                    brain.ColorVision = MedicalColorVision.Normal;
                    break;
                case MedicalNeurologicalCondition.Aphasia:
                    brain.Aphasia = false;
                    break;
                case MedicalNeurologicalCondition.Weakness:
                    brain.MuscleWeakness = false;
                    break;
                case MedicalNeurologicalCondition.Phobia:
                    brain.Phobia = MedicalPhobia.None;
                    brain.Fear = brain.FearUnconsciousSeconds = brain.FearRecoverySeconds = brain.FearSampleSeconds = 0f;
                    brain.VisibleFearTriggers = 0;
                    break;
                default:
                    brain.CognitiveSymptoms &= ~(MedicalCognitiveSymptoms) (1 << ((int) condition - 3));
                    break;
            }
        }
        Refresh(host);
    }

    public bool HasPersistentCondition(CerebralHealthComponent brain)
    {
        foreach (var condition in Enum.GetValues<MedicalNeurologicalCondition>())
            if (HasCondition(brain, condition) && !brain.RecoveryRemaining.ContainsKey(condition))
                return true;
        return false;
    }

    /// <summary>One persistent deficit becomes recoverable; this never repairs destroyed brain tissue.</summary>
    public bool Rehabilitate(CerebralHealthComponent brain)
    {
        if (brain.Damage >= PhysiologyModel.MaximumBrainDamage)
            return false;
        foreach (var condition in Enum.GetValues<MedicalNeurologicalCondition>())
        {
            if (!HasCondition(brain, condition) || brain.RecoveryRemaining.ContainsKey(condition))
                continue;
            brain.RecoveryRemaining[condition] = NeurologicalTrauma.SurgicalRecoverySeconds;
            return true;
        }
        return false;
    }

    private void OnSpeech(Entity<PhysiologyComponent> ent, ref AccentGetEvent args)
    {
        Refresh(ent);
        if (ent.Comp.Aphasia)
            args.Message = NeurologicalTrauma.AphasicSpeech(args.Message);
    }

    private void OnThrow(Entity<PhysiologyComponent> ent, ref BeforeThrowEvent args)
    {
        if (!ent.Comp.CognitiveSymptoms.HasFlag(MedicalCognitiveSymptoms.Dysmetria))
            return;
        var rotation = _random.NextAngle(Angle.FromDegrees(-15), Angle.FromDegrees(15));
        args.Direction = rotation.RotateVec(args.Direction) * _random.NextFloat(0.65f, 1.35f);
    }

    public void AdvanceFear(EntityUid host, CerebralHealthComponent brain, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f)
            return;
        // A new exposure cannot chain fainting: five seconds unconscious, then 30 seconds protected.
        brain.FearUnconsciousSeconds = Math.Max(0f, brain.FearUnconsciousSeconds - seconds);
        brain.FearRecoverySeconds = Math.Max(0f, brain.FearRecoverySeconds - seconds);
        var visible = 0;
        brain.FearSampleSeconds -= seconds;
        if (brain.Phobia != MedicalPhobia.None && brain.Damage < PhysiologyModel.UnconsciousBrainDamage
            && brain.OxygenDebt < PhysiologyModel.UnconsciousDebt && _mobState.IsAlive(host)
            && !(TryComp<BlindableComponent>(host, out var blind) && blind.IsBlind))
        {
            if (brain.FearSampleSeconds <= 0f)
            {
                brain.FearSampleSeconds = 0.5f;
                brain.VisibleFearTriggers = CountTriggers(host, brain.Phobia);
            }
            visible = brain.VisibleFearTriggers;
        }
        // Anxiolysis controls exposure stress, not the underlying phobia or an existing faint.
        if (Dose(host, "MedicalTestAnxiolytic") >= NeurologicalTrauma.TherapeuticDose)
            visible = 0;
        brain.Fear = NeurologicalTrauma.AdvanceFear(brain.Fear, visible, seconds);
        if (brain.FearRecoverySeconds > 0f)
            brain.Fear = Math.Min(brain.Fear, 80f);
        else if (brain.Fear >= 100f)
        {
            brain.FearUnconsciousSeconds = 5f;
            brain.FearRecoverySeconds = 35f;
            brain.Fear = 60f;
        }
        Refresh(host);
    }

    public int CountTriggers(EntityUid observer, MedicalPhobia phobia)
    {
        if (phobia == MedicalPhobia.None || TryComp<BlindableComponent>(observer, out var blind) && blind.IsBlind)
            return 0;
        _nearby.Clear();
        _lookup.GetEntitiesInRange(observer, 6f, _nearby, LookupFlags.Uncontained);
        var count = 0;
        foreach (var target in _nearby)
        {
            if (target == observer || !IsTrigger(target, phobia)
                || !_containers.IsInSameOrTransparentContainer(observer, target)
                || !_examine.InRangeUnOccluded(observer, target, 6f, entity => entity == observer || entity == target))
                continue;
            if (++count >= 5)
                break;
        }
        return count;
    }

    private bool IsTrigger(EntityUid target, MedicalPhobia phobia)
    {
        switch (phobia)
        {
            case MedicalPhobia.Doors:
                return HasComp<DoorComponent>(target);
            case MedicalPhobia.Uniforms:
                // Only visibly worn uniforms, never clothing hidden in a backpack.
                return HasComp<Content.Shared.Humanoid.HumanoidProfileComponent>(target)
                    && _inventory.TryGetSlotEntity(target, "jumpsuit", out _)
                    && !_inventory.TryGetSlotEntity(target, "outerClothing", out _);
            case MedicalPhobia.Fire:
                return TryComp<FlammableComponent>(target, out var fire) && fire.OnFire;
            case MedicalPhobia.Corpses:
                return _mobState.IsDead(target);
            case MedicalPhobia.Blood:
                if (!TryComp<PuddleComponent>(target, out var puddle)
                    || !_solutions.TryGetSolution(target, puddle.SolutionName, out _, out var solution))
                    return false;
                foreach (var reagent in solution.Contents)
                    if (reagent.Reagent.Prototype.Id.Contains("Blood", StringComparison.Ordinal))
                        return true;
                return false;
            default:
                return false;
        }
    }
}
