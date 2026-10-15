using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Medical;
using Content.Shared.Body;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Body;

public sealed class MedicalPersistenceTest : GameTest
{
    // Upstream mobs deliberately opt out of map saves. Enable it only for this fixture.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobMedicalTestHuman
  id: MedicalPersistencePatient
  save: true
";

    [Test]
    public async Task SaveLoadPreservesPhysicalPathologyAndDetachedAnatomy()
    {
        await Server.WaitAssertion(() =>
        {
            var maps = SEntMan.System<SharedMapSystem>();
            var loader = SEntMan.System<MapLoaderSystem>();
            var resources = Server.ResolveDependency<IResourceManager>();
            var path = new ResPath("/Maps/Test/MedicalPersistence.yml");
            resources.UserData.CreateDir(path.Directory);
            var mapEntity = maps.CreateMap(out var mapId);
            var patient = SSpawnAtPosition("MedicalPersistencePatient", new EntityCoordinates(mapEntity, System.Numerics.Vector2.Zero));
            var body = SEntMan.System<BodySystem>();
            var wounds = SEntMan.System<MedicalWoundSystem>();
            wounds.TryInjure(patient, "LegLeft", "Blunt", 60f);
            wounds.TryInjure(patient, "ArmRight", "Piercing", 60f);
            var leg = body.EnumerateOrgans<MedicalWoundComponent>(patient).Single(p => p.Comp1.Category?.Id == "LegLeft");
            leg.Comp2.Necrotic = true;
            leg.Comp2.IschemiaDebt = 180f;
            leg.Comp2.SurgicalAccess = 3;
            var brain = body.EnumerateOrgans<CerebralHealthComponent>(patient).Single();
            brain.Comp2.Damage = 42f;
            brain.Comp2.OxygenDebt = 16f;
            var heart = body.EnumerateOrgans<MedicalOrganComponent>(patient).Single(p => p.Comp2.Kind == MedicalOrganKind.Heart);
            heart.Comp2.Trauma = 30f;
            heart.Comp2.Fibrillating = true;
            var containers = SEntMan.System<Robust.Shared.Containers.SharedContainerSystem>();
            Assert.That(containers.Remove(leg.Owner, containers.GetContainer(patient, BodyComponent.ContainerID)), Is.True);
            var damage = SEntMan.System<DamageableSystem>().GetAllDamage(patient).GetTotal();
            var bed = SSpawnAtPosition("MedicalTestIntensiveCareBed", new EntityCoordinates(mapEntity, System.Numerics.Vector2.UnitX));
            SComp<MedicalLifeSupportComponent>(bed).GasSupply.Clear();
            Assert.That(loader.TrySaveMap(mapId, path), Is.True);
            maps.DeleteMap(mapId);
            Assert.That(loader.TryLoadMap(path, out var loaded, out _), Is.True);
            var loadedMap = loaded!.Value;
            try
            {
                var loadedPatient = SEntMan.EntityQuery<PhysiologyComponent, TransformComponent>(true)
                    .Single(p => p.Item2.MapID == loadedMap.Comp.MapId).Item2.Owner;
                Assert.That(SComp<PhysiologyStateComponent>(loadedPatient).AnatomyInitialized, Is.True);
                var parts = body.EnumerateOrgans<MedicalWoundComponent>(loadedPatient).ToArray();
                Assert.That(parts.Length, Is.EqualTo(8), "Loading must not regenerate the amputated leg and foot.");
                Assert.That(parts.Select(p => p.Comp1.Category).Distinct().Count(), Is.EqualTo(8));
                Assert.That(parts.Single(p => p.Comp1.Category?.Id == "ArmRight").Comp2.VascularDamage, Is.EqualTo(20f));
                var loadedBrain = body.EnumerateOrgans<CerebralHealthComponent>(loadedPatient).Single();
                Assert.That(loadedBrain.Comp2.Damage, Is.EqualTo(42f));
                Assert.That(loadedBrain.Comp2.OxygenDebt, Is.EqualTo(16f));
                var loadedHeart = body.EnumerateOrgans<MedicalOrganComponent>(loadedPatient).Single(p => p.Comp2.Kind == MedicalOrganKind.Heart);
                Assert.That(loadedHeart.Comp2.Trauma, Is.EqualTo(30f));
                Assert.That(loadedHeart.Comp2.Fibrillating, Is.True);
                Assert.That(SEntMan.System<DamageableSystem>().GetAllDamage(loadedPatient).GetTotal(), Is.EqualTo(damage));
                var detached = SEntMan.EntityQuery<MedicalWoundComponent, OrganComponent, TransformComponent>(true)
                    .Single(p => p.Item2.Category?.Id == "LegLeft" && p.Item3.MapID == loadedMap.Comp.MapId);
                Assert.That(detached.Item2.Body, Is.Null);
                Assert.That(detached.Item1.Fractured && detached.Item1.Necrotic, Is.True);
                Assert.That(detached.Item1.IschemiaDebt, Is.EqualTo(180f));
                Assert.That(detached.Item1.Injury.GetTotal(), Is.EqualTo(FixedPoint2.New(60)));
                Assert.That(detached.Item1.SurgicalAccess, Is.EqualTo(3));
                Assert.That(containers.GetContainer(detached.Item1.Owner, "medical_detached_parts").ContainedEntities.Count, Is.EqualTo(1));
                var loadedBed = SEntMan.EntityQuery<MedicalLifeSupportComponent, TransformComponent>(true)
                    .Single(p => p.Item2.MapID == loadedMap.Comp.MapId);
                Assert.That(loadedBed.Item1.GasSupply.TotalMoles, Is.Zero);
                Assert.That(containers.Insert(detached.Item1.Owner, containers.GetContainer(loadedPatient, BodyComponent.ContainerID)), Is.True);
                Assert.That(body.EnumerateOrgans<MedicalWoundComponent>(loadedPatient).Count(), Is.EqualTo(10));
            }
            finally
            {
                maps.DeleteMap(loadedMap.Comp.MapId);
            }
        });
    }
}
