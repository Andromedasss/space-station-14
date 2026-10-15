using System;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Medical;
using Content.Shared.Administration.Systems;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.DoAfter;
using Content.Shared.Medical;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stacks;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.Tests.Body;

[TestFixture]
[TestOf(typeof(MedicalWoundSystem))]
public sealed class MedicalWoundTest : GameTest
{
    [Test]
    [RunOnSide(Side.Server)]
    public void LocatedInjuryClottingAndBandagingPersist()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var wounds = SEntMan.System<MedicalWoundSystem>();
        var blood = SEntMan.System<BloodstreamSystem>();
        var stream = SComp<BloodstreamComponent>(patient);
        var parts = body.EnumerateOrgans<MedicalWoundComponent>(patient).ToArray();
        Assert.That(parts, Has.Length.EqualTo(10));
        var leg = parts.Single(p => p.Comp1.Category?.Id == "LegLeft");
        Assert.That(wounds.TryInjure(patient, "LegLeft", "Slash", 20f), Is.True);
        Assert.That(leg.Comp2.Injury.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(20)));
        Assert.That(leg.Comp2.Bleeding, Is.EqualTo(4f).Within(0.001f));
        Assert.That(parts.Where(p => p.Owner != leg.Owner).All(p => p.Comp2.Injury.Empty), Is.True);
        Assert.That(stream.BleedAmount, Is.EqualTo(4f).Within(0.001f));

        var before = blood.GetBloodLevel((patient, stream));
        blood.TickBleed((patient, stream));
        Assert.That(blood.GetBloodLevel((patient, stream)), Is.LessThan(before));
        Assert.That(leg.Comp2.Bleeding, Is.EqualTo(stream.BleedAmount).Within(0.001f));
        Assert.That(leg.Comp2.Bleeding, Is.LessThan(4f));

        blood.TryModifyBleedAmount((patient, stream), -10f);
        Assert.That(leg.Comp2.Bleeding, Is.Zero);
        Assert.That(leg.Comp2.Injury.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(20)));
        SEntMan.System<DamageableSystem>().TryChangeDamage(patient,
            new DamageSpecifier { DamageDict = { ["Poison"] = FixedPoint2.New(1) } }, true);
        Assert.That(stream.BleedAmount, Is.Zero, "An unrelated damage event must not reopen a dressed wound.");
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void UnlocatedDamageUsesTorsoAndUpstreamPatientsAreUnchanged()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var upstream = SSpawn("MobHuman");
        var damage = SEntMan.System<DamageableSystem>();
        var slash = new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(10) } };
        damage.TryChangeDamage(patient, slash, true);
        var torso = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient)
            .Single(p => p.Comp1.Category?.Id == "Torso");
        Assert.That(torso.Comp2.Injury.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(10)));
        Assert.That(SComp<BloodstreamComponent>(patient).BleedAmount, Is.EqualTo(2f).Within(0.001f));
        damage.TryChangeDamage(upstream, slash, true);
        Assert.That(SComp<BloodstreamComponent>(upstream).BleedAmount, Is.GreaterThan(0f));
        Assert.That(SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(upstream), Is.Empty);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void HealingAndRejuvenationRepairLocalInjuries()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var wounds = SEntMan.System<MedicalWoundSystem>();
        wounds.TryInjure(patient, "LegLeft", "Slash", 20f);
        wounds.TryInjure(patient, "ArmRight", "Slash", 10f);
        var parts = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient).ToArray();
        SEntMan.System<DamageableSystem>().TryChangeDamage(patient,
            new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(-15) } }, true);
        Assert.That(parts.Sum(p => p.Comp2.Injury.GetTotal().Float()), Is.EqualTo(15f).Within(0.003f));
        Assert.That(SComp<BloodstreamComponent>(patient).BleedAmount, Is.EqualTo(6f).Within(0.001f),
            "Tissue repair alone is not hemostasis.");

        SEntMan.System<RejuvenateSystem>().PerformRejuvenate(patient);
        Assert.That(parts.All(p => p.Comp2.Injury.Empty && p.Comp2.Bleeding == 0f), Is.True);
        Assert.That(SComp<BloodstreamComponent>(patient).BleedAmount, Is.Zero);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void InjuryAndBleedingFollowPhysicalPart()
    {
        var donor = SSpawn("MobMedicalTestHuman");
        var recipient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var wounds = SEntMan.System<MedicalWoundSystem>();
        wounds.TryInjure(donor, "LegLeft", "Piercing", 10f);
        var leg = body.EnumerateOrgans<MedicalWoundComponent>(donor).Single(p => p.Comp1.Category?.Id == "LegLeft");
        var oldLeg = body.EnumerateOrgans<MedicalWoundComponent>(recipient).Single(p => p.Comp1.Category?.Id == "LegLeft");
        var containers = SEntMan.System<SharedContainerSystem>();
        Assert.That(containers.Remove(leg.Owner, containers.GetContainer(donor, BodyComponent.ContainerID)), Is.True);
        Assert.That(SComp<BloodstreamComponent>(donor).BleedAmount, Is.Zero);
        Assert.That(SEntMan.System<DamageableSystem>().GetAllDamage(donor).GetTotal(), Is.EqualTo(FixedPoint2.Zero));
        Assert.That(leg.Comp2.Bleeding, Is.EqualTo(3f).Within(0.001f));
        Assert.That(containers.Remove(oldLeg.Owner, containers.GetContainer(recipient, BodyComponent.ContainerID)), Is.True);
        Assert.That(containers.Insert(leg.Owner, containers.GetContainer(recipient, BodyComponent.ContainerID)), Is.True);
        Assert.That(SComp<BloodstreamComponent>(recipient).BleedAmount, Is.EqualTo(3f).Within(0.001f));
        Assert.That(leg.Comp2.Injury.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(10)));
        var damage = SEntMan.System<DamageableSystem>();
        Assert.That(damage.GetAllDamage(recipient).DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(10)));
        damage.TryChangeDamage(recipient,
            new DamageSpecifier { DamageDict = { ["Piercing"] = FixedPoint2.New(-10) } }, true);
        Assert.That(leg.Comp2.Injury.GetTotal(), Is.EqualTo(FixedPoint2.Zero));
        Assert.That(damage.GetAllDamage(recipient).GetTotal(), Is.EqualTo(FixedPoint2.Zero));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void BleedingLegCanCauseHypoxiaAndEarlyStabilizationAllowsRecovery()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var blood = SEntMan.System<BloodstreamSystem>();
        var stream = SComp<BloodstreamComponent>(patient);
        var physiology = SEntMan.System<PhysiologySystem>();
        // A patient with reduced reserve is vulnerable before this wound starts bleeding.
        Assert.That(physiology.TryIntervene(patient, "blood", 0.6f), Is.True);
        Assert.That(SEntMan.System<MedicalWoundSystem>().TryInjure(patient, "LegLeft", "Slash", 50f), Is.True);
        // Isolate wound-driven blood loss from natural regeneration and oxygen-reserve ticking.
        for (var i = 0; i < 80; i++)
            blood.TickBleed((patient, stream));
        Assert.That(blood.GetBloodLevel((patient, stream)), Is.LessThan(0.5f));
        physiology.Update(60f);
        Assert.That(SEntMan.System<MobStateSystem>().IsCritical(patient), Is.True);
        blood.TryModifyBleedAmount((patient, stream), -10f);
        Assert.That(physiology.TryIntervene(patient, "blood", 1f), Is.True);
        physiology.Update(60f);
        Assert.That(SEntMan.System<MobStateSystem>().IsAlive(patient), Is.True);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void InvalidLocatedInjuriesDoNotMutatePatient()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var wounds = SEntMan.System<MedicalWoundSystem>();
        Assert.That(wounds.TryInjure(patient, "Brain", "Slash", 10f), Is.False);
        Assert.That(wounds.TryInjure(patient, "LegLeft", "Poison", 10f), Is.False);
        Assert.That(wounds.TryInjure(patient, "LegLeft", "Slash", float.NaN), Is.False);
        Assert.That(wounds.TryInjure(patient, "LegLeft", "Slash", -1f), Is.False);
        Assert.That(wounds.TryInjure(patient, "LegLeft", "Slash", 0.001f), Is.False);
        Assert.That(wounds.TryInjure(SSpawn("MobHuman"), "LegLeft", "Slash", 10f), Is.False);
        Assert.That(SComp<BloodstreamComponent>(patient).BleedAmount, Is.Zero);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void CompletedGauzeTreatmentStopsBleedingAndConsumesOneUnit()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var gauze = SSpawn("Gauze");
        Assert.That(SEntMan.System<MedicalWoundSystem>().TryInjure(patient, "LegLeft", "Slash", 20f), Is.True);
        var leg = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient)
            .Single(p => p.Comp1.Category?.Id == "LegLeft");
        var ev = new HealingDoAfterEvent();
        ev.DoAfter = new Content.Shared.DoAfter.DoAfter(0,
            new DoAfterArgs(SEntMan, patient, 0f, ev, patient, target: patient, used: gauze), TimeSpan.Zero);
        SEntMan.EventBus.RaiseLocalEvent(patient, ev);
        Assert.That(ev.Handled, Is.True);
        Assert.That(SComp<StackComponent>(gauze).Count, Is.EqualTo(9));
        Assert.That(SComp<BloodstreamComponent>(patient).BleedAmount, Is.Zero);
        Assert.That(leg.Comp2.Bleeding, Is.Zero);
        Assert.That(leg.Comp2.Injury.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(15)));
        SEntMan.System<BloodstreamSystem>().TickBleed((patient, SComp<BloodstreamComponent>(patient)));
        Assert.That(leg.Comp2.Bleeding, Is.Zero);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void MultipleWoundsRespectBleedCapAndTreatmentDoesNotReopenThem()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var wounds = SEntMan.System<MedicalWoundSystem>();
        wounds.TryInjure(patient, "LegLeft", "Slash", 40f);
        wounds.TryInjure(patient, "LegRight", "Piercing", 40f);
        var parts = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient).ToArray();
        var blood = SComp<BloodstreamComponent>(patient);
        Assert.That(blood.BleedAmount, Is.EqualTo(blood.MaxBleedAmount));
        Assert.That(parts.Sum(p => p.Comp2.Bleeding), Is.EqualTo(blood.BleedAmount).Within(0.001f));
        SEntMan.System<BloodstreamSystem>().TryModifyBleedAmount((patient, blood), -5f);
        wounds.TryInjure(patient, "ArmLeft", "Blunt", 1f);
        Assert.That(blood.BleedAmount, Is.EqualTo(5f).Within(0.001f));
        Assert.That(parts.Sum(p => p.Comp2.Bleeding), Is.EqualTo(5f).Within(0.001f));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void TransplantIntoCappedBloodstreamKeepsLocalBleedingSynchronized()
    {
        var donor = SSpawn("MobMedicalTestHuman");
        var recipient = SSpawn("MobMedicalTestHuman");
        var wounds = SEntMan.System<MedicalWoundSystem>();
        wounds.TryInjure(donor, "LegLeft", "Slash", 50f);
        wounds.TryInjure(recipient, "ArmLeft", "Slash", 50f);
        var body = SEntMan.System<BodySystem>();
        var leg = body.EnumerateOrgans<MedicalWoundComponent>(donor).Single(p => p.Comp1.Category?.Id == "LegLeft");
        var containers = SEntMan.System<SharedContainerSystem>();
        containers.Remove(leg.Owner, containers.GetContainer(donor, BodyComponent.ContainerID));
        containers.Insert(leg.Owner, containers.GetContainer(recipient, BodyComponent.ContainerID));
        var blood = SComp<BloodstreamComponent>(recipient);
        Assert.That(body.EnumerateOrgans<MedicalWoundComponent>(recipient).Sum(p => p.Comp2.Bleeding),
            Is.EqualTo(blood.BleedAmount).Within(0.001f));
        Assert.That(blood.BleedAmount, Is.EqualTo(blood.MaxBleedAmount));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void HealingPreservesHundredthsAcrossUnequalWounds()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var wounds = SEntMan.System<MedicalWoundSystem>();
        wounds.TryInjure(patient, "LegLeft", "Slash", 0.01f);
        wounds.TryInjure(patient, "ArmRight", "Slash", 3f);
        SEntMan.System<DamageableSystem>().TryChangeDamage(patient,
            new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(-1) } }, true);
        var parts = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient).ToArray();
        Assert.That(parts.Sum(p => p.Comp2.Injury.GetTotal().Value), Is.EqualTo(201));
        SEntMan.System<DamageableSystem>().TryChangeDamage(patient,
            new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(-10) } }, true);
        Assert.That(parts.All(p => p.Comp2.Injury.GetTotal() == FixedPoint2.Zero), Is.True);
    }
}
