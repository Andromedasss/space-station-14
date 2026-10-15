using System.Linq;
using Content.Client.HealthAnalyzer.UI;
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
            Assert.That(SEntMan.System<MobStateSystem>().IsAlive(STarget.Value), Is.True);
            SEntMan.GetComponent<MedicalLifeSupportComponent>(SEntMan.GetEntity(bed)).GasSupply.Clear();
        });
        await RunSeconds(20f);
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.OxygenDelivery, Is.Zero);
            Assert.That(scan.OxygenDebt, Is.GreaterThan(0f));
        });
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
            var torso = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "Torso");
            Assert.That(torso.Comp2.Injury.GetTotal().Float(), Is.InRange(19.8f, 20f));
            Assert.That(torso.Comp2.Bleeding, Is.GreaterThan(0f));
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
            && label.Text.Contains("LegLeft: fracture"), GetWindow<HealthAnalyzerWindow>()).Text,
            Does.Contain("Tissue repair cannot restore skeletal support")));
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

    [Test]
    public async Task InternalBleedingSurvivesDressingsAndStopsAfterVascularRepair()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() =>
        {
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
            Assert.That(leg.Comp2.Injury.GetTotal().Float(), Is.Zero);
            Assert.That(leg.Comp2.Bleeding, Is.Zero);
            Assert.That(leg.Comp2.VascularDamage, Is.EqualTo(20f));
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
        await Server.WaitPost(() => SEntMan.System<SharedVerbSystem>()
            .GetLocalVerbs(STarget!.Value, SPlayer, typeof(AlternativeVerb))
            .Single(v => v.Text.Trim() == "Surgery: Insert ArmLeft HandLeft").Act!());
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
        await PlaceInHands(tool);
        await Server.WaitPost(() =>
        {
            var verbs = SEntMan.System<SharedVerbSystem>().GetLocalVerbs(STarget!.Value, SPlayer, typeof(AlternativeVerb));
            var verb = verbs.Single(v => v.Text.Trim() == $"Surgery: {action} {part} {organ}".Trim());
            verb.Act!();
        });
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await AwaitDoAfters();
    }

    private async Task OpenSurgicalAccess(string part = "Torso")
    {
        await SurgeryStep("MedicalTestScalpel", "Incise", part);
        await SurgeryStep("MedicalTestHemostat", "Clamp", part);
        await SurgeryStep("MedicalTestRetractor", "Retract", part);
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
        await Server.WaitPost(() => SEntMan.System<SharedVerbSystem>()
            .GetLocalVerbs(STarget!.Value, SPlayer, typeof(AlternativeVerb))
            .Single(v => v.Text.Trim() == "Surgery: Insert Torso Heart").Act!());
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await AwaitDoAfters();
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<OrganComponent>(SEntMan.GetEntity(heart)).Body, Is.EqualTo(STarget!.Value));
            Assert.That(SEntMan.GetComponent<MedicalOrganComponent>(SEntMan.GetEntity(heart)).Trauma, Is.EqualTo(30f));
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!.HeartFunction, Is.EqualTo(0.7f).Within(0.001f));
        });
        await SurgeryStep("MedicalTestCautery", "Close");
    }

    [TestCase("unbuckle")]
    [TestCase("site removal")]
    public async Task SurgeryCancelsWithoutOpeningTissueWhenConditionsChange(string change)
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedBuckleSystem>()
            .TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True));
        await PlaceInHands("MedicalTestScalpel");
        await Server.WaitPost(() => SEntMan.System<SharedVerbSystem>()
            .GetLocalVerbs(STarget!.Value, SPlayer, typeof(AlternativeVerb))
            .Single(v => v.Text.Trim() == "Surgery: Incise ArmLeft").Act!());
        var action = ActiveDoAfters.Single();
        await Server.WaitPost(() =>
        {
            if (change == "unbuckle")
                SEntMan.System<SharedBuckleSystem>().Unbuckle(STarget!.Value, null);
            else
            {
                var site = SEntMan.GetEntity(((MedicalSurgeryDoAfterEvent) action.Args.Event).Part);
                var containers = SEntMan.System<SharedContainerSystem>();
                Assert.That(containers.Remove(site, containers.GetContainer(STarget!.Value, BodyComponent.ContainerID)), Is.True);
            }
        });
        await RunTicks(180);
        await Server.WaitAssertion(() =>
        {
            Assert.That(action.Cancelled, Is.True);
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
