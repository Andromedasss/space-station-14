using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared.Administration.Systems;
using Content.Server.Medical;
using Content.Shared.Body;
using Content.Shared.Medical;
using Content.Shared.Rejuvenate;
using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.Tests.Body;

public sealed class MedicalOrganViabilityTest : GameTest
{
    [Test]
    [RunOnSide(Side.Server)]
    public void StorageSlowsButCannotReverseInjuryAndExhaustsItsCoolant()
    {
        var warm = SSpawn("OrganMedicalTestHeart");
        var stored = SSpawn("OrganMedicalTestHeart");
        var brain = SSpawn("OrganMedicalTestBrain");
        var storage = SSpawn("MedicalTestOrganCase");
        var containers = SEntMan.System<SharedContainerSystem>();
        var container = containers.GetContainer(storage, StorageComponent.ContainerId);
        Assert.That(containers.Insert(stored, container), Is.True);
        Assert.That(containers.Insert(brain, container), Is.True);
        var viability = SEntMan.System<MedicalOrganViabilitySystem>();
        Assert.That(viability.ExposureSeconds(brain, 100f), Is.EqualTo(10f));
        viability.Update(100f);
        Assert.That(SComp<MedicalOrganViabilityComponent>(warm).Damage, Is.EqualTo(10f));
        Assert.That(SComp<MedicalOrganViabilityComponent>(stored).Damage, Is.EqualTo(1f));
        Assert.That(SComp<MedicalOrganPreservationComponent>(storage).RemainingSeconds, Is.EqualTo(500f));
        SEntMan.System<PhysiologySystem>().Update(100f);
        Assert.That(SComp<CerebralHealthComponent>(brain).OxygenDebt, Is.EqualTo(10f));
        SComp<MedicalOrganPreservationComponent>(storage).RemainingSeconds = 0f;
        viability.Update(100f);
        Assert.That(SComp<MedicalOrganViabilityComponent>(stored).Damage, Is.EqualTo(11f));
        Assert.That(viability.ExposureSeconds(brain, 100f), Is.EqualTo(100f));
        Assert.That(containers.Remove(stored, container), Is.True);
        Assert.That(viability.ExposureSeconds(stored, 10f), Is.EqualTo(10f));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void TransplantRetainsStorageInjuryAndRejectsNonviableReplacement()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var containers = SEntMan.System<SharedContainerSystem>();
        var container = containers.GetContainer(patient, BodyComponent.ContainerID);
        var outgoing = body.EnumerateOrgans<MedicalOrganComponent>(patient).Single(o => o.Comp2.Kind == MedicalOrganKind.Heart);
        Assert.That(containers.Remove(outgoing.Owner, container), Is.True);
        var replacement = SSpawn("OrganMedicalTestHeart");
        SComp<MedicalOrganViabilityComponent>(replacement).Damage = 50f;
        Assert.That(containers.Insert(replacement, container), Is.True);
        var physiology = SEntMan.System<PhysiologySystem>();
        Assert.That(physiology.Scan(patient)!.HeartFunction, Is.EqualTo(0.5f));
        Assert.That(physiology.Scan(patient)!.StorageInjuries.Single().Trauma, Is.EqualTo(50f));
        SEntMan.System<MedicalOrganViabilitySystem>().Update(100f);
        Assert.That(SComp<MedicalOrganViabilityComponent>(replacement).Damage, Is.EqualTo(50f));
        Assert.That(containers.Remove(replacement, container), Is.True);
        SComp<MedicalOrganViabilityComponent>(replacement).Damage = 100f;
        Assert.That(containers.Insert(replacement, container), Is.False);
        SEntMan.System<RejuvenateSystem>().PerformRejuvenate(replacement);
        Assert.That(containers.Insert(replacement, container), Is.True);
        Assert.That(physiology.Scan(patient)!.HeartFunction, Is.EqualTo(1f));
    }
}
