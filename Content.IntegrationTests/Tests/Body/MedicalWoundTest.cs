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
using Content.Shared.ActionBlocker;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.Medical;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Stacks;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.Tests.Body;

[TestFixture]
[TestOf(typeof(MedicalWoundSystem))]
public sealed class MedicalWoundTest : GameTest
{
    [Test]
    [RunOnSide(Side.Server)]
    public void WoundVisualDataIsReplicatedPerBodyPart()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var parts = body.EnumerateOrgans<MedicalWoundComponent>(patient).ToArray();

        foreach (var part in parts)
        {
            // The replicated visual component is only created for parts that get wounded,
            // healthy parts must not carry the extra networked state.
            Assert.That(SEntMan.HasComponent<MedicalWoundVisualComponent>(part.Owner), Is.False,
                $"Healthy part {part.Comp1.Category?.Id} must not have replicated wound visual data.");
        }

        var arm = parts.Single(p => p.Comp1.Category?.Id == "ArmLeft");
        var leg = parts.Single(p => p.Comp1.Category?.Id == "LegRight");

        Assert.That(SEntMan.System<MedicalWoundSystem>().TryInjure(patient, "ArmLeft", "Slash", 60f), Is.True);

        Assert.That(SEntMan.HasComponent<MedicalWoundVisualComponent>(arm.Owner), Is.True,
            "The wounded part must have replicated wound visual data.");
        var armVisual = SEntMan.GetComponent<MedicalWoundVisualComponent>(arm.Owner);
        Assert.That(armVisual.TotalInjury, Is.GreaterThan(0f));
        Assert.That(armVisual.DominantGroup, Is.EqualTo("Brute"));

        Assert.That(SEntMan.HasComponent<MedicalWoundVisualComponent>(leg.Owner), Is.False,
            "Unwounded parts must not report injury.");
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void AbdominalOrgansArePhysicalPairedAndSeparateFromChestTrauma()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var parts = body.EnumerateOrgans<MedicalWoundComponent>(patient).ToArray();
        Assert.That(parts, Has.Length.EqualTo(12));
        var abdomen = parts.Single(p => p.Comp1.Category?.Id == "Abdomen");
        var organs = body.EnumerateOrgans<MedicalOrganComponent>(patient).ToArray();
        Assert.That(body.EnumerateOrgans<OrganComponent>(patient).Any(p => p.Comp1.Category?.Id == "Kidneys"), Is.False,
            "The paired experimental kidneys must replace the upstream combined organ.");
        Assert.That(organs.Count(p => p.Comp2.Kind == MedicalOrganKind.Kidney), Is.EqualTo(2));
        foreach (var organ in organs.Where(p => p.Comp2.ProtectedBy == "Abdomen"))
            Assert.That(SEntMan.GetComponent<ChildOrganComponent>(organ.Owner).Parent, Is.EqualTo(abdomen.Owner));
        SEntMan.System<MedicalWoundSystem>().TryInjure(patient, "Abdomen", "Piercing", 60f);
        Assert.That(organs.All(p => p.Comp2.Trauma == (p.Comp2.ProtectedBy == "Abdomen" ? 40f : 0f)), Is.True);
        Assert.That(SEntMan.System<PhysiologySystem>().Scan(patient)!.OrganFunctions
            .Single(p => p.Category == "KidneyLeft").Trauma, Is.EqualTo(60f).Within(0.001f));
        var containers = SEntMan.System<SharedContainerSystem>();
        var kidney = organs.Single(p => p.Comp1.Category?.Id == "KidneyLeft");
        Assert.That(containers.Remove(kidney.Owner, containers.GetContainer(patient, BodyComponent.ContainerID)), Is.True);
        Assert.That(body.EnumerateOrgans<MedicalOrganComponent>(patient).Count(p => p.Comp2.Kind == MedicalOrganKind.Kidney), Is.EqualTo(1));
        Assert.That(containers.Insert(kidney.Owner, containers.GetContainer(patient, BodyComponent.ContainerID)), Is.True);
        Assert.That(SEntMan.GetComponent<ChildOrganComponent>(kidney.Owner).Parent, Is.EqualTo(abdomen.Owner));
        Assert.That(kidney.Comp2.Trauma, Is.EqualTo(40f));
    }

    [TestCase("ArmLeft")]
    [TestCase("ArmRight")]
    [TestCase("HandLeft")]
    [TestCase("HandRight")]
    [TestCase("LegLeft")]
    [TestCase("LegRight")]
    [TestCase("FootLeft")]
    [TestCase("FootRight")]
    [RunOnSide(Side.Server)]
    public void RepeatedCrushingHitsCauseDeepThenExternalBleeding(string category)
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var wounds = SEntMan.System<MedicalWoundSystem>();
        var part = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient)
            .Single(p => p.Comp1.Category?.Id == category);
        for (var i = 0; i < 12; i++)
            wounds.TryInjure(patient, category, "Blunt", 10f);
        Assert.That(part.Comp2.Fractured, Is.True);
        Assert.That(part.Comp2.VascularDamage, Is.Zero);
        Assert.That(part.Comp2.Bleeding, Is.Zero);
        wounds.TryInjure(patient, category, "Blunt", 10f);
        Assert.That(part.Comp2.VascularDamage, Is.EqualTo(5f));
        Assert.That(MedicalWoundSystem.InternalBleeding(part.Comp2), Is.EqualTo(0.1f).Within(0.001f));
        Assert.That(part.Comp2.Bleeding, Is.Zero);
        for (var i = 0; i < 4; i++)
            wounds.TryInjure(patient, category, "Blunt", 10f);
        Assert.That(part.Comp2.VascularDamage, Is.EqualTo(25f));
        Assert.That(part.Comp2.Bleeding, Is.EqualTo(0.5f));
        SEntMan.System<BloodstreamSystem>().TryModifyBleedAmount(patient, -10f);
        SEntMan.System<DamageableSystem>().TryChangeDamage(patient,
            new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(-170) } }, true, targetPart: part.Owner);
        Assert.That(part.Comp2.Injury.GetTotal(), Is.EqualTo(FixedPoint2.Zero));
        Assert.That(part.Comp2.Bleeding, Is.Zero);
        Assert.That(part.Comp2.VascularDamage, Is.EqualTo(25f));
        Assert.That(part.Comp2.Fractured, Is.True);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void RepeatedTorsoHitsAccumulateOrganTraumaWithoutAffectingOtherZones()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var wounds = SEntMan.System<MedicalWoundSystem>();
        var organs = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(patient).ToArray();
        for (var i = 0; i < 10; i++)
            wounds.TryInjure(patient, "LegLeft", "Blunt", 10f);
        Assert.That(organs.All(o => o.Comp2.Trauma == 0f), Is.True);
        for (var i = 0; i < 2; i++)
            wounds.TryInjure(patient, "Torso", "Blunt", 10f);
        Assert.That(organs.All(o => o.Comp2.Trauma == 0f), Is.True);
        for (var i = 0; i < 8; i++)
            wounds.TryInjure(patient, "Torso", "Blunt", 10f);
        Assert.That(organs.All(o => o.Comp2.Trauma == (o.Comp2.ProtectedBy == "Torso" ? 40f : 0f)), Is.True);
        var scan = SEntMan.System<PhysiologySystem>().Scan(patient)!;
        Assert.That(scan.HeartFunction, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(scan.LungFunction, Is.EqualTo(0.6f).Within(0.001f));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void RepeatedHeadHitsFractureSkullAndKillPhysicalBrainBeforeGibbing()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var wounds = SEntMan.System<MedicalWoundSystem>();
        var body = SEntMan.System<BodySystem>();
        var head = body.EnumerateOrgans<MedicalWoundComponent>(patient).Single(p => p.Comp1.Category?.Id == "Head");
        var brain = body.EnumerateOrgans<CerebralHealthComponent>(patient).Single();
        for (var i = 0; i < 6; i++)
            Assert.That(wounds.TryInjure(patient, "Head", "Blunt", 10f), Is.True);
        Assert.That(head.Comp2.Fractured, Is.True);
        Assert.That(brain.Comp2.Damage, Is.EqualTo(10f));
        Assert.That(SEntMan.System<PhysiologySystem>().Scan(patient)!.Fractures, Does.Contain("Head"));
        for (var i = 0; i < 8; i++)
            wounds.TryInjure(patient, "Head", "Blunt", 10f);
        SEntMan.System<PhysiologySystem>().Update(0f);
        Assert.That(brain.Comp2.Damage, Is.EqualTo(50f));
        Assert.That(SEntMan.System<MobStateSystem>().IsCritical(patient), Is.True);
        for (var i = 0; i < 10; i++)
            wounds.TryInjure(patient, "Head", "Blunt", 10f);
        SEntMan.System<PhysiologySystem>().Update(0f);
        Assert.That(brain.Comp2.Damage, Is.EqualTo(100f));
        Assert.That(SEntMan.System<MobStateSystem>().IsDead(patient), Is.True);
        SEntMan.System<DamageableSystem>().TryChangeDamage(patient,
            new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(-240) } }, true, targetPart: head.Owner);
        Assert.That(brain.Comp2.Damage, Is.EqualTo(100f), "Healing scalp tissue cannot repair the physical brain.");
        Assert.That(head.Comp2.Fractured, Is.True);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void HeadTraumaDoesNotReachAnotherBrainOrComeFromLimbDamage()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var donor = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var brain = body.EnumerateOrgans<CerebralHealthComponent>(patient).Single();
        var otherBrain = body.EnumerateOrgans<CerebralHealthComponent>(donor).Single();
        var wounds = SEntMan.System<MedicalWoundSystem>();
        wounds.TryInjure(patient, "ArmLeft", "Blunt", 100f);
        Assert.That(brain.Comp2.Damage, Is.Zero);
        wounds.TryInjure(patient, "Head", "Piercing", 30f);
        Assert.That(brain.Comp2.Damage, Is.EqualTo(20f));
        Assert.That(otherBrain.Comp2.Damage, Is.Zero);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void SystemicHealingRepairsBiologicalInjuryButNotProstheticDamage()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var wounds = SEntMan.System<MedicalWoundSystem>();
        var damage = SEntMan.System<DamageableSystem>();
        var prosthetic = body.EnumerateOrgans<MedicalWoundComponent>(patient)
            .Single(p => p.Comp1.Category?.Id == "HandLeft");
        prosthetic.Comp2.Prosthetic = true;
        wounds.TryInjure(patient, "HandLeft", "Slash", 30f);
        wounds.TryInjure(patient, "ArmRight", "Slash", 10f);
        wounds.TryInjure(patient, "LegRight", "Slash", 20f);
        damage.TryChangeDamage(patient,
            new DamageSpecifier { DamageDict = { ["Bloodloss"] = FixedPoint2.New(10) } }, true);
        var repair = new DamageSpecifier
        {
            DamageDict = { ["Slash"] = FixedPoint2.New(-100), ["Bloodloss"] = FixedPoint2.New(-100) },
        };

        damage.TryChangeDamage(patient, repair, true);
        Assert.That(prosthetic.Comp2.Injury.GetTotal(), Is.EqualTo(FixedPoint2.New(30)));
        Assert.That(body.EnumerateOrgans<MedicalWoundComponent>(patient)
            .Where(p => !p.Comp2.Prosthetic).All(p => p.Comp2.Injury.GetTotal() == FixedPoint2.Zero), Is.True);
        Assert.That(damage.GetAllDamage(patient).DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(30)));
        Assert.That(damage.GetAllDamage(patient).DamageDict["Bloodloss"], Is.EqualTo(FixedPoint2.Zero));
        damage.TryChangeDamage(patient, repair, true);
        Assert.That(damage.GetAllDamage(patient).DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(30)));

        var ordinary = SSpawn("MobHuman");
        damage.TryChangeDamage(ordinary,
            new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(30) } }, true);
        damage.TryChangeDamage(ordinary, repair, true);
        Assert.That(damage.GetAllDamage(ordinary).GetTotal(), Is.EqualTo(FixedPoint2.Zero));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void LocalHealingCannotRepairAnotherPartOrUseForeignTissue()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var donor = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var wounds = SEntMan.System<MedicalWoundSystem>();
        var damage = SEntMan.System<DamageableSystem>();
        wounds.TryInjure(patient, "LegLeft", "Slash", 2f);
        wounds.TryInjure(patient, "ArmRight", "Slash", 10f);
        var leg = body.EnumerateOrgans<MedicalWoundComponent>(patient).Single(p => p.Comp1.Category?.Id == "LegLeft");
        var arm = body.EnumerateOrgans<MedicalWoundComponent>(patient).Single(p => p.Comp1.Category?.Id == "ArmRight");
        var foreign = body.EnumerateOrgans<MedicalWoundComponent>(donor).First();
        var repair = new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(-50) } };
        damage.TryChangeDamage(patient, repair, true, targetPart: leg.Owner);
        Assert.That(leg.Comp2.Injury.GetTotal(), Is.EqualTo(FixedPoint2.Zero));
        Assert.That(arm.Comp2.Injury.GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
        Assert.That(damage.GetAllDamage(patient).DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(10)));
        Assert.That(damage.TryChangeDamage(patient, repair, true, targetPart: foreign.Owner), Is.False);
        Assert.That(damage.GetAllDamage(patient).DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(10)));
    }

    [Test]
    public async Task DestroyedArmBlocksOnlyItsHandAndRepairRestoresUse()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SSpawnAtPosition("MobMedicalTestHuman", map.GridCoords);
            var hands = SEntMan.System<SharedHandsSystem>();
            var medical = SEntMan.System<MedicalHandSystem>();
            var blocker = SEntMan.System<ActionBlockerSystem>();
            var left = hands.EnumerateHands(patient).Single(id => hands.TryGetHand(patient, id, out var hand)
                && hand.Value.Location == HandLocation.Left);
            var right = hands.EnumerateHands(patient).Single(id => hands.TryGetHand(patient, id, out var hand)
                && hand.Value.Location == HandLocation.Right);
            hands.TrySetActiveHand(patient, left);
            var item = SSpawn("Gauze");
            Assert.That(hands.TryPickup(patient, item, left), Is.True);
            SEntMan.System<MedicalWoundSystem>().TryInjure(patient, "ArmLeft", "Blunt", 100f);
            Assert.That(SComp<PhysiologyComponent>(patient).LeftHandFunction, Is.Zero);
            Assert.That(hands.GetHeldItem(patient, left), Is.Null);
            Assert.That(hands.CanPickupToHand(patient, item, left), Is.False);
            Assert.That(blocker.CanAttack(patient), Is.False);
            Assert.That(medical.CanUseHand(patient, right), Is.True);
            hands.TrySetActiveHand(patient, right);
            Assert.That(blocker.CanAttack(patient), Is.True);
            Assert.That(hands.TryPickup(patient, item, right), Is.True);
            SEntMan.System<RejuvenateSystem>().PerformRejuvenate(patient);
            Assert.That(medical.CanUseHand(patient, left), Is.True);
            var handPart = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient)
                .Single(p => p.Comp1.Category?.Id == "HandLeft");
            var containers = SEntMan.System<SharedContainerSystem>();
            containers.Remove(handPart.Owner, containers.GetContainer(patient, BodyComponent.ContainerID));
            Assert.That(SComp<PhysiologyComponent>(patient).LeftHandFunction, Is.Zero);
            Assert.That(medical.CanUseHand(patient, left), Is.False);
        });
        await Server.WaitPost(() => SEntMan.DeleteEntity(map.MapUid));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void LocalSupportInjurySlowsMovementAndTissueRepairRestoresIt()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var wounds = SEntMan.System<MedicalWoundSystem>();
        var speed = SComp<MovementSpeedModifierComponent>(patient);
        Assert.That(SComp<PhysiologyComponent>(patient).Mobility, Is.EqualTo(1f).Within(0.001f));
        wounds.TryInjure(patient, "ArmLeft", "Blunt", 100f);
        Assert.That(speed.WalkSpeedModifier, Is.EqualTo(1f).Within(0.001f), "Arm injury is not leg injury.");
        wounds.TryInjure(patient, "LegLeft", "Slash", 50f);
        Assert.That(speed.WalkSpeedModifier, Is.EqualTo(0.8f).Within(0.001f));
        Assert.That(speed.SprintSpeedModifier, Is.EqualTo(0.8f).Within(0.001f));
        SEntMan.System<BloodstreamSystem>().TryModifyBleedAmount((patient, SComp<BloodstreamComponent>(patient)), -10f);
        Assert.That(speed.WalkSpeedModifier, Is.EqualTo(0.8f).Within(0.001f), "Hemostasis is not tissue repair.");
        SEntMan.System<DamageableSystem>().TryChangeDamage(patient,
            new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(-50) } }, true);
        Assert.That(speed.WalkSpeedModifier, Is.EqualTo(1f).Within(0.001f));
        wounds.TryInjure(patient, "FootRight", "Blunt", 100f);
        Assert.That(speed.WalkSpeedModifier, Is.EqualTo(0.6f).Within(0.001f));
        wounds.TryInjure(patient, "LegRight", "Blunt", 100f);
        Assert.That(speed.WalkSpeedModifier, Is.EqualTo(0.6f).Within(0.001f), "The same side must not be penalized twice.");
        wounds.TryInjure(patient, "LegLeft", "Blunt", 1000f);
        Assert.That(speed.WalkSpeedModifier, Is.EqualTo(0.2f).Within(0.001f));
        SEntMan.System<RejuvenateSystem>().PerformRejuvenate(patient);
        Assert.That(speed.WalkSpeedModifier, Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void MissingSupportAndWoundedTransplantsAffectOnlyExperimentalMobility()
    {
        var donor = SSpawn("MobMedicalTestHuman");
        var patient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var leg = body.EnumerateOrgans<MedicalWoundComponent>(patient).Single(p => p.Comp1.Category?.Id == "LegLeft");
        var replacement = body.EnumerateOrgans<MedicalWoundComponent>(donor).Single(p => p.Comp1.Category?.Id == "LegLeft");
        SEntMan.System<MedicalWoundSystem>().TryInjure(donor, "LegLeft", "Blunt", 50f);
        var containers = SEntMan.System<SharedContainerSystem>();
        var container = containers.GetContainer(patient, BodyComponent.ContainerID);
        containers.Remove(leg.Owner, container);
        Assert.That(SComp<PhysiologyComponent>(patient).Mobility, Is.EqualTo(0.6f).Within(0.001f));
        containers.Remove(replacement.Owner, containers.GetContainer(donor, BodyComponent.ContainerID));
        containers.Insert(replacement.Owner, container);
        Assert.That(SComp<PhysiologyComponent>(patient).Mobility, Is.EqualTo(0.8f).Within(0.001f));
        SEntMan.System<RejuvenateSystem>().PerformRejuvenate(replacement.Owner);
        Assert.That(SComp<PhysiologyComponent>(patient).Mobility, Is.EqualTo(1f).Within(0.001f));

        var upstream = SSpawn("MobHuman");
        SEntMan.System<DamageableSystem>().TryChangeDamage(upstream,
            new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(90) } }, true);
        Assert.That(SEntMan.HasComponent<PhysiologyComponent>(upstream), Is.True);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void FractureFollowsPhysicalPartAndRejuvenationRestoresSupport()
    {
        var donor = SSpawn("MobMedicalTestHuman");
        var recipient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var leg = body.EnumerateOrgans<MedicalWoundComponent>(donor).Single(p => p.Comp1.Category?.Id == "LegLeft");
        var oldLeg = body.EnumerateOrgans<MedicalWoundComponent>(recipient).Single(p => p.Comp1.Category?.Id == "LegLeft");
        SEntMan.System<MedicalWoundSystem>().TryInjure(donor, "LegLeft", "Blunt", 60f);
        SEntMan.System<DamageableSystem>().TryChangeDamage(donor,
            new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(-60) } }, true, targetPart: leg.Owner);
        Assert.That(leg.Comp2.Fractured, Is.True);
        Assert.That(leg.Comp2.Injury.GetTotal(), Is.EqualTo(FixedPoint2.Zero));
        var containers = SEntMan.System<SharedContainerSystem>();
        containers.Remove(leg.Owner, containers.GetContainer(donor, BodyComponent.ContainerID));
        containers.Remove(oldLeg.Owner, containers.GetContainer(recipient, BodyComponent.ContainerID));
        Assert.That(containers.Insert(leg.Owner, containers.GetContainer(recipient, BodyComponent.ContainerID)), Is.True);
        Assert.That(SComp<PhysiologyComponent>(recipient).Mobility, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(SEntMan.System<PhysiologySystem>().Scan(recipient)!.Fractures, Does.Contain("LegLeft"));
        SEntMan.System<RejuvenateSystem>().PerformRejuvenate(leg.Owner);
        Assert.That(leg.Comp2.Fractured, Is.False);
        Assert.That(SComp<PhysiologyComponent>(recipient).Mobility, Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void VascularInjuryFollowsTransplantAndRejuvenationClearsIt()
    {
        var donor = SSpawn("MobMedicalTestHuman");
        var recipient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var leg = body.EnumerateOrgans<MedicalWoundComponent>(donor).Single(p => p.Comp1.Category?.Id == "LegLeft");
        var oldLeg = body.EnumerateOrgans<MedicalWoundComponent>(recipient).Single(p => p.Comp1.Category?.Id == "LegLeft");
        SEntMan.System<MedicalWoundSystem>().TryInjure(donor, "LegLeft", "Piercing", 60f);
        SEntMan.System<BloodstreamSystem>().TryModifyBleedAmount(donor, -10f);
        Assert.That(leg.Comp2.VascularDamage, Is.EqualTo(20f));
        var containers = SEntMan.System<SharedContainerSystem>();
        containers.Remove(leg.Owner, containers.GetContainer(donor, BodyComponent.ContainerID));
        containers.Remove(oldLeg.Owner, containers.GetContainer(recipient, BodyComponent.ContainerID));
        Assert.That(containers.Insert(leg.Owner, containers.GetContainer(recipient, BodyComponent.ContainerID)), Is.True);
        Assert.That(SEntMan.System<PhysiologySystem>().Scan(recipient)!.VascularInjuries.Single().Category, Is.EqualTo("LegLeft"));
        Assert.That(MedicalWoundSystem.InternalBleeding(leg.Comp2), Is.EqualTo(0.4f).Within(0.001f));
        SEntMan.System<RejuvenateSystem>().PerformRejuvenate(leg.Owner);
        Assert.That(leg.Comp2.VascularDamage, Is.Zero);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void ProlongedLocalIschemiaCausesPersistentNecrosisButEarlyRepairPreventsIt()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var wounds = SEntMan.System<MedicalWoundSystem>();
        var physiology = SEntMan.System<PhysiologySystem>();
        var parts = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient).ToArray();
        var leg = parts.Single(p => p.Comp1.Category?.Id == "LegLeft");
        var arm = parts.Single(p => p.Comp1.Category?.Id == "ArmLeft");
        leg.Comp2.VascularDamage = 100f;
        arm.Comp2.VascularDamage = 100f;
        leg.Comp2.InternalBleedingPerDamage = 0f;
        arm.Comp2.InternalBleedingPerDamage = 0f;
        physiology.Scan(patient);
        wounds.Update(90f);
        Assert.That(leg.Comp2.Necrotic, Is.False);
        wounds.RepairVessel(arm.Comp2);
        wounds.Update(91f);
        Assert.That(leg.Comp2.Necrotic, Is.True);
        Assert.That(arm.Comp2.Necrotic, Is.False);
        wounds.RepairVessel(leg.Comp2);
        wounds.Update(180f);
        Assert.That(leg.Comp2.Necrotic, Is.True, "Restoring flow does not revive dead tissue.");
        Assert.That(SComp<PhysiologyComponent>(patient).Mobility, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(physiology.Scan(patient)!.NecroticParts, Does.Contain("LegLeft"));
        SEntMan.System<RejuvenateSystem>().PerformRejuvenate(patient);
        Assert.That(leg.Comp2.Necrotic, Is.False);
        Assert.That(SComp<PhysiologyComponent>(patient).Mobility, Is.EqualTo(1f).Within(0.001f));
    }

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
        Assert.That(parts, Has.Length.EqualTo(12));
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
        Assert.That(SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(upstream), Is.Not.Empty);
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
        Assert.That(SComp<BloodstreamComponent>(donor).BleedAmount, Is.EqualTo(4f).Within(0.001f));
        Assert.That(SEntMan.System<DamageableSystem>().GetAllDamage(donor).DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(20)));
        Assert.That(leg.Comp2.Bleeding, Is.EqualTo(3f).Within(0.001f));
        Assert.That(containers.Remove(oldLeg.Owner, containers.GetContainer(recipient, BodyComponent.ContainerID)), Is.True);
        Assert.That(containers.Insert(leg.Owner, containers.GetContainer(recipient, BodyComponent.ContainerID)), Is.True);
        Assert.That(SComp<BloodstreamComponent>(recipient).BleedAmount, Is.EqualTo(7f).Within(0.001f));
        Assert.That(leg.Comp2.Injury.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(10)));
        var damage = SEntMan.System<DamageableSystem>();
        Assert.That(damage.GetAllDamage(recipient).DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(10)));
        damage.TryChangeDamage(recipient,
            new DamageSpecifier { DamageDict = { ["Piercing"] = FixedPoint2.New(-10) } }, true);
        Assert.That(leg.Comp2.Injury.GetTotal(), Is.EqualTo(FixedPoint2.Zero));
        Assert.That(damage.GetAllDamage(recipient).DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(20)));
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
        Assert.That(wounds.TryInjure(SSpawn("MobMedicalTestHuman"), "LegLeft", "Slash", 10f), Is.True);
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
        var oldLeg = body.EnumerateOrgans<MedicalWoundComponent>(recipient).Single(p => p.Comp1.Category?.Id == "LegLeft");
        containers.Remove(oldLeg.Owner, containers.GetContainer(recipient, BodyComponent.ContainerID));
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

    [Test]
    [RunOnSide(Side.Server)]
    public void DetachedHandLeavesTreatableWoundOnAttachedArm()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var parts = body.EnumerateOrgans<MedicalWoundComponent>(patient).ToArray();
        var hand = parts.Single(p => p.Comp1.Category?.Id == "HandLeft");
        var arm = parts.Single(p => p.Comp1.Category?.Id == "ArmLeft");
        var containers = SEntMan.System<SharedContainerSystem>();
        var container = containers.GetContainer(patient, BodyComponent.ContainerID);
        Assert.That(containers.Remove(hand.Owner, container), Is.True);
        Assert.That(arm.Comp2.Injury.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(20)));
        Assert.That(SComp<BloodstreamComponent>(patient).BleedAmount, Is.EqualTo(4f).Within(0.001f));
        Assert.That(containers.Insert(hand.Owner, container), Is.True);
        Assert.That(arm.Comp2.Bleeding, Is.EqualTo(4f).Within(0.001f), "Container insertion is not surgical closure.");
        SEntMan.System<BloodstreamSystem>().TryModifyBleedAmount((patient, SComp<BloodstreamComponent>(patient)), -10f);
        Assert.That(arm.Comp2.Bleeding, Is.Zero);
        Assert.That(arm.Comp2.Injury.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(20)));
        SEntMan.System<RejuvenateSystem>().PerformRejuvenate(patient);
        Assert.That(arm.Comp2.Injury.Empty, Is.True);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void RemovingBodyComponentDoesNotCreateDetachmentWounds()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var parts = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient).ToArray();
        SEntMan.RemoveComponent<BodyComponent>(patient);
        Assert.That(parts.All(p => p.Comp2.Injury.Empty && p.Comp2.Bleeding == 0f), Is.True);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void AmputationCarriesDescendantsAndTransplantReparentsWithoutDuplicateSlots()
    {
        var donor = SSpawn("MobMedicalTestHuman");
        var recipient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var containers = SEntMan.System<SharedContainerSystem>();
        var donorContainer = containers.GetContainer(donor, BodyComponent.ContainerID);
        var recipientContainer = containers.GetContainer(recipient, BodyComponent.ContainerID);
        var leg = body.EnumerateOrgans<MedicalWoundComponent>(donor).Single(p => p.Comp1.Category?.Id == "LegLeft");
        var foot = body.EnumerateOrgans<MedicalWoundComponent>(donor).Single(p => p.Comp1.Category?.Id == "FootLeft");
        var oldLeg = body.EnumerateOrgans<MedicalWoundComponent>(recipient).Single(p => p.Comp1.Category?.Id == "LegLeft");
        var groin = body.EnumerateOrgans<MedicalWoundComponent>(recipient).Single(p => p.Comp1.Category?.Id == "Groin");
        SEntMan.System<MedicalWoundSystem>().TryInjure(donor, "FootLeft", "Piercing", 10f);

        Assert.That(containers.Remove(leg.Owner, donorContainer), Is.True);
        Assert.That(foot.Comp1.Body, Is.Null);
        Assert.That(SComp<ChildOrganComponent>(leg.Owner).Parent, Is.Null);
        Assert.That(SComp<ChildOrganComponent>(foot.Owner).Parent, Is.EqualTo(leg.Owner));
        Assert.That(body.EnumerateOrgans<MedicalWoundComponent>(donor).Any(p => p.Owner == foot.Owner), Is.False);
        Assert.That(foot.Comp2.Injury.GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
        Assert.That(containers.Insert(leg.Owner, recipientContainer), Is.False, "Occupied leg and foot cannot be duplicated.");
        Assert.That(foot.Comp1.Body, Is.Null, "A rejected transplant must not partly attach its descendants.");

        Assert.That(containers.Remove(oldLeg.Owner, recipientContainer), Is.True);
        Assert.That(containers.Insert(leg.Owner, recipientContainer), Is.True);
        Assert.That(foot.Comp1.Body, Is.EqualTo(recipient));
        Assert.That(SComp<ChildOrganComponent>(leg.Owner).Parent, Is.EqualTo(groin.Owner));
        Assert.That(SComp<ChildOrganComponent>(foot.Owner).Parent, Is.EqualTo(leg.Owner));
        Assert.That(body.EnumerateOrgans<MedicalWoundComponent>(recipient).Count(), Is.EqualTo(12));
        Assert.That(foot.Comp2.Injury.GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
        Assert.That(SEntMan.System<DamageableSystem>().GetAllDamage(recipient).DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(10)));

        Assert.That(containers.Remove(foot.Owner, recipientContainer), Is.True);
        Assert.That(leg.Comp2.Injury.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(20)), "New amputations wound the recipient's anatomy.");
        Assert.That(containers.Insert(foot.Owner, donorContainer), Is.False, "A foot needs an attached leg.");
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void HeadRemovalCarriesBrainAndDuplicateBrainIsRejected()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var containers = SEntMan.System<SharedContainerSystem>();
        var container = containers.GetContainer(patient, BodyComponent.ContainerID);
        var head = body.EnumerateOrgans<MedicalWoundComponent>(patient).Single(p => p.Comp1.Category?.Id == "Head");
        var brain = body.EnumerateOrgans<CerebralHealthComponent>(patient).Single();
        var extraBrain = SSpawn("OrganMedicalTestBrain");
        Assert.That(containers.Insert(extraBrain, container), Is.False);
        Assert.That(containers.Remove(head.Owner, container), Is.True);
        Assert.That(brain.Comp1.Body, Is.Null);
        Assert.That(body.EnumerateOrgans<CerebralHealthComponent>(patient), Is.Empty);
        Assert.That(containers.Insert(extraBrain, container), Is.False, "A brain needs an attached head.");
        Assert.That(containers.Insert(head.Owner, container), Is.True);
        Assert.That(body.EnumerateOrgans<CerebralHealthComponent>(patient).Single().Owner, Is.EqualTo(brain.Owner));
    }
}
