using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Medical;
using Content.Server.Temperature.Systems;
using Content.Shared.Administration.Systems;
using Content.Shared.Body;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mind;
using Content.Shared.Body.Events;
using Content.Shared.Medical;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Temperature.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Body;

[TestFixture]
[TestOf(typeof(PhysiologySystem))]
public sealed class PhysiologyTest : GameTest
{
    [Test]
    [RunOnSide(Side.Server)]
    public void AbdominalFailureAccumulatesWasteAndHealthyTransplantsAllowRecovery()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var physiology = SEntMan.System<PhysiologySystem>();
        var body = SEntMan.System<BodySystem>();
        var kidneys = body.EnumerateOrgans<MedicalOrganComponent>(patient)
            .Where(p => p.Comp2.Kind == MedicalOrganKind.Kidney).ToArray();
        kidneys[0].Comp2.Function = 0f;
        Assert.That(physiology.Scan(patient)!.KidneyFunction, Is.EqualTo(1f), "One healthy kidney is sufficient.");
        kidneys[1].Comp2.Function = 0f;
        physiology.TryIntervene(patient, "liver", 0f);
        physiology.Update(600f);
        var scan = physiology.Scan(patient)!;
        Assert.That(scan.RenalWaste, Is.EqualTo(60f).Within(0.01f));
        Assert.That(scan.HepaticWaste, Is.EqualTo(48f).Within(0.01f));
        Assert.That(scan.CirculatoryFunction, Is.LessThan(0.5f));
        Assert.That(scan.Systolic, Is.LessThan(60f));
        var donor = SSpawn("MobMedicalTestHuman");
        var containers = SEntMan.System<SharedContainerSystem>();
        foreach (var kind in new[] { MedicalOrganKind.Kidney, MedicalOrganKind.Liver })
        {
            var incoming = body.EnumerateOrgans<MedicalOrganComponent>(donor).First(p => p.Comp2.Kind == kind);
            var outgoing = body.EnumerateOrgans<MedicalOrganComponent>(patient).First(p => p.Comp2.Kind == kind);
            Assert.That(containers.Remove(incoming.Owner, containers.GetContainer(donor, BodyComponent.ContainerID)), Is.True);
            Assert.That(containers.Remove(outgoing.Owner, containers.GetContainer(patient, BodyComponent.ContainerID)), Is.True);
            Assert.That(containers.Insert(incoming.Owner, containers.GetContainer(patient, BodyComponent.ContainerID)), Is.True);
        }
        Assert.That(physiology.Scan(patient)!.KidneyFunction, Is.EqualTo(1f));
        Assert.That(physiology.Scan(patient)!.LiverFunction, Is.EqualTo(1f));
        physiology.Update(100f);
        scan = physiology.Scan(patient)!;
        Assert.That(scan.RenalWaste, Is.LessThan(60f));
        Assert.That(scan.HepaticWaste, Is.LessThan(48f));
        SEntMan.System<RejuvenateSystem>().PerformRejuvenate(patient);
        Assert.That(physiology.Scan(patient)!.RenalWaste, Is.Zero);
        Assert.That(physiology.Scan(patient)!.HepaticWaste, Is.Zero);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void OrganRoutesAffectSelectedClearanceDigestionAndClottingOnly()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var physiology = SEntMan.System<PhysiologySystem>();
        physiology.TryIntervene(patient, "kidneys", 0f);
        physiology.TryIntervene(patient, "liver", 0f);
        physiology.TryIntervene(patient, "intestines", 0f);
        foreach (var reagent in new[] { "MedicalTestAnalgesic", "MedicalTestInotrope", "MedicalTestSedative" })
        {
            var clearance = new MetabolismRateEvent("Bloodstream", reagent);
            SEntMan.EventBus.RaiseLocalEvent(patient, ref clearance);
            Assert.That(clearance.Multiplier, Is.Zero);
        }
        var unaffected = new MetabolismRateEvent("Bloodstream", "Inaprovaline");
        SEntMan.EventBus.RaiseLocalEvent(patient, ref unaffected);
        Assert.That(unaffected.Multiplier, Is.EqualTo(1f));
        var digestion = new MetabolismRateEvent("Digestion", "Nutriment");
        SEntMan.EventBus.RaiseLocalEvent(patient, ref digestion);
        Assert.That(digestion.Multiplier, Is.Zero);
        var clotting = new BleedModifierEvent(2f, 0.1f);
        SEntMan.EventBus.RaiseLocalEvent(patient, ref clotting);
        Assert.That(clotting.BleedAmount, Is.EqualTo(2f));
        Assert.That(clotting.BleedReductionAmount, Is.Zero);
        physiology.TryIntervene(patient, "liver", 1f);
        clotting = new BleedModifierEvent(2f, 0.1f);
        SEntMan.EventBus.RaiseLocalEvent(patient, ref clotting);
        Assert.That(clotting.BleedReductionAmount, Is.EqualTo(0.1f));
        var ordinary = SSpawn("MobHuman");
        digestion = new MetabolismRateEvent("Digestion", "Nutriment");
        SEntMan.EventBus.RaiseLocalEvent(ordinary, ref digestion);
        Assert.That(digestion.Multiplier, Is.EqualTo(1f));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void PerforationContaminatesAbdomenAndRaisesThermoregulatoryTarget()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var physiology = SEntMan.System<PhysiologySystem>();
        SEntMan.System<MedicalWoundSystem>().TryInjure(patient, "Abdomen", "Piercing", 80f);
        var intestine = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(patient)
            .Single(p => p.Comp2.Kind == MedicalOrganKind.Intestines);
        Assert.That(intestine.Comp2.Perforated, Is.True);
        physiology.Update(200f);
        Assert.That(physiology.Scan(patient)!.Contamination, Is.GreaterThan(20f));
        var regulation = new ThermalRegulationEvent();
        SEntMan.EventBus.RaiseLocalEvent(patient, ref regulation);
        Assert.That(regulation.TargetOffset, Is.GreaterThan(0f));
        intestine.Comp2.Perforated = false;
        physiology.TryIntervene(patient, "liver", 1f);
        var before = physiology.Scan(patient)!.Contamination;
        physiology.Update(100f);
        Assert.That(physiology.Scan(patient)!.Contamination, Is.LessThan(before));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void CoolingBuysHypoxicTimeButExtremeTemperaturesAndBrainDeathRemainLethal()
    {
        var warm = SSpawn("MobMedicalTestHuman");
        var cold = SSpawn("MobMedicalTestHuman");
        var physiology = SEntMan.System<PhysiologySystem>();
        Assert.That(physiology.TryIntervene(cold, "temperature", 300.15f), Is.True);
        physiology.TryIntervene(warm, "heart", 0f);
        physiology.TryIntervene(cold, "heart", 0f);
        physiology.Update(140f);
        Assert.That(SEntMan.System<MobStateSystem>().IsDead(warm), Is.True);
        Assert.That(SEntMan.System<MobStateSystem>().IsDead(cold), Is.False);
        Assert.That(physiology.Scan(cold)!.BrainDamage, Is.EqualTo(40f).Within(0.01f));
        Assert.That(SComp<PhysiologyComponent>(cold).TemperatureSpeed, Is.LessThan(1f));
        physiology.TryIntervene(warm, "temperature", 300.15f);
        physiology.Update(0f);
        Assert.That(SEntMan.System<MobStateSystem>().IsDead(warm), Is.True);
        var frozen = SSpawn("MobMedicalTestHuman");
        physiology.TryIntervene(frozen, "temperature", 278.15f);
        physiology.Update(110f);
        Assert.That(SEntMan.System<MobStateSystem>().IsDead(frozen), Is.True);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void PressureAndPulseFollowBloodPumpAndTemperatureWithoutReplacingBrainDeath()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var physiology = SEntMan.System<PhysiologySystem>();
        var healthy = physiology.Scan(patient)!;
        Assert.That(healthy.Pulse, Is.EqualTo(80f).Within(0.01f));
        Assert.That(healthy.Systolic, Is.EqualTo(120f).Within(0.01f));
        Assert.That(healthy.Diastolic, Is.EqualTo(80f).Within(0.01f));
        physiology.TryIntervene(patient, "blood", 0.5f);
        var bleeding = physiology.Scan(patient)!;
        Assert.That(bleeding.Systolic, Is.LessThan(healthy.Systolic));
        Assert.That(bleeding.Pulse, Is.GreaterThan(healthy.Pulse));
        physiology.TryIntervene(patient, "temperature", 300.15f);
        Assert.That(physiology.Scan(patient)!.Pulse, Is.LessThan(bleeding.Pulse));
        physiology.TryIntervene(patient, "heart", 0f);
        Assert.That(physiology.Scan(patient)!.Pulse, Is.Zero);
        Assert.That(physiology.Scan(patient)!.Systolic, Is.Zero);
        physiology.TryIntervene(patient, "heart", 1f);
        SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(patient).Single().Comp2.Damage = 100f;
        physiology.Update(0f);
        Assert.That(SEntMan.System<MobStateSystem>().IsDead(patient), Is.True);
        Assert.That(physiology.Scan(patient)!.Pulse, Is.GreaterThan(0f), "Brain death does not itself stop the heart.");
    }

    [Test]
    public async Task ExperimentalTemperatureSpeedReplacesRatherThanStacksWithUpstreamSlowdown()
    {
        EntityUid patient = default;
        EntityUid ordinary = default;
        await Server.WaitPost(() =>
        {
            patient = SSpawn("MobMedicalTestHuman");
            ordinary = SSpawn("MobHuman");
            SEntMan.System<PhysiologySystem>().TryIntervene(patient, "temperature", 285.15f);
            var temperature = SComp<TemperatureComponent>(ordinary);
            SEntMan.System<TemperatureSystem>().ChangeHeat((ordinary, temperature),
                (285.15f - temperature.Temperature) * temperature.HeatCapacity, true);
        });
        await Pair.RunSeconds(2f);
        await Server.WaitAssertion(() =>
        {
            var upstream = SComp<TemperatureSpeedComponent>(patient);
            Assert.That(upstream.NextSlowdownUpdate, Is.Null);
            Assert.That(upstream.CurrentSpeedModifier, Is.Not.Null);
            SEntMan.System<MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(patient);
            Assert.That(SComp<MovementSpeedModifierComponent>(patient).WalkSpeedModifier,
                Is.EqualTo(SComp<PhysiologyComponent>(patient).TemperatureSpeed).Within(0.001f));
            SEntMan.System<MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(ordinary);
            Assert.That(SComp<MovementSpeedModifierComponent>(ordinary).WalkSpeedModifier,
                Is.EqualTo(SComp<TemperatureSpeedComponent>(ordinary).CurrentSpeedModifier!.Value).Within(0.001f));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    [RunOnSide(Side.Server)]
    public void PhysicalBrainTransplantCarriesMindAndInjury(bool wholeHead)
    {
        var donor = SSpawn("MobMedicalTestHuman");
        var recipient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var minds = SEntMan.System<SharedMindSystem>();
        var containers = SEntMan.System<SharedContainerSystem>();
        var mind = minds.CreateMind(null);
        var recipientMind = minds.CreateMind(null);
        minds.TransferTo(mind, donor, mind: mind);
        minds.TransferTo(recipientMind, recipient, mind: recipientMind);
        var brain = body.EnumerateOrgans<CerebralHealthComponent>(donor).Single();
        var oldBrain = body.EnumerateOrgans<CerebralHealthComponent>(recipient).Single();
        brain.Comp2.Damage = 42f;
        var incoming = wholeHead ? body.EnumerateOrgans<MedicalWoundComponent>(donor)
            .Single(p => p.Comp1.Category?.Id == "Head").Owner : brain.Owner;
        var outgoing = wholeHead ? body.EnumerateOrgans<MedicalWoundComponent>(recipient)
            .Single(p => p.Comp1.Category?.Id == "Head").Owner : oldBrain.Owner;
        Assert.That(containers.Remove(incoming, containers.GetContainer(donor, BodyComponent.ContainerID)), Is.True);
        Assert.That(mind.Comp.OwnedEntity, Is.EqualTo(brain.Owner));
        Assert.That(containers.Remove(outgoing, containers.GetContainer(recipient, BodyComponent.ContainerID)), Is.True);
        Assert.That(recipientMind.Comp.OwnedEntity, Is.EqualTo(oldBrain.Owner));
        Assert.That(containers.Insert(incoming, containers.GetContainer(recipient, BodyComponent.ContainerID)), Is.True);
        Assert.That(mind.Comp.OwnedEntity, Is.EqualTo(recipient));
        Assert.That(recipientMind.Comp.OwnedEntity, Is.EqualTo(oldBrain.Owner));
        Assert.That(brain.Comp2.Damage, Is.EqualTo(42f));
        Assert.That(body.EnumerateOrgans<CerebralHealthComponent>(recipient).Single().Owner, Is.EqualTo(brain.Owner));
        SEntMan.System<PhysiologySystem>().Update(0f);
        Assert.That(SEntMan.System<MobStateSystem>().IsDead(donor), Is.True);
        minds.WipeMind(mind.Owner, mind.Comp);
        minds.WipeMind(recipientMind.Owner, recipientMind.Comp);
        SDeleteNow(donor);
        SDeleteNow(recipient);
        SDeleteNow(outgoing);
        SDeleteNow(mind.Owner);
        SDeleteNow(recipientMind.Owner);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void TorsoTraumaReducesOrganFunctionWithoutBeingHealedByDressingsOrBlood()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var wounds = SEntMan.System<MedicalWoundSystem>();
        var physiology = SEntMan.System<PhysiologySystem>();
        var organs = body.EnumerateOrgans<MedicalOrganComponent>(patient).ToArray();
        wounds.TryInjure(patient, "ArmLeft", "Piercing", 100f);
        wounds.TryInjure(patient, "Torso", "Piercing", 20f);
        Assert.That(organs.All(p => p.Comp2.Trauma == 0f), Is.True);

        wounds.TryInjure(patient, "Torso", "Piercing", 60f);
        Assert.That(organs.All(p => p.Comp2.Trauma == (p.Comp2.ProtectedBy == "Torso" ? 40f : 0f)), Is.True);
        var scan = physiology.Scan(patient)!;
        Assert.That(scan.HeartFunction, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(scan.LungFunction, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(scan.OrganInjuries.Count, Is.EqualTo(2));
        Assert.That(scan.OrganInjuries.All(p => p.Trauma == 40f), Is.True);
        Assert.That(physiology.Inspect(patient), Does.Contain("trauma=40.00/100"));

        SEntMan.System<DamageableSystem>().TryChangeDamage(patient,
            new DamageSpecifier { DamageDict = { ["Piercing"] = FixedPoint2.New(-1000) } }, true);
        SEntMan.System<BloodstreamSystem>().TryModifyBleedAmount(patient, -10f);
        physiology.TryIntervene(patient, "blood", 1f);
        physiology.Update(60f);
        Assert.That(organs.All(p => p.Comp2.Trauma == (p.Comp2.ProtectedBy == "Torso" ? 40f : 0f)), Is.True);
        Assert.That(SEntMan.System<MobStateSystem>().IsCritical(patient), Is.True);
        Assert.That(physiology.Scan(patient)!.OxygenDelivery, Is.EqualTo(0.36f).Within(0.001f));

        SEntMan.System<RejuvenateSystem>().PerformRejuvenate(patient);
        physiology.Update(0f);
        Assert.That(organs.All(p => p.Comp2.Trauma == 0f && p.Comp2.EffectiveFunction == 1f), Is.True);
        Assert.That(SEntMan.System<MobStateSystem>().IsAlive(patient), Is.True);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void InternalTraumaFollowsPhysicalTransplantsAndOrganRejuvenation()
    {
        var donor = SSpawn("MobMedicalTestHuman");
        var patient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var physiology = SEntMan.System<PhysiologySystem>();
        var heart = body.EnumerateOrgans<MedicalOrganComponent>(donor).Single(p => p.Comp2.Kind == MedicalOrganKind.Heart);
        var oldHeart = body.EnumerateOrgans<MedicalOrganComponent>(patient).Single(p => p.Comp2.Kind == MedicalOrganKind.Heart);
        SEntMan.System<MedicalWoundSystem>().TryInjure(donor, "Torso", "Blunt", 100f);
        Assert.That(heart.Comp2.Trauma, Is.EqualTo(40f));
        var containers = SEntMan.System<SharedContainerSystem>();
        containers.Remove(heart.Owner, containers.GetContainer(donor, BodyComponent.ContainerID));
        containers.Remove(oldHeart.Owner, containers.GetContainer(patient, BodyComponent.ContainerID));
        containers.Insert(heart.Owner, containers.GetContainer(patient, BodyComponent.ContainerID));
        Assert.That(physiology.Scan(patient)!.HeartFunction, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(heart.Comp2.Trauma, Is.EqualTo(40f));
        physiology.TryIntervene(patient, "heart", 1f);
        Assert.That(physiology.Scan(patient)!.HeartFunction, Is.EqualTo(0.6f).Within(0.001f),
            "Changing functional capacity must not erase structural trauma.");
        SEntMan.System<RejuvenateSystem>().PerformRejuvenate(heart.Owner);
        Assert.That(physiology.Scan(patient)!.HeartFunction, Is.EqualTo(1f));
        Assert.That(physiology.Scan(patient)!.OrganInjuries, Is.Empty);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void DestructiveTorsoInjuryCapsInternalTraumaAndCausesPerfusionFailure()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        SEntMan.System<MedicalWoundSystem>().TryInjure(patient, "Torso", "Piercing", 1000f);
        var physiology = SEntMan.System<PhysiologySystem>();
        var scan = physiology.Scan(patient)!;
        Assert.That(scan.OrganInjuries.All(p => p.Trauma == 100f), Is.True);
        Assert.That(scan.HeartFunction, Is.Zero);
        Assert.That(scan.LungFunction, Is.Zero);
        physiology.Update(16f);
        Assert.That(SEntMan.System<MobStateSystem>().IsCritical(patient), Is.True);
        physiology.Update(140f);
        Assert.That(SEntMan.System<MobStateSystem>().IsDead(patient), Is.True);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void HeartFailureRecoveryAndIrreversibleDeath()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var physiology = SEntMan.System<PhysiologySystem>();
        var mob = SEntMan.System<MobStateSystem>();
        var brain = body.EnumerateOrgans<CerebralHealthComponent>(patient).Single().Comp2;
        Assert.That(body.EnumerateOrgans<MedicalOrganComponent>(patient).Count(), Is.EqualTo(6));

        Assert.That(physiology.TryIntervene(patient, "heart", 0f), Is.True);
        physiology.Update(16f);
        Assert.That(mob.IsCritical(patient), Is.True);
        Assert.That(brain.Damage, Is.Zero);
        Assert.That(physiology.TryIntervene(patient, "heart", 1f), Is.True);
        physiology.Update(9f);
        Assert.That(mob.IsAlive(patient), Is.True);

        Assert.That(physiology.TryIntervene(patient, "heart", 0f), Is.True);
        physiology.Update(140f);
        Assert.That(mob.IsDead(patient), Is.True);
        Assert.That(physiology.TryIntervene(patient, "heart", 1f), Is.True);
        physiology.Update(100f);
        mob.ChangeMobState(patient, MobState.Alive);
        Assert.That(mob.IsDead(patient), Is.True);
        mob.ChangeMobState(patient, MobState.Critical);
        Assert.That(mob.IsDead(patient), Is.True);
        Assert.That(brain.Damage, Is.EqualTo(100f));

        SEntMan.System<RejuvenateSystem>().PerformRejuvenate(patient);
        physiology.Update(0f);
        Assert.That(mob.IsAlive(patient), Is.True);
        Assert.That(brain.Damage, Is.Zero);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void UpstreamPatientsKeepDamageThresholds()
    {
        var patient = SSpawn("MobHuman");
        var damage = SEntMan.System<DamageableSystem>();
        damage.TryChangeDamage(patient, new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(300) } }, true);
        Assert.That(SEntMan.System<MobStateSystem>().IsDead(patient), Is.True);

        var experimental = SSpawn("MobMedicalTestHuman");
        damage.TryChangeDamage(experimental, new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(300) } }, true);
        Assert.That(SEntMan.System<MobStateSystem>().IsDead(experimental), Is.False);
        Assert.That(damage.GetTotalDamage((experimental, SComp<DamageableComponent>(experimental))), Is.GreaterThan(FixedPoint2.Zero));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void BrainInjuryFollowsTransplantAndDetachedBrainDeteriorates()
    {
        var donor = SSpawn("MobMedicalTestHuman");
        var recipient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var physiology = SEntMan.System<PhysiologySystem>();
        var donorBrain = body.EnumerateOrgans<CerebralHealthComponent>(donor).Single();
        var oldBrain = body.EnumerateOrgans<CerebralHealthComponent>(recipient).Single();
        donorBrain.Comp2.Damage = 42f;
        var containers = SEntMan.System<SharedContainerSystem>();
        containers.Remove(donorBrain.Owner, containers.GetContainer(donor, BodyComponent.ContainerID));
        containers.Remove(oldBrain.Owner, containers.GetContainer(recipient, BodyComponent.ContainerID));
        physiology.Update(10f);
        Assert.That(donorBrain.Comp2.OxygenDebt, Is.EqualTo(10f));
        containers.Insert(donorBrain.Owner, containers.GetContainer(recipient, BodyComponent.ContainerID));
        Assert.That(body.EnumerateOrgans<CerebralHealthComponent>(recipient).Single().Comp2.Damage, Is.EqualTo(42f));
        physiology.Update(0f);
        Assert.That(SEntMan.System<MobStateSystem>().IsDead(donor), Is.True);
        Assert.That(physiology.TryIntervene(recipient, "blood", float.NaN), Is.False);
        Assert.That(physiology.TryIntervene(recipient, "heart", -1f), Is.False);
        Assert.That(physiology.TryIntervene(recipient, "blood", 0.25f), Is.True);
        Assert.That(physiology.Inspect(recipient), Does.Contain("Insufficient usable blood"));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void ExternalStateChangesMustMatchBrainHealth()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var mob = SEntMan.System<MobStateSystem>();
        var physiology = SEntMan.System<PhysiologySystem>();

        mob.ChangeMobState(patient, MobState.Dead);
        Assert.That(mob.IsAlive(patient), Is.True);
        mob.ChangeMobState(patient, MobState.Critical);
        Assert.That(mob.IsAlive(patient), Is.True);

        Assert.That(physiology.TryIntervene(patient, "heart", 0f), Is.True);
        physiology.Update(16f);
        mob.ChangeMobState(patient, MobState.Alive);
        Assert.That(mob.IsCritical(patient), Is.True);
        mob.ChangeMobState(patient, MobState.Dead);
        Assert.That(mob.IsCritical(patient), Is.True);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void UnconsciousExperimentalPatientsCanBreatheButDeadPatientsCannot()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var physiology = SEntMan.System<PhysiologySystem>();
        var respirator = SEntMan.System<RespiratorSystem>();
        var reserve = SComp<RespiratorComponent>(patient);
        Assert.That(reserve.Saturation, Is.GreaterThan(reserve.SuffocationThreshold));
        Assert.That(physiology.TryIntervene(patient, "heart", 0f), Is.True);
        physiology.Update(16f);
        Assert.That(respirator.IsBreathing(patient), Is.True);
        physiology.Update(140f);
        Assert.That(respirator.IsBreathing(patient), Is.False);

        var upstream = SSpawn("MobHuman");
        SEntMan.System<MobStateSystem>().ChangeMobState(upstream, MobState.Critical);
        Assert.That(respirator.IsBreathing(upstream), Is.False);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void UnsupportedTransplantDoesNotFreezeBrainHypoxia()
    {
        var donor = SSpawn("MobMedicalTestHuman");
        var recipient = SSpawn("MobHuman");
        var body = SEntMan.System<BodySystem>();
        var brain = body.EnumerateOrgans<CerebralHealthComponent>(donor).Single();
        var containers = SEntMan.System<SharedContainerSystem>();
        containers.Remove(brain.Owner, containers.GetContainer(donor, BodyComponent.ContainerID));
        containers.Insert(brain.Owner, containers.GetContainer(recipient, BodyComponent.ContainerID));

        SEntMan.System<PhysiologySystem>().Update(40f);
        Assert.That(brain.Comp2.OxygenDebt, Is.EqualTo(40f));
        Assert.That(brain.Comp2.Damage, Is.EqualTo(10f));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void PausedPatientsAndDetachedBrainsDoNotAccumulateHypoxia()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var detached = SSpawn("OrganMedicalTestBrain");
        var physiology = SEntMan.System<PhysiologySystem>();
        var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(patient).Single().Comp2;
        var metadata = SEntMan.System<MetaDataSystem>();
        Assert.That(physiology.TryIntervene(patient, "heart", 0f), Is.True);
        metadata.SetEntityPaused(patient, true);
        metadata.SetEntityPaused(detached, true);
        physiology.Update(40f);
        Assert.That(brain.OxygenDebt, Is.Zero);
        Assert.That(SComp<CerebralHealthComponent>(detached).OxygenDebt, Is.Zero);

        metadata.SetEntityPaused(patient, false);
        metadata.SetEntityPaused(detached, false);
        physiology.Update(40f);
        Assert.That(brain.OxygenDebt, Is.EqualTo(40f));
        Assert.That(SComp<CerebralHealthComponent>(detached).OxygenDebt, Is.EqualTo(40f));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void HealthAnalyzerReportsExperimentalPhysiologyWithoutChangingUpstreamScans()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var analyzer = SEntMan.System<HealthAnalyzerSystem>();
        var physiology = SEntMan.System<PhysiologySystem>();
        SEntMan.System<MedicalWoundSystem>().TryInjure(patient, "LegLeft", "Slash", 20f);
        physiology.TryIntervene(patient, "heart", 0f);
        physiology.Update(16f);
        var scan = analyzer.GetHealthAnalyzerUiState(patient);
        Assert.That(scan.Physiology, Is.Not.Null);
        Assert.That(scan.Physiology!.HeartFunction, Is.Zero);
        Assert.That(scan.Physiology.OxygenDebt, Is.Zero);
        Assert.That(scan.Physiology.InternalWarning && scan.Physiology.NeurologicalWarning, Is.True);
        Assert.That(scan.Physiology.Injuries.Single().Category, Is.EqualTo("LegLeft"));
        Assert.That(scan.Physiology.Injuries.Single().Bleeding, Is.EqualTo(4f).Within(0.001f));
        Assert.That(analyzer.GetHealthAnalyzerUiState(SSpawn("MobHuman")).Physiology, Is.Null);
        physiology.Update(140f);
        Assert.That(analyzer.GetHealthAnalyzerUiState(patient).Physiology!.BrainDamage, Is.Zero);
        Assert.That(analyzer.GetHealthAnalyzerUiState(patient).Physiology!.NeurologicalWarning, Is.True);
        Assert.That(analyzer.GetHealthAnalyzerUiState(patient, detailed: true).Physiology!.BrainDamage, Is.EqualTo(100f));
    }
}
