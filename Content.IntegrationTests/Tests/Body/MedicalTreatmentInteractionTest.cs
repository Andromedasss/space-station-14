using System.Linq;
using Content.Client.HealthAnalyzer.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Medical;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle;
using Content.Shared.Mobs.Systems;
using Content.Shared.Medical;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Stacks;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.Tests.Body;

public sealed class MedicalTreatmentInteractionTest : InteractionTest
{
    [TestCase("MobHuman", "MedicalTestOperatingTable")]
    [TestCase("MobMedicalTestHuman", "OperatingTable")]
    public async Task InternalTraumaTreatmentDoesNotApplyOutsideExperimentalSetup(string patientPrototype, string tablePrototype)
    {
        await AddAtmosphere();
        await SpawnTarget(patientPrototype);
        var table = await Spawn(tablePrototype);
        await Server.WaitPost(() =>
        {
            var patient = STarget!.Value;
            foreach (var organ in SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(patient))
                organ.Comp2.Trauma = 40f;
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(patient, null, SEntMan.GetEntity(table)), Is.True);
        });
        var kit = await PlaceInHands("MedicalTestOrganRepairKit", 10);
        await Interact(awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.Zero);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<StackComponent>(SEntMan.GetEntity(kit)).Count, Is.EqualTo(10));
            Assert.That(SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget!.Value)
                .All(p => p.Comp2.Trauma == 40f), Is.True);
        });
    }

    [TestCase("unbuckle")]
    [TestCase("bleeding")]
    [TestCase("organ removal")]
    [TestCase("brain death")]
    public async Task InternalTraumaTreatmentCancelsWhenConditionsChange(string change)
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() =>
        {
            var patient = STarget!.Value;
            Assert.That(SEntMan.System<MedicalWoundSystem>().TryInjure(patient, "Torso", "Blunt", 100f), Is.True);
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(patient, null, SEntMan.GetEntity(table)), Is.True);
        });
        var kit = await PlaceInHands("MedicalTestOrganRepairKit", 10);
        await Interact(awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        var action = ActiveDoAfters.Single();
        await Server.WaitPost(() =>
        {
            var patient = STarget!.Value;
            switch (change)
            {
                case "unbuckle":
                    SEntMan.System<SharedBuckleSystem>().Unbuckle(patient, null);
                    break;
                case "bleeding":
                    SEntMan.System<BloodstreamSystem>().TryModifyBleedAmount(patient, 1f);
                    break;
                case "organ removal":
                    var organ = SEntMan.GetEntity(((InternalTraumaTreatmentDoAfterEvent) action.Args.Event).Organ);
                    var containers = SEntMan.System<SharedContainerSystem>();
                    Assert.That(containers.Remove(organ, containers.GetContainer(patient, BodyComponent.ContainerID)), Is.True);
                    break;
                case "brain death":
                    SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(patient).Single().Comp2.Damage = 100f;
                    break;
            }
        });
        await RunTicks(600);
        await Server.WaitAssertion(() =>
        {
            Assert.That(action.Cancelled, Is.True);
            Assert.That(SEntMan.GetComponent<StackComponent>(SEntMan.GetEntity(kit)).Count, Is.EqualTo(10));
            Assert.That(SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget!.Value)
                .All(p => p.Comp2.Trauma == 40f), Is.True);
        });
    }

    [Test]
    public async Task InternalTraumaTreatmentRequiresTableHemostasisAndConsumesOnlyOnCompletion()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() => Assert.That(SEntMan.System<MedicalWoundSystem>()
            .TryInjure(STarget!.Value, "Torso", "Piercing", 60f), Is.True));
        await InteractUsing("MedicalTestOrganRepairKit", 10, awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.Zero);
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedBuckleSystem>()
            .TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True));
        await Interact(awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.Zero, "External bleeding must be stopped first.");
        await InteractUsing("Gauze", 10);
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<PhysiologySystem>().Update(60f);
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(STarget!.Value), Is.True);
        });
        var kit = await PlaceInHands("MedicalTestOrganRepairKit", 10);
        await Interact(awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await CancelDoAfters();
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<StackComponent>(SEntMan.GetEntity(kit)).Count, Is.EqualTo(10));
            Assert.That(SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget!.Value)
                .Sum(p => p.Comp2.Trauma), Is.EqualTo(80f));
        });
        await RunTicks(30);
        await Interact(awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await AwaitDoAfters();
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<BodySystem>()
            .EnumerateOrgans<MedicalOrganComponent>(STarget!.Value).Sum(p => p.Comp2.Trauma), Is.EqualTo(55f)));
        // Each application repairs only one organ and does not automatically repeat.
        await Interact();
        await Interact();
        await Interact();
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.OrganInjuries, Is.Empty);
            Assert.That(scan.HeartFunction, Is.EqualTo(1f));
            Assert.That(scan.LungFunction, Is.EqualTo(1f));
            Assert.That(SEntMan.GetComponent<StackComponent>(SEntMan.GetEntity(kit)).Count, Is.EqualTo(6));
        });
        await Interact(awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.Zero, "Healthy organs must not consume supplies.");
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<StackComponent>(SEntMan.GetEntity(kit)).Count, Is.EqualTo(6)));
        await RunTicks(900);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<MobStateSystem>().IsAlive(STarget!.Value), Is.True));
    }

    [Test]
    public async Task GauzeRepairsExternalWoundButScannerStillReportsInternalTrauma()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() => Assert.That(SEntMan.System<MedicalWoundSystem>()
            .TryInjure(STarget!.Value, "Torso", "Piercing", 60f), Is.True));
        await InteractUsing("Gauze", 10);
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var torso = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient)
                .Single(p => p.Comp1.Category?.Id == "Torso");
            Assert.That(torso.Comp2.Injury.GetTotal().Float(), Is.Zero);
            Assert.That(SEntMan.GetComponent<BloodstreamComponent>(patient).BleedAmount, Is.Zero);
            var scan = SEntMan.System<PhysiologySystem>().Scan(patient)!;
            Assert.That(scan.OrganInjuries.Count, Is.EqualTo(2));
            Assert.That(scan.OrganInjuries.All(p => p.Trauma == 40f), Is.True);
        });
        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(40);
        await Client.WaitAssertion(() =>
        {
            var window = GetWindow<HealthAnalyzerWindow>();
            foreach (var category in new[] { "Heart", "Lungs" })
                Assert.That(GetControlFromChildren<Label>(label => label.Text != null
                    && label.Text.Contains(category + ": internal trauma"), window).Text, Does.Contain("40.00/100"));
        });
    }

    [Test]
    public async Task AnalyzerInteractionDisplaysLocalizedPhysiologyOnClient()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() => SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "LegLeft", "Slash", 20f));
        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(40);
        await Client.WaitAssertion(() =>
        {
            var window = GetWindow<HealthAnalyzerWindow>();
            Assert.That(GetControlFromChildren<Label>(label => label.Text != null && label.Text.Contains("LegLeft"), window).Text,
                Does.Contain("injury 20"));
            Assert.That(GetControlFromChildren<Label>(label => label.Text != null && label.Text.Contains("not SpO2"), window).Visible, Is.True);
            Assert.That(GetControlFromChildren<Label>(label => label.Text != null && label.Text.Contains("Locomotor support"), window).Text,
                Does.Contain("92%"));
            var patient = CEntMan.GetEntity(Target!.Value);
            Assert.That(CEntMan.GetComponent<PhysiologyComponent>(patient).Mobility, Is.EqualTo(0.92f).Within(0.001f));
            CEntMan.System<MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(patient);
            Assert.That(CEntMan.GetComponent<MovementSpeedModifierComponent>(patient).WalkSpeedModifier,
                Is.EqualTo(0.92f).Within(0.001f));
        });
    }

    [Test]
    public async Task GauzeInteractionCanBeCancelledAndThenTreatLocalizedWounds()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "LegLeft", "Slash", 20f), Is.True);
        });
        await InteractUsing("Gauze", 10, awaitDoAfters: false);
        await CancelDoAfters();
        await Server.WaitAssertion(() =>
        {
            var leg = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "LegLeft");
            Assert.That(leg.Comp2.Injury.GetTotal().Float(), Is.EqualTo(20f));
        });
        await RunTicks(30);
        await Interact(awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await AwaitDoAfters();
        await Server.WaitAssertion(() =>
        {
            var leg = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "LegLeft");
            Assert.That(leg.Comp2.Injury.GetTotal() == 0 && leg.Comp2.Bleeding == 0f, Is.True,
                SEntMan.System<PhysiologySystem>().Inspect(STarget!.Value));
            Assert.That(SEntMan.GetComponent<BloodstreamComponent>(STarget!.Value).BleedAmount, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task WoundDrivenHypoxiaRequiresTreatmentBeforeBrainDeath(bool delayedTreatment)
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        // Repeated injuries sustain hemorrhage despite natural clotting and blood regeneration.
        for (var i = 0; i < 6; i++)
        {
            var damage = i == 0 ? 34f : 12f;
            await Server.WaitPost(() => Assert.That(SEntMan.System<MedicalWoundSystem>()
                .TryInjure(STarget!.Value, "LegLeft", "Piercing", damage), Is.True));
            await RunTicks(900);
        }
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            Assert.That(SEntMan.System<BloodstreamSystem>().GetBloodLevel(patient), Is.LessThan(0.5f));
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(patient), Is.True,
                SEntMan.System<PhysiologySystem>().Inspect(patient));
        });
        if (delayedTreatment)
        {
            await RunTicks(5400);
            await Server.WaitAssertion(() => Assert.That(SEntMan.System<MobStateSystem>().IsDead(STarget!.Value), Is.True));
        }
        await InteractUsing("Gauze", 10);
        await InteractUsing("Bloodpack", 10);
        if (delayedTreatment)
            await InteractUsing("Bloodpack", 10);
        await RunTicks(900);
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            Assert.That(SEntMan.GetComponent<BloodstreamComponent>(patient).BleedAmount, Is.Zero);
            Assert.That(SEntMan.System<BloodstreamSystem>().GetBloodLevel(patient), Is.GreaterThan(0.5f));
            var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(patient).Single().Comp2;
            Assert.That(brain.Damage >= 100f, Is.EqualTo(delayedTreatment));
            Assert.That(SEntMan.System<MobStateSystem>().IsAlive(patient), Is.EqualTo(!delayedTreatment),
                SEntMan.System<PhysiologySystem>().Inspect(patient));
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(patient), Is.EqualTo(delayedTreatment));
        });
    }

    [Test]
    public async Task BloodpackInteractionRestoresPerfusionAndConsciousnessAfterHemostasis()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() =>
        {
            var patient = STarget!.Value;
            var physiology = SEntMan.System<PhysiologySystem>();
            Assert.That(SEntMan.System<MedicalWoundSystem>().TryInjure(patient, "LegLeft", "Slash", 20f), Is.True);
            // Start at the post-hemorrhage stage; treatment and recovery use normal interactions and ticks.
            physiology.TryIntervene(patient, "blood", 0.25f);
            physiology.Update(31f);
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(patient), Is.True);
        });
        await InteractUsing("Gauze", 10);
        await InteractUsing("Bloodpack", 10);
        await RunTicks(300);
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            Assert.That(SEntMan.GetComponent<BloodstreamComponent>(patient).BleedAmount, Is.Zero);
            Assert.That(SEntMan.System<BloodstreamSystem>().GetBloodLevel(patient), Is.GreaterThan(0.5f));
            Assert.That(SEntMan.System<MobStateSystem>().IsAlive(patient), Is.True,
                SEntMan.System<PhysiologySystem>().Inspect(patient));
        });
    }
}
