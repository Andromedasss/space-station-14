using Content.Client.HealthAnalyzer.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Medical;
using Content.Shared.Medical;
using Content.Shared.Movement.Components;
using Robust.Client.UserInterface.Controls;
using System.Linq;
using System.Numerics;
using Content.Shared.Body;
using Content.Shared.Mobs.Systems;
using Content.Shared.Eye.Blinding.Systems;
using Robust.Shared.Map;
using Content.Shared.Inventory;
using Content.Shared.Atmos.Components;
using Content.Shared.Mobs;
using Robust.Shared.Containers;
using Content.Client.Paper.UI;
using Content.Shared.Paper;

namespace Content.IntegrationTests.Tests.Body;

public sealed class MedicalNeurologyInteractionTest : InteractionTest
{
    [TestCase("BulletPistol")]
    [TestCase("PelletShotgunSpread")]
    public async Task TremorChangesActualProjectileDirection(string projectilePrototype)
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var origin = SEntMan.GetComponent<Robust.Shared.GameObjects.TransformComponent>(patient).Coordinates;
            var gunSystem = SEntMan.System<Content.Server.Weapons.Ranged.Systems.GunSystem>();
            var random = Server.ResolveDependency<Robust.Shared.Random.IRobustRandom>();
            var physics = SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
            var physiology = SEntMan.System<PhysiologySystem>();
            var normalGun = SSpawnAtPosition("WeaponPistolViper", origin);
            var tremorGun = SSpawnAtPosition("WeaponPistolViper", origin);
            var normalBullet = SSpawnAtPosition(projectilePrototype, origin);
            var tremorBullet = SSpawnAtPosition(projectilePrototype, origin);
            var target = new EntityCoordinates(origin.EntityId, origin.Position + Vector2.UnitX * 10f);
            random.SetSeed(4321);
            gunSystem.Shoot((normalGun, SEntMan.GetComponent<Content.Shared.Weapons.Ranged.Components.GunComponent>(normalGun)),
                normalBullet,
                origin, target, out _, patient);
            Assert.That(physiology.TryIntervene(patient, "cognition", 8f), Is.True);
            random.SetSeed(4321);
            gunSystem.Shoot((tremorGun, SEntMan.GetComponent<Content.Shared.Weapons.Ranged.Components.GunComponent>(tremorGun)),
                tremorBullet,
                origin, target, out _, patient);
            var normalVelocity = physics.GetMapLinearVelocity(normalBullet);
            var tremorVelocity = physics.GetMapLinearVelocity(tremorBullet);
            Assert.That(tremorVelocity, Is.Not.EqualTo(normalVelocity));
            Assert.That(tremorVelocity.Length(), Is.EqualTo(normalVelocity.Length()).Within(0.001f));
        });
    }

    [Test]
    public async Task AlexiaHidesDocumentWithoutChangingItAndClearsOnBodySwitch()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() =>
        {
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget!.Value, "cognition", 2f);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, STarget.Value);
        });
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            using var window = new PaperWindow();
            var state = new PaperComponent.PaperBoundUserInterfaceState("Medical report", new(), PaperComponent.PaperAction.Write);
            window.Populate(state);
            Assert.That(window.Input.Visible, Is.False);
            Assert.That(Robust.Shared.Utility.Rope.Collapse(window.Input.TextRope), Is.EqualTo("Medical report"));
        });
        await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(ServerSession!, SPlayer));
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            using var window = new PaperWindow();
            window.Populate(new PaperComponent.PaperBoundUserInterfaceState("Medical report", new(), PaperComponent.PaperAction.Write));
            Assert.That(window.Input.Visible, Is.True);
        });
    }

    [Test]
    public async Task PhobiasIgnoreOccludedAndContainedStimuliAndRecognizeOtherTriggers()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var origin = SEntMan.GetComponent<Robust.Shared.GameObjects.TransformComponent>(patient).Coordinates;
            var neurology = SEntMan.System<MedicalNeurologySystem>();
            var behindWall = new EntityCoordinates(origin.EntityId, origin.Position + new Vector2(3f, 0f));
            var door = SSpawnAtPosition("Airlock", behindWall);
            var wall = SSpawnAtPosition("WallSolid", new EntityCoordinates(origin.EntityId, origin.Position + new Vector2(1.5f, 0f)));
            Assert.That(neurology.CountTriggers(patient, MedicalPhobia.Doors), Is.Zero);
            SEntMan.DeleteEntity(wall);
            Assert.That(neurology.CountTriggers(patient, MedicalPhobia.Doors), Is.EqualTo(1));
            var box = SSpawnAtPosition("ClosetTool", origin);
            var containers = SEntMan.System<SharedContainerSystem>();
            var hidden = containers.EnsureContainer<Robust.Shared.Containers.Container>(box, "medical_test_hidden");
            // World occluders cannot be parented inside containers in the engine.
            SEntMan.RemoveComponent<Robust.Shared.GameObjects.OccluderComponent>(door);
            Assert.That(containers.Insert(door, hidden), Is.True);
            Assert.That(neurology.CountTriggers(patient, MedicalPhobia.Doors), Is.Zero);
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var origin = SEntMan.GetComponent<Robust.Shared.GameObjects.TransformComponent>(patient).Coordinates;
            var neurology = SEntMan.System<MedicalNeurologySystem>();
            var person = SSpawnAtPosition("MobHuman", new EntityCoordinates(origin.EntityId, origin.Position + Vector2.UnitY));
            var inventory = SEntMan.System<InventorySystem>();
            var uniform = SSpawn("ClothingUniformJumpsuitSec");
            Assert.That(inventory.TryEquip(person, uniform, "jumpsuit", force: true), Is.True);
            Assert.That(neurology.CountTriggers(patient, MedicalPhobia.Uniforms), Is.EqualTo(1));
            var coat = SSpawn("ClothingOuterCoatLab");
            Assert.That(inventory.TryEquip(person, coat, "outerClothing", force: true), Is.True);
            Assert.That(neurology.CountTriggers(patient, MedicalPhobia.Uniforms), Is.Zero);
            SEntMan.GetComponent<FlammableComponent>(person).OnFire = true;
            Assert.That(neurology.CountTriggers(patient, MedicalPhobia.Fire), Is.EqualTo(1));
            SEntMan.System<MobStateSystem>().ChangeMobState(person, MobState.Dead);
            Assert.That(neurology.CountTriggers(patient, MedicalPhobia.Corpses), Is.EqualTo(1));
            SSpawnAtPosition("PuddleBlood", origin);
            SSpawnAtPosition("PuddleVomit", new EntityCoordinates(origin.EntityId, origin.Position + Vector2.UnitX));
            Assert.That(neurology.CountTriggers(patient, MedicalPhobia.Blood), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task VisibleDoorsBuildFearAndFaintingHasBoundedRecovery()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var origin = SEntMan.GetComponent<Robust.Shared.GameObjects.TransformComponent>(patient).Coordinates;
            var neurology = SEntMan.System<MedicalNeurologySystem>();
            Assert.That(SEntMan.System<PhysiologySystem>().TryIntervene(patient, "phobia", 1f), Is.True);
            SSpawnAtPosition("Airlock", new EntityCoordinates(origin.EntityId, origin.Position + Vector2.UnitX));
            Assert.That(neurology.CountTriggers(patient, MedicalPhobia.Doors), Is.EqualTo(1));
            SSpawnAtPosition("Airlock", new EntityCoordinates(origin.EntityId, origin.Position + Vector2.UnitY));
            Assert.That(neurology.CountTriggers(patient, MedicalPhobia.Doors), Is.EqualTo(2));
            SSpawnAtPosition("Airlock", new EntityCoordinates(origin.EntityId, origin.Position + Vector2.UnitX * 10));
            Assert.That(neurology.CountTriggers(patient, MedicalPhobia.Doors), Is.EqualTo(2));
            var eyes = SEntMan.System<BlindableSystem>();
            eyes.AdjustEyeDamage(patient, 9);
            Assert.That(neurology.CountTriggers(patient, MedicalPhobia.Doors), Is.Zero);
            eyes.AdjustEyeDamage(patient, -9);
            var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(patient).Single().Comp2;
            brain.Fear = 99f;
        });
        await RunSeconds(1f);
        await Server.WaitAssertion(() =>
        {
            var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(STarget!.Value).Single().Comp2;
            Assert.That(brain.FearUnconsciousSeconds, Is.InRange(3f, 5f));
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(STarget.Value), Is.True);
        });
        await RunSeconds(6f);
        await Server.WaitAssertion(() =>
        {
            var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(STarget!.Value).Single().Comp2;
            Assert.That(brain.FearUnconsciousSeconds, Is.Zero);
            Assert.That(brain.FearRecoverySeconds, Is.GreaterThan(20f));
            Assert.That(brain.Fear, Is.LessThanOrEqualTo(80f));
            Assert.That(brain.Damage, Is.Zero);
            Assert.That(SEntMan.System<MobStateSystem>().IsAlive(STarget.Value), Is.True);
            SEntMan.System<BlindableSystem>().AdjustEyeDamage(STarget.Value, 9);
        });
        await RunSeconds(12f);
        await Server.WaitAssertion(() =>
        {
            var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(STarget!.Value).Single().Comp2;
            Assert.That(brain.Fear, Is.Zero);
            Assert.That(brain.FearUnconsciousSeconds, Is.Zero);
        });
    }

    [Test]
    public async Task SymptomsReachClientAndHealthAnalyzer()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() =>
        {
            var physiology = SEntMan.System<PhysiologySystem>();
            physiology.TryIntervene(STarget!.Value, "vision", 3f);
            physiology.TryIntervene(STarget.Value, "aphasia", 1f);
            physiology.TryIntervene(STarget.Value, "weakness", 1f);
            physiology.TryIntervene(STarget.Value, "cognition", 31f);
            physiology.TryIntervene(STarget.Value, "phobia", 3f);
        });
        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(40);
        await Client.WaitAssertion(() =>
        {
            var patient = CEntMan.GetEntity(Target!.Value);
            var physiology = CEntMan.GetComponent<PhysiologyComponent>(patient);
            Assert.That(physiology.NeurologicalVision, Is.EqualTo(MedicalColorVision.Tritanopia));
            Assert.That(physiology.Aphasia && physiology.MuscleWeakness, Is.True);
            Assert.That(physiology.CognitiveSymptoms, Is.EqualTo((MedicalCognitiveSymptoms) 31));
            Assert.That(physiology.Phobia, Is.EqualTo(MedicalPhobia.Blood));
            Assert.That(CEntMan.GetComponent<MovementSpeedModifierComponent>(patient).WalkSpeedModifier,
                Is.EqualTo(0.7f).Within(0.001f));
            Assert.That(GetControlFromChildren<Label>(l => l.Text != null && l.Text.StartsWith("Abnormal neurological"),
                GetWindow<HealthAnalyzerWindow>()).Visible, Is.True);
            Assert.That(TryGetControlFromChildren<Label>(l => l.Text != null && l.Text.Contains("acquired tritanopia"),
                GetWindow<HealthAnalyzerWindow>(), out _), Is.False);
        });
    }
}
