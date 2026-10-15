using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Medical;
using Content.Shared.Administration.Systems;
using Content.Shared.Body;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Body;

[TestFixture]
[TestOf(typeof(PhysiologySystem))]
public sealed class PhysiologyTest : GameTest
{
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
        Assert.That(organs.All(p => p.Comp2.Trauma == 40f), Is.True);
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
        Assert.That(organs.All(p => p.Comp2.Trauma == 40f), Is.True);
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
        Assert.That(body.EnumerateOrgans<MedicalOrganComponent>(patient).Count(), Is.EqualTo(2));

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
        Assert.That(scan.Physiology.OxygenDebt, Is.EqualTo(16f));
        Assert.That(scan.Physiology.Injuries.Single().Category, Is.EqualTo("LegLeft"));
        Assert.That(scan.Physiology.Injuries.Single().Bleeding, Is.EqualTo(4f).Within(0.001f));
        Assert.That(analyzer.GetHealthAnalyzerUiState(SSpawn("MobHuman")).Physiology, Is.Null);
        physiology.Update(140f);
        Assert.That(analyzer.GetHealthAnalyzerUiState(patient).Physiology!.BrainDamage, Is.EqualTo(100f));
    }
}
