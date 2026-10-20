using System.Linq;
using Content.Client.HealthAnalyzer.UI;
using Content.Client.Medical;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Medical;
using Content.Server.Power.Components;
using Content.Server.Body.Systems;
using Content.Server.Body.Components;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle;
using Content.Shared.Mobs.Systems;
using Content.Shared.Medical;
using Content.Shared.Medical.HealthAnalyzer;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.Tests.Body;

public sealed class MedicalTreatmentInteractionTest : InteractionTest
{
    [Test]
    public async Task DirtyInstrumentContaminatesSiteAndPoweredSterilizerRestoresSterility()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedBuckleSystem>()
            .TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True));
        await SelectZone("ArmLeft");
        var scalpel = await PlaceInHands("MedicalTestScalpel");
        await Server.WaitPost(() => SEntMan.GetComponent<MedicalSurgeryToolComponent>(SEntMan.GetEntity(scalpel)).Sterile = false);
        await Interact(awaitDoAfters: false);
        await RunTicks(10);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await AwaitDoAfters();
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<BodySystem>()
            .EnumerateOrgans<MedicalWoundComponent>(STarget!.Value).Single(p => p.Comp1.Category?.Id == "ArmLeft")
            .Comp2.Contamination, Is.GreaterThanOrEqualTo(15f)));
        var sterilizer = await Spawn("MedicalTestSterilizer");
        await Server.WaitPost(() => SEntMan.GetComponent<ApcPowerReceiverComponent>(SEntMan.GetEntity(sterilizer)).Powered = false);
        await Interact(sterilizer, TargetCoords);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<MedicalSurgeryToolComponent>(SEntMan.GetEntity(scalpel)).Sterile, Is.False));
        await Server.WaitPost(() => SEntMan.GetComponent<ApcPowerReceiverComponent>(SEntMan.GetEntity(sterilizer)).Powered = true);
        await Interact(sterilizer, TargetCoords);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<MedicalSurgeryToolComponent>(SEntMan.GetEntity(scalpel)).Sterile, Is.True));
        await Drop();
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<MedicalSurgeryToolComponent>(SEntMan.GetEntity(scalpel)).Sterile, Is.False));
    }

    [Test]
    public async Task StationaryScannerDisplaysDetailedDiagnosisAndClearsItAfterEjection()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var scanner = await Spawn("MedicalScanner");
        await Server.WaitPost(() =>
        {
            var uid = SEntMan.GetEntity(scanner);
            var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(uid);
            power.NeedsPower = false;
            power.Powered = true;
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget!.Value, "aphasia", 1);
            SEntMan.System<MedicalScannerSystem>().InsertBody(uid, STarget.Value,
                SEntMan.GetComponent<Content.Server.Medical.Components.MedicalScannerComponent>(uid));
        });
        await Activate(scanner);
        await RunTicks(40);
        await Client.WaitAssertion(() => Assert.That(GetControlFromChildren<Label>(l => l.Text != null
            && l.Text.Contains("expressive aphasia"), GetWindow<HealthAnalyzerWindow>()).Text, Does.Contain("words omitted")));
        await Server.WaitPost(() =>
        {
            var uid = SEntMan.GetEntity(scanner);
            SEntMan.System<MedicalScannerSystem>().EjectBody(uid,
                SEntMan.GetComponent<Content.Server.Medical.Components.MedicalScannerComponent>(uid));
        });
        await RunTicks(40);
        await Client.WaitAssertion(() => Assert.That(TryGetControlFromChildren<Label>(l => l.Text != null
            && l.Text.Contains("expressive aphasia"), GetWindow<HealthAnalyzerWindow>(), out _), Is.False));
    }

    [Test]
    public async Task StationaryScannerRequiresPowerAndOccupantForDetailedDiagnosis()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var scanner = await Spawn("MedicalScanner");
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.GetEntity(scanner);
            var system = SEntMan.System<MedicalScannerSystem>();
            var component = SEntMan.GetComponent<Content.Server.Medical.Components.MedicalScannerComponent>(uid);
            var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(uid);
            power.Powered = true;
            Assert.That(system.GetDiagnosticState(uid).TargetEntity, Is.Null);
            SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "Torso", "Piercing", 60f);
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget.Value, "aphasia", 1);
            system.InsertBody(uid, STarget.Value, component);
            var primary = SEntMan.System<HealthAnalyzerSystem>().GetHealthAnalyzerUiState(STarget.Value, false).Physiology!;
            Assert.That(primary.Detailed, Is.False);
            Assert.That(primary.InternalWarning && primary.NeurologicalWarning, Is.True);
            Assert.That(primary.OrganInjuries, Is.Empty);
            Assert.That(primary.NeurologicalConditions, Is.Empty);
            Assert.That(primary.Aphasia, Is.False);
            var deep = system.GetDiagnosticState(uid).Physiology!;
            Assert.That(deep.Detailed, Is.True);
            Assert.That(deep.OrganInjuries, Has.Count.EqualTo(2));
            Assert.That(deep.Aphasia, Is.True);
            Assert.That(deep.NeurologicalConditions, Has.Count.EqualTo(1));
            power.Powered = false;
            Assert.That(system.GetDiagnosticState(uid).TargetEntity, Is.Null);
            power.Powered = true;
            system.EjectBody(uid, component);
            Assert.That(system.GetDiagnosticState(uid).TargetEntity, Is.Null);
        });
    }

    [Test]
    public async Task SurgicalHygienePreventsContaminationAndAntibioticTreatsEstablishedInfection()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedBuckleSystem>()
            .TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True));
        await SelectZone("ArmLeft");
        await InteractUsing("MedicalTestAntiseptic");
        var scalpel = await PlaceInHands("MedicalTestScalpel");
        await Interact(awaitDoAfters: false);
        await CancelDoAfters();
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<MedicalSurgeryToolComponent>(SEntMan.GetEntity(scalpel)).Sterile, Is.True));
        await RunTicks(30);
        await Interact();
        await Server.WaitAssertion(() =>
        {
            var wound = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "ArmLeft").Comp2;
            Assert.That(wound.Contamination, Is.Zero);
            Assert.That(SEntMan.GetComponent<MedicalSurgeryToolComponent>(SEntMan.GetEntity(scalpel)).Sterile, Is.False);
            var wounds = SEntMan.System<MedicalWoundSystem>();
            wounds.AdvanceInfection(STarget.Value, wound, 30);
            Assert.That(wound.Contamination, Is.Zero);
            wound.AntisepticSeconds = 0;
            for (var i = 0; i < 250; i++)
                wounds.AdvanceInfection(STarget.Value, wound, 1);
            Assert.That(wound.Infection, Is.GreaterThan(0));
            Assert.That(wound.Contamination, Is.GreaterThan(20));
        });
        await InteractUsing("MedicalTestAntiseptic");
        await Server.WaitAssertion(() =>
        {
            var wound = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "ArmLeft").Comp2;
            Assert.That(wound.Contamination, Is.Zero);
            Assert.That(wound.Infection, Is.GreaterThan(0), "Antiseptic must not erase established infection.");
            wound.SurgicalAccess = 0;
        });
        await InteractUsing("MedicalTestAntibioticMedipen");
        await Server.WaitAssertion(() =>
        {
            var wound = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "ArmLeft").Comp2;
            SEntMan.System<MedicalWoundSystem>().AdvanceInfection(STarget.Value, wound, 60);
            Assert.That(wound.Infection, Is.Zero);
            var ordinary = SSpawn("MobHuman");
            wound.Infection = 50;
            SEntMan.System<MedicalWoundSystem>().AdvanceInfection(ordinary, wound, 60);
            Assert.That(wound.Infection, Is.EqualTo(50));
            SEntMan.System<Content.Shared.Administration.Systems.RejuvenateSystem>().PerformRejuvenate(STarget.Value);
            Assert.That(wound.Infection + wound.Contamination + wound.AntisepticSeconds, Is.Zero);
        });
    }

    [Test]
    public async Task NeurorestorativeInjectionAcceleratesTemporaryRecoveryWithoutCuringPersistentDeficits()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() =>
        {
            var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(STarget!.Value).Single().Comp2;
            brain.CognitiveSymptoms = MedicalCognitiveSymptoms.Alexia | MedicalCognitiveSymptoms.Tremor;
            brain.RecoveryRemaining[MedicalNeurologicalCondition.Tremor] = 200;
        });
        await InteractUsing("MedicalTestNeurorestorativeMedipen");
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.NeurorestorativeDose, Is.GreaterThan(2));
            var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(STarget.Value).Single().Comp2;
            Assert.That(brain.RecoveryRemaining[MedicalNeurologicalCondition.Tremor], Is.LessThan(193));
            Assert.That(brain.CognitiveSymptoms.HasFlag(MedicalCognitiveSymptoms.Alexia), Is.True);
            Assert.That(scan.NeurologicalConditions.Single(c => c.Condition == MedicalNeurologicalCondition.Alexia).RecoverySeconds, Is.Zero);
        });
    }

    [Test]
    public async Task AnxiolyticInjectionControlsFearButDoesNotCurePhobia()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() =>
        {
            var origin = SEntMan.GetComponent<Robust.Shared.GameObjects.TransformComponent>(STarget!.Value).Coordinates;
            SSpawnAtPosition("Airlock", new Robust.Shared.Map.EntityCoordinates(origin.EntityId, origin.Position + System.Numerics.Vector2.UnitY));
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget.Value, "phobia", 1);
            SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(STarget.Value).Single().Comp2.Fear = 60;
        });
        await InteractUsing("MedicalTestAnxiolyticMedipen");
        await RunSeconds(8);
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.AnxiolyticDose, Is.GreaterThan(2));
            Assert.That(scan.Phobia, Is.EqualTo(MedicalPhobia.Doors));
            Assert.That(scan.Fear, Is.Zero);
            var blood = SEntMan.GetComponent<BloodstreamComponent>(STarget.Value);
            var solutions = SEntMan.System<Content.Shared.Chemistry.EntitySystems.SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(STarget.Value, blood.BloodSolutionName, out var solution, out _), Is.True);
            solutions.RemoveReagent(solution!.Value, "MedicalTestAnxiolytic", Content.Shared.FixedPoint.FixedPoint2.New(100));
        });
        await RunSeconds(2);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!.Fear, Is.GreaterThan(0)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NeuralRehabilitationRequiresExposedBrainAndConsumesDrugOnlyOnCompletion(bool cancel)
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True);
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget.Value, "aphasia", 1);
        });
        await InteractUsing("MedicalTestNeuralRehabilitationTool", awaitDoAfters: false);
        Assert.That(ActiveDoAfters, Is.Empty, "Closed skull cannot be treated.");
        await SelectZone("Head");
        await OpenSurgicalAccess("Head");
        await InteractUsing("MedicalTestNeuralRehabilitationTool", awaitDoAfters: false);
        Assert.That(ActiveDoAfters, Is.Empty, "Rehabilitation requires the drug.");
        await Server.WaitPost(() => Assert.That(SEntMan.System<BloodstreamSystem>().TryAddToBloodstream(STarget!.Value,
            new Content.Shared.Chemistry.Components.Solution([new("MedicalTestNeurorestorative", 5)])), Is.True));
        await Interact(awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        var action = ActiveDoAfters.Single();
        if (cancel)
            await Server.WaitPost(() => SEntMan.System<SharedBuckleSystem>().Unbuckle(STarget!.Value, null));
        await RunSeconds(21);
        await Server.WaitAssertion(() =>
        {
            var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(STarget!.Value).Single().Comp2;
            Assert.That(action.Cancelled, Is.EqualTo(cancel));
            Assert.That(brain.RecoveryRemaining.ContainsKey(MedicalNeurologicalCondition.Aphasia), Is.EqualTo(!cancel));
            Assert.That(brain.Aphasia, Is.True, "Rehabilitation begins recovery instead of instantly curing the deficit.");
            var dose = SEntMan.System<Content.Server.Medical.MedicalNeurologySystem>().Dose(STarget.Value, "MedicalTestNeurorestorative");
            if (cancel)
                Assert.That(dose, Is.GreaterThan(3.5f));
            else
                Assert.That(dose, Is.InRange(1.5f, 3f));
            Assert.That(brain.Damage, Is.Zero);
        });
    }

    [Test]
    public async Task DebugStationPowerSuppliesDisconnectedLifeSupportAndRespectsItsSwitch()
    {
        var bed = await Spawn("MedicalTestIntensiveCareBed");
        try
        {
            await Server.WaitPost(() => Server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.DebugStationPower, true));
            await RunTicks(10);
            await Server.WaitAssertion(() =>
            {
                var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(SEntMan.GetEntity(bed));
                Assert.That(power.Powered, Is.True);
                power.PowerDisabled = true;
            });
            await RunTicks(10);
            await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<ApcPowerReceiverComponent>(SEntMan.GetEntity(bed)).Powered, Is.False));
            await Server.WaitPost(() =>
            {
                SEntMan.GetComponent<ApcPowerReceiverComponent>(SEntMan.GetEntity(bed)).PowerDisabled = false;
                Server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.DebugStationPower, false);
            });
            await RunTicks(10);
            await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<ApcPowerReceiverComponent>(SEntMan.GetEntity(bed)).Powered, Is.False));
        }
        finally
        {
            await Server.WaitPost(() => Server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.DebugStationPower, false));
        }
    }

    [Test]
    public async Task LifeSupportVentilatesFromReservoirButEmptySupplyCannotPreventHypoxia()
    {
        // No ambient atmosphere: respiratory reserve must come from the finite machine reservoir.
        await SpawnTarget("MobMedicalTestHuman");
        var bed = await Spawn("MedicalTestIntensiveCareBed");
        await Server.WaitPost(() =>
        {
            var uid = SEntMan.GetEntity(bed);
            var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(uid);
            power.NeedsPower = false;
            power.Powered = true;
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(STarget!.Value, null, uid), Is.True);
        });
        await RunSeconds(20f);
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.OxygenDelivery, Is.GreaterThanOrEqualTo(0.5f));
            Assert.That(scan.LifeSupportGasMoles, Is.GreaterThan(0f));
            Assert.That(SEntMan.System<MobStateSystem>().IsAlive(STarget.Value), Is.True);
            SEntMan.GetComponent<MedicalLifeSupportComponent>(SEntMan.GetEntity(bed)).GasSupply.Clear();
        });
        await RunSeconds(20f);
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.OxygenDelivery, Is.Zero);
            Assert.That(scan.LifeSupport, Is.True);
            Assert.That(scan.LifeSupportGasMoles, Is.Zero);
            Assert.That(scan.OxygenDebt, Is.GreaterThan(0f));
        });
        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(40);
        await Client.WaitAssertion(() => Assert.That(GetControlFromChildren<Label>(
            label => label.Text != null && label.Text.StartsWith("Life support gas remaining:"),
            GetWindow<HealthAnalyzerWindow>()).Text, Does.Contain("0.00 mol")));
    }

    [Test]
    public async Task PoweredLifeSupportAssistsExistingHeartAndConsumesFiniteBreathingGas()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var bed = await Spawn("MedicalTestIntensiveCareBed");
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.GetEntity(bed);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(STarget!.Value, null, uid), Is.True);
            var physiology = SEntMan.System<PhysiologySystem>();
            physiology.TryIntervene(STarget.Value, "heart", 0f);
            physiology.Update(16f);
            var scan = physiology.Scan(STarget.Value)!;
            Assert.That(scan.LifeSupport, Is.True);
            Assert.That(scan.HeartFunction, Is.Zero, "Assistance does not repair the native heart.");
            Assert.That(scan.CirculatoryFunction, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(SEntMan.System<MobStateSystem>().IsAlive(STarget.Value), Is.True);
            var support = SEntMan.GetComponent<MedicalLifeSupportComponent>(uid);
            var before = support.GasSupply.TotalMoles;
            SEntMan.System<RespiratorSystem>().Inhale(STarget.Value);
            Assert.That(support.GasSupply.TotalMoles, Is.LessThan(before));
            SEntMan.GetComponent<ApcPowerReceiverComponent>(uid).Powered = false;
            physiology.Update(16f);
            Assert.That(physiology.Scan(STarget.Value)!.CirculatoryFunction, Is.Zero);
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(STarget.Value), Is.True);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
            physiology.Update(9f);
            Assert.That(SEntMan.System<MobStateSystem>().IsAlive(STarget.Value), Is.True);
            SEntMan.System<SharedBuckleSystem>().Unbuckle(STarget.Value, null);
            Assert.That(physiology.Scan(STarget.Value)!.LifeSupport, Is.False);
            Assert.That(physiology.Scan(STarget.Value)!.CirculatoryFunction, Is.Zero);
        });
    }

    [Test]
    public async Task LifeSupportCannotReplaceBloodDestroyedOrgansOrAnIrreversibleBrain()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var bed = await Spawn("MedicalTestIntensiveCareBed");
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.GetEntity(bed);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
            SEntMan.System<SharedBuckleSystem>().TryBuckle(STarget!.Value, null, uid);
            var physiology = SEntMan.System<PhysiologySystem>();
            physiology.TryIntervene(STarget.Value, "heart", 0f);
            physiology.TryIntervene(STarget.Value, "blood", 0f);
            Assert.That(physiology.Scan(STarget.Value)!.OxygenDelivery, Is.Zero);
            physiology.TryIntervene(STarget.Value, "blood", 1f);
            var heart = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget.Value)
                .Single(p => p.Comp2.Kind == MedicalOrganKind.Heart);
            heart.Comp2.Trauma = 100f;
            Assert.That(physiology.Scan(STarget.Value)!.CirculatoryFunction, Is.Zero);
            heart.Comp2.Trauma = 0f;
            physiology.TryIntervene(STarget.Value, "lungs", 0f);
            Assert.That(physiology.Scan(STarget.Value)!.OxygenDelivery, Is.Zero);
            physiology.TryIntervene(STarget.Value, "lungs", 1f);
            var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(STarget.Value).Single();
            brain.Comp2.Damage = 100f;
            physiology.Update(30f);
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(STarget.Value), Is.True);
            Assert.That(brain.Comp2.Damage, Is.EqualTo(100f));
            var containers = SEntMan.System<SharedContainerSystem>();
            containers.Remove(heart.Owner, containers.GetContainer(STarget.Value, BodyComponent.ContainerID));
            Assert.That(physiology.Scan(STarget.Value)!.CirculatoryFunction, Is.Zero);
            containers.Remove(brain.Owner, containers.GetContainer(STarget.Value, BodyComponent.ContainerID));
            physiology.Update(0f);
            Assert.That(physiology.Scan(STarget.Value)!.BrainMissing, Is.True);
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(STarget.Value), Is.True);
        });
    }

    private async Task SelectZone(string category)
    {
        await Client.WaitPost(() => CEntMan.System<Content.Client.Verbs.VerbSystem>()
            .ExecuteVerb(Target!.Value, new Verb { Text = $"Target zone: {category}" }));
        await RunTicks(10);
        await Client.WaitAssertion(() => Assert.That(CEntMan.GetComponent<MedicalTargetingComponent>(CPlayer).Category,
            Is.EqualTo(category)));
    }

    [TestCase("Torso", true)]
    [TestCase("Head", true)]
    [TestCase("Abdomen", false)]
    [TestCase("ArmLeft", false)]
    public async Task DirectToolsFollowHudZoneAndRequireReverseClosure(string category, bool bone)
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedBuckleSystem>()
            .TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True));
        var targeting = GetControlFromChildren<MedicalTargetingControl>(GetWidget<HotbarGui>(), true);
        await ClickControl(GetControlFromChildren<Button>(b => b.Name == category, targeting));
        await RunTicks(10);
        await InteractUsing("MedicalTestScalpel");
        await InteractUsing("MedicalTestHemostat");
        await InteractUsing("MedicalTestRetractor");
        await InteractUsing("MedicalTestCautery", awaitDoAfters: false);
        Assert.That(ActiveDoAfters, Is.Empty, "Cautery cannot close spread skin.");
        await Server.WaitAssertion(() =>
        {
            var site = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == category);
            Assert.That(site.Comp2.SurgicalAccess, Is.EqualTo(3));
            Assert.That(site.Comp2.InternalsExposed, Is.EqualTo(!bone));
            Assert.That(SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget.Value)
                .Where(p => p.Owner != site.Owner).All(p => p.Comp2.SurgicalAccess == 0), Is.True);
        });
        if (bone)
        {
            await InteractUsing("MedicalTestBoneSaw");
            await InteractUsing("MedicalTestRetractor");
            await Server.WaitAssertion(() => Assert.That(SEntMan.System<BodySystem>()
                .EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == category).Comp2.InternalsExposed, Is.True));
            await InteractUsing("MedicalTestCautery", awaitDoAfters: false);
            Assert.That(ActiveDoAfters, Is.Empty);
            await InteractUsing("MedicalTestRetractor");
            await InteractUsing("MedicalTestCautery", awaitDoAfters: false);
            Assert.That(ActiveDoAfters, Is.Empty, "Sawn bone must be fixed before closing skin.");
            await InteractUsing("MedicalTestBoneSetter");
        }
        await InteractUsing("MedicalTestRetractor");
        await InteractUsing("MedicalTestCautery");
        await Server.WaitAssertion(() =>
        {
            var site = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == category).Comp2;
            Assert.That(site.SurgicalAccess, Is.Zero);
            Assert.That(site.BoneAccess, Is.Zero);
            Assert.That(site.Injury.GetTotal().Float(), Is.GreaterThan(0f), "Closing does not erase tissue injury.");
        });
    }

    [Test]
    public async Task DefaultDressingCannotTreatDistantWounds()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() => SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "LegLeft", "Slash", 20f));
        var gauze = await PlaceInHands("Gauze", 10);
        await Interact(awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.Zero);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<StackComponent>(SEntMan.GetEntity(gauze)).Count, Is.EqualTo(10));
            var leg = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "LegLeft");
            Assert.That(leg.Comp2.Injury.GetTotal().Float(), Is.EqualTo(20f));
            Assert.That(leg.Comp2.Bleeding, Is.GreaterThan(0f));
        });
    }

    [Test]
    public async Task AnalyzerHasNoZoneSelectorAndTreatmentUsesHudSelection()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() => SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "LegLeft", "Slash", 20f));
        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(40);
        var control = GetControlFromChildren<MedicalTargetingControl>(GetWidget<HotbarGui>(), true);
        var button = GetControlFromChildren<Button>(b => b.Name == "LegLeft", control);
        await ClickControl(button);
        await RunTicks(70);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<MedicalTargetingComponent>(SPlayer).Category, Is.EqualTo("LegLeft")));
        await Client.WaitAssertion(() =>
        {
            var window = GetWindow<HealthAnalyzerWindow>();
            Assert.That(TryGetControlFromChildren<MedicalBodySelector>(_ => true, window, out _), Is.False);
            Assert.That(TryGetControlFromChildren<Button>(b => b.Text != null && b.Text.StartsWith("Target zone:"), window, out _), Is.False);
            Assert.That(button.Pressed, Is.True);
        });
        await InteractUsing("Gauze", 10);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!.Injuries, Is.Empty));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExplicitOrganRepairKeepsItsPhysicalTargetAndCancelsOnAccessRevision(bool changeAccess)
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedBuckleSystem>()
            .TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True));
        await OpenSurgicalAccess();
        var kit = await PlaceInHands("MedicalTestOrganRepairKit", 10);
        await Server.WaitPost(() =>
        {
            foreach (var organ in SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget!.Value))
                organ.Comp2.Trauma = organ.Comp2.Kind == MedicalOrganKind.Heart ? 60f : 20f;
        });
        await Client.WaitPost(() => CEntMan.System<Content.Client.Verbs.VerbSystem>()
            .ExecuteVerb(Target!.Value, new UtilityVerb { Text = "Repair internal organ: Lungs" }));
        await RunTicks(10);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await Server.WaitAssertion(() =>
        {
            var action = (InternalTraumaTreatmentDoAfterEvent) ActiveDoAfters.Single().Args.Event;
            Assert.That(SEntMan.GetComponent<MedicalOrganComponent>(SEntMan.GetEntity(action.Organ)).Kind, Is.EqualTo(MedicalOrganKind.Lungs));
            if (changeAccess)
                SEntMan.GetComponent<MedicalWoundComponent>(SEntMan.GetEntity(action.Part)).SurgeryRevision++;
        });
        if (changeAccess)
            await RunSeconds(10f);
        else
            await AwaitDoAfters();
        await Server.WaitAssertion(() =>
        {
            var organs = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget!.Value).ToArray();
            Assert.That(organs.Single(o => o.Comp2.Kind == MedicalOrganKind.Heart).Comp2.Trauma, Is.EqualTo(60f));
            Assert.That(organs.Single(o => o.Comp2.Kind == MedicalOrganKind.Lungs).Comp2.Trauma, Is.EqualTo(changeAccess ? 20f : 0f));
            Assert.That(SEntMan.GetComponent<StackComponent>(SEntMan.GetEntity(kit)).Count, Is.EqualTo(changeAccess ? 10 : 9));
        });
    }

    [Test]
    public async Task AnalyzerHasNoZoneControlsAfterDetachmentOrForOrdinaryPatients()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(40);
        await Client.WaitAssertion(() => Assert.That(TryGetControlFromChildren<MedicalBodySelector>(
            _ => true, GetWindow<HealthAnalyzerWindow>(), out _), Is.False));
        await Server.WaitPost(() =>
        {
            var arm = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "ArmLeft");
            var containers = SEntMan.System<SharedContainerSystem>();
            Assert.That(containers.Remove(arm.Owner, containers.GetContainer(STarget.Value, BodyComponent.ContainerID)), Is.True);
        });
        // A request from the old scan must not select a part which has already been detached.
        await Client.WaitPost(() => CEntMan.System<Content.Client.Verbs.VerbSystem>()
            .ExecuteVerb(Target!.Value, new Verb { Text = "Target zone: ArmLeft" }));
        await RunTicks(70);
        await Server.WaitAssertion(() => Assert.That(SEntMan.TryGetComponent<MedicalTargetingComponent>(SPlayer, out var selection)
            ? selection.Category : "Torso", Is.EqualTo("Torso")));
        await Client.WaitAssertion(() =>
        {
            var window = GetWindow<HealthAnalyzerWindow>();
            Assert.That(TryGetControlFromChildren<Button>(b => b.Text == "Target zone: ArmLeft", window, out _), Is.False);
            Assert.That(TryGetControlFromChildren<Button>(b => b.Text == "Target zone: HandLeft", window, out _), Is.False);
            Assert.That(TryGetControlFromChildren<MedicalBodySelector>(_ => true, window, out _), Is.False);
        });
        await SpawnTarget("MobHuman");
        await Interact();
        await RunTicks(40);
        await Client.WaitAssertion(() => Assert.That(TryGetControlFromChildren<Button>(
            b => b.Text != null && b.Text.StartsWith("Target zone:"), GetWindow<HealthAnalyzerWindow>(), out _), Is.False));
    }

    [Test]
    public async Task ExplicitOrganRepairIsDisabledWithoutAccessAndCannotBeForged()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True);
            foreach (var organ in SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget.Value))
                organ.Comp2.Trauma = 20f;
        });
        var kit = await PlaceInHands("MedicalTestOrganRepairKit", 10);
        await Server.WaitAssertion(() =>
        {
            var verbs = SEntMan.System<SharedVerbSystem>().GetLocalVerbs(STarget!.Value, SPlayer, typeof(UtilityVerb))
                .Where(v => v.Text.StartsWith("Repair internal organ:")).ToArray();
            Assert.That(verbs.Length, Is.EqualTo(6));
            Assert.That(verbs.All(v => v.Disabled && !string.IsNullOrEmpty(v.Message)), Is.True);
        });
        await Client.WaitPost(() => CEntMan.System<Content.Client.Verbs.VerbSystem>()
            .ExecuteVerb(Target!.Value, new UtilityVerb { Text = "Repair internal organ: Lungs" }));
        await RunTicks(10);
        Assert.That(ActiveDoAfters.Count(), Is.Zero);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget!.Value)
                .All(o => o.Comp2.Trauma == 20f), Is.True);
            Assert.That(SEntMan.GetComponent<StackComponent>(SEntMan.GetEntity(kit)).Count, Is.EqualTo(10));
        });
    }

    [Test]
    public async Task ZoneSelectionDoesNotChangeOrdinaryPatientHealing()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await SelectZone("LegLeft");
        await SpawnTarget("MobHuman");
        await Server.WaitPost(() => SEntMan.System<Content.Shared.Damage.Systems.DamageableSystem>()
            .TryChangeDamage(STarget!.Value, new Content.Shared.Damage.DamageSpecifier
            {
                DamageDict = { ["Slash"] = Content.Shared.FixedPoint.FixedPoint2.New(10) },
            }, true));
        await InteractUsing("Gauze", 10);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<Content.Shared.Damage.Systems.DamageableSystem>()
            .GetAllDamage(STarget!.Value).DamageDict["Slash"], Is.EqualTo(Content.Shared.FixedPoint.FixedPoint2.Zero)));
    }

    [Test]
    public async Task SelectedBloodpackStillRepairsSystemicBloodloss()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await SelectZone("ArmLeft");
        await Server.WaitPost(() => SEntMan.System<Content.Shared.Damage.Systems.DamageableSystem>()
            .TryChangeDamage(STarget!.Value, new Content.Shared.Damage.DamageSpecifier
            {
                DamageDict = { ["Bloodloss"] = Content.Shared.FixedPoint.FixedPoint2.New(1) },
            }, true));
        await InteractUsing("Bloodpack", 10);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<Content.Shared.Damage.Systems.DamageableSystem>()
            .GetAllDamage(STarget!.Value).DamageDict["Bloodloss"], Is.EqualTo(Content.Shared.FixedPoint.FixedPoint2.Zero)));
    }

    [Test]
    public async Task SelectedLightAttackInjuresOnlyChosenPhysicalPart()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await SelectZone("ArmLeft");
        await PlaceInHands("Crowbar");
        await RunSeconds(2);
        await AttemptLightAttack();
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            var parts = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value).ToArray();
            Assert.That(parts.Single(p => p.Comp1.Category?.Id == "ArmLeft").Comp2.Injury.GetTotal().Float(), Is.GreaterThan(0f));
            Assert.That(parts.Where(p => p.Comp1.Category?.Id != "ArmLeft").All(p => p.Comp2.Injury.Empty), Is.True);
        });
    }

    [Test]
    public async Task RemovingDressingSiteDoesNotTreatAnotherPartOrConsumeSupplies()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() => SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "LegLeft", "Slash", 20f));
        await SelectZone("LegLeft");
        var gauze = await PlaceInHands("Gauze", 10);
        await Interact(awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await Server.WaitPost(() =>
        {
            var leg = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "LegLeft");
            var containers = SEntMan.System<SharedContainerSystem>();
            containers.Remove(leg.Owner, containers.GetContainer(STarget!.Value, BodyComponent.ContainerID));
        });
        await RunTicks(180);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<StackComponent>(SEntMan.GetEntity(gauze)).Count, Is.EqualTo(10));
            var parts = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value).ToArray();
            var groin = parts.Single(p => p.Comp1.Category?.Id == "Groin");
            Assert.That(groin.Comp2.Injury.GetTotal().Float(), Is.InRange(19.8f, 20f));
            Assert.That(groin.Comp2.Bleeding, Is.GreaterThan(0f));
            Assert.That(parts.Where(p => p.Comp1.Category?.Id != "Groin").All(p => p.Comp2.Injury.Empty), Is.True);
        });
    }

    [Test]
    public async Task SelectedDressingKeepsPhysicalSiteAndDoesNotHealOtherWounds()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() =>
        {
            var wounds = SEntMan.System<MedicalWoundSystem>();
            wounds.TryInjure(STarget!.Value, "LegLeft", "Slash", 2f);
            wounds.TryInjure(STarget!.Value, "ArmRight", "Slash", 20f);
        });
        await SelectZone("LegLeft");
        var gauze = await PlaceInHands("Gauze", 10);
        await Interact(awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await SelectZone("ArmRight");
        await AwaitDoAfters();
        await Server.WaitAssertion(() =>
        {
            var parts = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value).ToArray();
            Assert.That(parts.Single(p => p.Comp1.Category?.Id == "LegLeft").Comp2.Injury.GetTotal().Float(), Is.Zero);
            Assert.That(parts.Single(p => p.Comp1.Category?.Id == "LegLeft").Comp2.Bleeding, Is.Zero);
            Assert.That(parts.Single(p => p.Comp1.Category?.Id == "ArmRight").Comp2.Injury.GetTotal().Float(), Is.EqualTo(20f));
            Assert.That(parts.Single(p => p.Comp1.Category?.Id == "ArmRight").Comp2.Bleeding, Is.GreaterThan(0f));
            Assert.That(SEntMan.GetComponent<StackComponent>(SEntMan.GetEntity(gauze)).Count, Is.EqualTo(9));
        });
    }

    [Test]
    public async Task FractureSurvivesTissueRepairAndRequiresExposedSurgicalFixation()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "LegLeft", "Blunt", 60f), Is.True);
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(STarget.Value, null, SEntMan.GetEntity(table)), Is.True);
        });
        await SelectZone("LegLeft");
        await InteractUsing("MedicatedSuture", 10);
        await Server.WaitAssertion(() =>
        {
            var leg = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "LegLeft");
            Assert.That(leg.Comp2.Injury.GetTotal().Float(), Is.Zero);
            Assert.That(leg.Comp2.Fractured, Is.True);
            Assert.That(SEntMan.GetComponent<PhysiologyComponent>(STarget.Value).Mobility, Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget.Value)!.Fractures, Does.Contain("LegLeft"));
        });
        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(40);
        await Client.WaitAssertion(() => Assert.That(GetControlFromChildren<Label>(label => label.Text != null
            && label.Text.StartsWith("Signs of possible internal"), GetWindow<HealthAnalyzerWindow>()).Visible, Is.True));
        await PlaceInHands("MedicalTestBoneSetter");
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<SharedVerbSystem>()
            .GetLocalVerbs(STarget!.Value, SPlayer, typeof(AlternativeVerb))
            .Any(v => v.Text.Contains("RepairFracture")), Is.False));
        await OpenSurgicalAccess("LegLeft");
        await SurgeryStep("MedicalTestBoneSetter", "RepairFracture", "LegLeft");
        await Server.WaitAssertion(() =>
        {
            var leg = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "LegLeft");
            Assert.That(leg.Comp2.Fractured, Is.False);
            Assert.That(leg.Comp2.Injury.GetTotal().Float(), Is.GreaterThan(0f), "Fixation must not heal the surgical incision.");
            Assert.That(SEntMan.GetComponent<PhysiologyComponent>(STarget.Value).Mobility, Is.GreaterThan(0.9f));
        });
        await SurgeryStep("MedicalTestCautery", "Close", "LegLeft");
        await InteractUsing("Gauze", 10);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<PhysiologyComponent>(STarget!.Value).Mobility,
            Is.EqualTo(1f).Within(0.001f)));
    }

    [Test]
    public async Task InterruptedFractureFixationDoesNotRestoreSupport()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() =>
        {
            SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "ArmLeft", "Blunt", 60f);
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(STarget.Value, null, SEntMan.GetEntity(table)), Is.True);
        });
        await OpenSurgicalAccess("ArmLeft");
        await PlaceInHands("MedicalTestBoneSetter");
        await Server.WaitPost(() => SEntMan.System<SharedVerbSystem>()
            .GetLocalVerbs(STarget!.Value, SPlayer, typeof(AlternativeVerb))
            .Single(v => v.Text.Trim() == "Surgery: RepairFracture ArmLeft").Act!());
        var action = ActiveDoAfters.Single();
        await Server.WaitPost(() => SEntMan.System<SharedBuckleSystem>().Unbuckle(STarget!.Value, null));
        await RunTicks(180);
        await Server.WaitAssertion(() =>
        {
            Assert.That(action.Cancelled, Is.True);
            var arm = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "ArmLeft");
            Assert.That(arm.Comp2.Fractured, Is.True);
            Assert.That(SEntMan.GetComponent<PhysiologyComponent>(STarget.Value).LeftHandFunction, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InternalBleedingSurvivesDressingsAndStopsAfterVascularRepair(bool crushing)
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() =>
        {
            if (crushing)
            {
                for (var i = 0; i < 20; i++)
                    SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "LegLeft", "Blunt", 10f);
            }
            else
                SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "LegLeft", "Piercing", 60f);
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(STarget.Value, null, SEntMan.GetEntity(table)), Is.True);
        });
        await SelectZone("LegLeft");
        await InteractUsing("Gauze", 10);
        float before = 0f;
        await Server.WaitAssertion(() =>
        {
            var leg = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "LegLeft");
            Assert.That(leg.Comp2.Injury.GetTotal().Float(), crushing ? Is.GreaterThan(190f) : Is.Zero);
            Assert.That(leg.Comp2.Bleeding, Is.Zero);
            Assert.That(leg.Comp2.VascularDamage, Is.EqualTo(crushing ? 40f : 20f));
            before = SEntMan.System<BloodstreamSystem>().GetBloodLevel(STarget.Value);
        });
        await RunTicks(600);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<BloodstreamSystem>().GetBloodLevel(STarget!.Value),
            Is.LessThan(before), "Internal hemorrhage must continue after external hemostasis."));
        await OpenSurgicalAccess("LegLeft");
        await SurgeryStep("MedicalTestVascularRepairTool", "RepairVessel", "LegLeft");
        await SurgeryStep("MedicalTestCautery", "Close", "LegLeft");
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!.VascularInjuries, Is.Empty);
            before = SEntMan.System<BloodstreamSystem>().GetBloodLevel(STarget.Value);
        });
        await RunTicks(600);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<BloodstreamSystem>().GetBloodLevel(STarget!.Value),
            Is.GreaterThanOrEqualTo(before)));
    }

    [Test]
    public async Task ProstheticReplacementRestoresHandAndRequiresMechanicalRepair()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() =>
        {
            var hand = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "HandLeft");
            hand.Comp2.Necrotic = true;
            SEntMan.System<MedicalWoundSystem>().TryInjure(STarget.Value, "HandLeft", "Blunt", 1f);
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(STarget.Value, null, SEntMan.GetEntity(table)), Is.True);
        });
        await OpenSurgicalAccess("ArmLeft");
        await SurgeryStep("MedicalTestExtractor", "Extract", "ArmLeft", "HandLeft");
        await SurgeryStep("MedicalTestHemostat", "Clamp", "ArmLeft");
        await PlaceInHands("OrganMedicalTestProstheticHandLeft");
        await Client.WaitPost(() => CEntMan.System<Content.Client.Verbs.VerbSystem>()
            .ExecuteVerb(Target!.Value, new AlternativeVerb { Text = "Surgery: Insert ArmLeft HandLeft" }));
        await RunTicks(10);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await AwaitDoAfters();
        await SurgeryStep("MedicalTestCautery", "Close", "ArmLeft");
        await SelectZone("ArmLeft");
        await InteractUsing("Gauze", 10);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<PhysiologyComponent>(STarget!.Value).LeftHandFunction,
            Is.EqualTo(1f).Within(0.001f)));
        await Server.WaitPost(() => SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "HandLeft", "Piercing", 100f));
        await SelectZone("HandLeft");
        await InteractUsing("Gauze", 10, awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.Zero);
        await Server.WaitAssertion(() =>
        {
            var hand = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "HandLeft");
            Assert.That(hand.Comp2.Prosthetic, Is.True);
            Assert.That(hand.Comp2.Bleeding, Is.Zero);
            Assert.That(hand.Comp2.VascularDamage, Is.Zero);
            Assert.That(hand.Comp2.Fractured, Is.False);
            Assert.That(SEntMan.GetComponent<PhysiologyComponent>(STarget.Value).LeftHandFunction, Is.Zero);
        });
        await SurgeryStep("MedicalTestProstheticRepairTool", "RepairProsthetic", "HandLeft");
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<PhysiologyComponent>(STarget!.Value).LeftHandFunction,
            Is.EqualTo(1f).Within(0.001f)));
    }

    private async Task SurgeryStep(string tool, string action, string part = "Torso", string organ = "")
    {
        if (action == "Close")
        {
            if (part is "Torso" or "Head")
            {
                await SurgeryStep("MedicalTestRetractor", "ApproximateBone", part);
                await SurgeryStep("MedicalTestBoneSetter", "RepairBone", part);
            }
            await SurgeryStep("MedicalTestRetractor", "Approximate", part);
        }
        await PlaceInHands(tool);
        await Client.WaitPost(() => CEntMan.System<Content.Client.Verbs.VerbSystem>()
            .ExecuteVerb(Target!.Value, new AlternativeVerb { Text = $"Surgery: {action} {part} {organ}" }));
        await RunTicks(10);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await AwaitDoAfters();
    }

    private async Task OpenSurgicalAccess(string part = "Torso")
    {
        await SurgeryStep("MedicalTestScalpel", "Incise", part);
        await SurgeryStep("MedicalTestHemostat", "Clamp", part);
        await SurgeryStep("MedicalTestRetractor", "Retract", part);
        if (part is "Torso" or "Head")
        {
            await SurgeryStep("MedicalTestBoneSaw", "SawBone", part);
            await SurgeryStep("MedicalTestRetractor", "SpreadBone", part);
        }
    }

    [Test]
    public async Task SurgeryExtractsAndReimplantsPhysicalHeartWithoutErasingTrauma()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        Robust.Shared.GameObjects.NetEntity heart = default;
        await Server.WaitPost(() =>
        {
            var patient = STarget!.Value;
            var physical = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(patient)
                .Single(p => p.Comp2.Kind == MedicalOrganKind.Heart);
            physical.Comp2.Trauma = 30f;
            heart = SEntMan.GetNetEntity(physical.Owner);
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(patient, null, SEntMan.GetEntity(table)), Is.True);
        });
        await PlaceInHands("MedicalTestExtractor");
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<SharedVerbSystem>()
            .GetLocalVerbs(STarget!.Value, SPlayer, typeof(AlternativeVerb))
            .Any(v => v.Text.Contains("Extract")), Is.False));
        await OpenSurgicalAccess();
        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(40);
        await Client.WaitAssertion(() => Assert.That(GetControlFromChildren<Label>(label => label.Text != null
            && label.Text.Contains("Torso: surgical access"), GetWindow<HealthAnalyzerWindow>()).Text,
            Does.Contain("3/3")));
        await SurgeryStep("MedicalTestExtractor", "Extract", "Torso", "Heart");
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<OrganComponent>(SEntMan.GetEntity(heart)).Body, Is.Null);
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!.HeartFunction, Is.Zero);
        });
        await Pickup(heart);
        await Client.WaitPost(() => CEntMan.System<Content.Client.Verbs.VerbSystem>()
            .ExecuteVerb(Target!.Value, new AlternativeVerb { Text = "Surgery: Insert Torso Heart" }));
        await RunTicks(10);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await AwaitDoAfters();
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<OrganComponent>(SEntMan.GetEntity(heart)).Body, Is.EqualTo(STarget!.Value));
            Assert.That(SEntMan.GetComponent<MedicalOrganComponent>(SEntMan.GetEntity(heart)).Trauma, Is.EqualTo(30f));
            var viability = SEntMan.GetComponent<MedicalOrganViabilityComponent>(SEntMan.GetEntity(heart)).Damage;
            Assert.That(viability, Is.GreaterThan(0f).And.LessThan(5f));
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!.HeartFunction,
                Is.EqualTo(0.7f * (1f - viability / 100f)).Within(0.001f));
        });
        await SurgeryStep("MedicalTestCautery", "Close");
    }

    [TestCase("unbuckle")]
    [TestCase("site removal")]
    [TestCase("tool deletion")]
    [TestCase("table deletion")]
    [TestCase("site deletion")]
    [TestCase("revision change")]
    public async Task SurgeryCancelsWithoutOpeningTissueWhenConditionsChange(string change)
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedBuckleSystem>()
            .TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True));
        await PlaceInHands("MedicalTestScalpel");
        await Client.WaitPost(() => CEntMan.System<Content.Client.Verbs.VerbSystem>()
            .ExecuteVerb(Target!.Value, new AlternativeVerb { Text = "Surgery: Incise ArmLeft " }));
        await RunTicks(10);
        var action = ActiveDoAfters.Single();
        await Server.WaitPost(() =>
        {
            var site = SEntMan.GetEntity(((MedicalSurgeryDoAfterEvent) action.Args.Event).Part);
            switch (change)
            {
                case "unbuckle":
                    SEntMan.System<SharedBuckleSystem>().Unbuckle(STarget!.Value, null);
                    break;
                case "site removal":
                    var containers = SEntMan.System<SharedContainerSystem>();
                    Assert.That(containers.Remove(site, containers.GetContainer(STarget!.Value, BodyComponent.ContainerID)), Is.True);
                    break;
                case "tool deletion":
                    SEntMan.DeleteEntity(action.Args.Used!.Value);
                    break;
                case "table deletion":
                    SEntMan.DeleteEntity(SEntMan.GetEntity(table));
                    break;
                case "site deletion":
                    SEntMan.DeleteEntity(site);
                    break;
                case "revision change":
                    SEntMan.GetComponent<MedicalWoundComponent>(site).SurgeryRevision++;
                    break;
            }
        });
        await RunTicks(180);
        await Server.WaitAssertion(() =>
        {
            Assert.That(action.Cancelled, Is.True);
            if (change == "site deletion")
                return;
            var site = SEntMan.GetEntity(((MedicalSurgeryDoAfterEvent) action.Args.Event).Part);
            Assert.That(SEntMan.GetComponent<MedicalWoundComponent>(site).SurgicalAccess, Is.Zero);
            Assert.That(SEntMan.GetComponent<MedicalWoundComponent>(site).Injury.Empty, Is.True);
        });
    }

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
        await OpenSurgicalAccess();
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
                .All(p => p.Comp2.Trauma == (p.Comp2.ProtectedBy == "Torso" ? 40f : 0f)), Is.True);
        });
    }

    [Test]
    public async Task SurgicalIntestinalRepairStopsContaminationWithoutErasingExistingBurden()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedBuckleSystem>()
            .TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True));
        await SelectZone("Abdomen");
        await OpenSurgicalAccess("Abdomen");
        await Server.WaitAssertion(() =>
        {
            var intestine = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget!.Value)
                .Single(p => p.Comp2.Kind == MedicalOrganKind.Intestines);
            intestine.Comp2.Trauma = 70f;
            intestine.Comp2.Perforated = true;
            SEntMan.GetComponent<PhysiologyStateComponent>(STarget.Value).Contamination = 25f;
        });
        await SurgeryStep("MedicalTestCautery", "Close", "Abdomen");
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<BodySystem>()
            .EnumerateOrgans<MedicalOrganComponent>(STarget!.Value)
            .Single(p => p.Comp2.Kind == MedicalOrganKind.Intestines).Comp2.Perforated, Is.True));
        await OpenSurgicalAccess("Abdomen");
        var kit = await PlaceInHands("MedicalTestOrganRepairKit", 10);
        await Interact();
        await Server.WaitAssertion(() =>
        {
            var intestine = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget!.Value)
                .Single(p => p.Comp2.Kind == MedicalOrganKind.Intestines);
            Assert.That(intestine.Comp2.Trauma, Is.EqualTo(45f));
            Assert.That(intestine.Comp2.Perforated, Is.False);
            Assert.That(SEntMan.GetComponent<StackComponent>(SEntMan.GetEntity(kit)).Count, Is.EqualTo(9));
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget.Value)!.Contamination, Is.GreaterThan(0f));
        });
        await SurgeryStep("MedicalTestCautery", "Close", "Abdomen");
        var before = 0f;
        await Server.WaitPost(() => before = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!.Contamination);
        await RunSeconds(10f);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<PhysiologySystem>()
            .Scan(STarget!.Value)!.Contamination, Is.LessThan(before)));
    }

    [Test]
    public async Task InternalTraumaTreatmentRequiresTableHemostasisAndConsumesOnlyOnCompletion()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() => Assert.That(SEntMan.System<MedicalWoundSystem>()
            .TryInjure(STarget!.Value, "Torso", "Piercing", 60f), Is.True));
        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(40);
        await Client.WaitAssertion(() =>
        {
            Assert.That(GetControlFromChildren<Label>(label => label.Text != null
                && label.Text.StartsWith("Signs of possible internal"), GetWindow<HealthAnalyzerWindow>()).Visible, Is.True);
        });
        await InteractUsing("MedicalTestOrganRepairKit", 10, awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.Zero);
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedBuckleSystem>()
            .TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True));
        await Interact(awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.Zero, "External bleeding must be stopped first.");
        await InteractUsing("Gauze", 10);
        await InteractUsing("MedicalTestOrganRepairKit", 10, awaitDoAfters: false);
        Assert.That(ActiveDoAfters.Count(), Is.Zero, "Dressings cannot substitute for surgical access.");
        await OpenSurgicalAccess();
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
        await SurgeryStep("MedicalTestCautery", "Close");
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<BodySystem>()
            .EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
            .Single(p => p.Comp1.Category?.Id == "Torso").Comp2.SurgicalAccess, Is.Zero));
        await RunTicks(900);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<MobStateSystem>().IsAlive(STarget!.Value), Is.True));
        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(40);
        await Client.WaitAssertion(() => Assert.That(TryGetControlFromChildren<Label>(label => label.Text != null
            && label.Text.StartsWith("Heart:"), GetWindow<HealthAnalyzerWindow>(), out _), Is.False));
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
            Assert.That(GetControlFromChildren<Label>(label => label.Text != null
                && label.Text.StartsWith("Signs of possible internal"), window).Visible, Is.True);
            Assert.That(TryGetControlFromChildren<Label>(label => label.Text != null
                && label.Text.Contains("Heart: internal trauma"), window, out _), Is.False);
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
            Assert.That(GetControlFromChildren<Label>(label => label.Text != null && label.Text.StartsWith("LegLeft:"), window).Text,
                Does.Contain("injury 20"));
            Assert.That(GetControlFromChildren<Label>(label => label.Text == "Localized tissue injuries", window).Visible, Is.True);
            Assert.That(GetControlFromChildren<Label>(label => label.Text != null && label.Text.Contains("not SpO2"), window).Visible, Is.True);
            Assert.That(GetControlFromChildren<Label>(label => label.Text != null && label.Text.Contains("Locomotor support"), window).Text,
                Does.Contain("92%"));
            var patient = CEntMan.GetEntity(Target!.Value);
            Assert.That(CEntMan.GetComponent<PhysiologyComponent>(patient).Mobility, Is.EqualTo(0.92f).Within(0.001f));
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
        await SelectZone("LegLeft");
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
        // Cooling during shock delays hypoxic unconsciousness; wait for the clinical state,
        // not the old fixed normothermic deadline.
        for (var i = 0; i < 12; i++)
        {
            var critical = false;
            await Server.WaitPost(() => critical = SEntMan.System<MobStateSystem>().IsCritical(STarget!.Value));
            if (critical)
                break;
            await RunSeconds(5f);
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
        await SelectZone("LegLeft");
        await InteractUsing("Gauze", 10);
        await InteractUsing("Bloodpack", 10);
        // The longer untreated hemorrhage now requires a second pack in both scenarios.
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
        await SelectZone("LegLeft");
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
