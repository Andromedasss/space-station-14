using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Medical;
using Content.Shared.Administration.Systems;
using Content.Shared.Body;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.Tests.Body;

[TestFixture]
[TestOf(typeof(PhysiologySystem))]
public sealed class PhysiologyTest : GameTest
{
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
}
