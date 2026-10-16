using System.Linq;
using System.Numerics;
using Content.Client.Medical;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Medical;
using Content.Shared.Body;
using Content.Shared.Buckle;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Input;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Medical;
using Content.Shared.Stacks;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Body;

public sealed class MedicalPanelTest : InteractionTest
{
    [TestCase(1f)]
    [TestCase(1.5f)]
    public async Task SilhouetteHitTestingUsesContoursAndPaddedExtremities(float scale)
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(CVars.DisplayUIScale, scale));
        await RunTicks(15);
        var selector = GetControlFromChildren<MedicalBodySelector>(
            GetControlFromChildren<MedicalTargetingControl>(GetWidget<HotbarGui>(), true));
        await Client.WaitAssertion(() =>
        {
            // Exercise the UI manager's actual hit testing, not direct button event dispatch.
            (string Category, Vector2 Point)[] targets =
            [
                ("Head", new(60, 18)), ("Torso", new(60, 70)),
                ("Abdomen", new(60, 85)), ("Groin", new(60, 100)),
                ("ArmRight", new(28, 65)), ("ArmLeft", new(92, 65)),
                ("HandRight", new(6, 105)), ("HandLeft", new(114, 105)),
                ("LegRight", new(49, 140)), ("LegLeft", new(71, 140)),
                ("FootRight", new(32, 198)), ("FootLeft", new(88, 198)),
            ];
            foreach (var (category, point) in targets)
            {
                var hit = UiMan.MouseGetControl(new ScreenCoordinates(
                    selector.GlobalPixelPosition + point * selector.UIScale, selector.Window!.Id));
                Assert.That(hit, Is.SameAs(GetControlFromChildren<Button>(b => b.Name == category, selector)),
                    $"Hit testing {category} at {point}");
            }
            Vector2[] gaps = [new(46, 4), new(18, 41), new(60, 140), new(38, 75)];
            foreach (var point in gaps)
            {
                var hit = UiMan.MouseGetControl(new ScreenCoordinates(
                    selector.GlobalPixelPosition + point * selector.UIScale, selector.Window!.Id));
                Assert.That(hit, Is.Not.InstanceOf<Button>(), $"Empty silhouette area at {point}");
            }
        });
    }

    [Test]
    public async Task HudSelectsZoneBeforePatientAndTreatsChosenPart()
    {
        await AddAtmosphere();
        var hud = GetWidget<HotbarGui>();
        var control = GetControlFromChildren<MedicalTargetingControl>(hud, true);
        var button = GetControlFromChildren<Button>(b => b.Name == "LegLeft", control);
        await ClickControl(button);
        await RunTicks(15);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<MedicalTargetingComponent>(SPlayer).Category,
            Is.EqualTo("LegLeft")));
        await Client.WaitAssertion(() => Assert.That(button.Pressed, Is.True));
        await ClickControl(button);
        await RunTicks(15);
        await Client.WaitAssertion(() => Assert.That(button.Pressed, Is.True));
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() => SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "LegLeft", "Slash", 20f));
        await InteractUsing("Gauze", 10);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!.Injuries, Is.Empty));
        await Client.WaitAssertion(() => Assert.That(TryFindWindow<MedicalPanelWindow>(out _), Is.False));
        // Invalid client requests cannot write arbitrary organ categories into the aiming preference.
        await Client.WaitPost(() => CEntMan.EntityNetManager!.SendSystemNetworkMessage(new MedicalTargetZoneEvent("Heart")));
        await RunTicks(15);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<MedicalTargetingComponent>(SPlayer).Category,
            Is.EqualTo("LegLeft")));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task HudSelectionAimsLightMeleeWithoutChangingOrdinaryMedicine(bool experimental)
    {
        await AddAtmosphere();
        var control = GetControlFromChildren<MedicalTargetingControl>(GetWidget<HotbarGui>(), true);
        await ClickControl(GetControlFromChildren<Button>(b => b.Name == "ArmLeft", control));
        await RunTicks(15);
        await SpawnTarget(experimental ? "MobMedicalTestHuman" : "MobHuman");
        await PlaceInHands("Crowbar");
        await RunSeconds(2f);
        await AttemptLightAttack();
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(patient).Float(), Is.GreaterThan(0f));
            var scan = SEntMan.System<PhysiologySystem>().Scan(patient);
            if (!experimental)
            {
                Assert.That(scan, Is.Null);
                Assert.That(SEntMan.HasComponent<PhysiologyComponent>(patient), Is.False);
                return;
            }
            Assert.That(scan!.Injuries, Has.Count.EqualTo(1));
            Assert.That(scan.Injuries[0].Category, Is.EqualTo("ArmLeft"));
        });
    }

    [Test]
    public async Task HudAndSurgeryPanelShareReplicatedSelection()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await OpenPanel();
        var control = GetControlFromChildren<MedicalTargetingControl>(GetWidget<HotbarGui>(), true);
        await ClickControl(GetControlFromChildren<Button>(b => b.Name == "LegRight", control));
        await RunTicks(40);
        await Client.WaitAssertion(() =>
        {
            var window = GetWindow<MedicalPanelWindow>();
            Assert.That(GetControlFromChildren<Label>(l => l.Text != null && l.Text.StartsWith("Selected zone:"), window).Text,
                Does.Contain("LegRight"));
            Assert.That(TryGetControlFromChildren<MedicalBodySelector>(_ => true, window, out _), Is.False);
        });
        await ClickControl(GetControlFromChildren<Button>(b => b.Name == "Head", control));
        await RunTicks(40);
        await Client.WaitAssertion(() =>
        {
            Assert.That(GetControlFromChildren<Button>(b => b.Name == "Head", control).Pressed, Is.True);
            Assert.That(GetControlFromChildren<Button>(b => b.Name == "LegRight", control).Pressed, Is.False);
            Assert.That(GetControlFromChildren<Label>(l => l.Text != null && l.Text.StartsWith("Selected zone:"),
                GetWindow<MedicalPanelWindow>()).Text, Does.Contain("Head"));
        });
    }

    private async Task OpenPanel()
    {
        await PressKey(ContentKeyFunctions.OpenMedicalPanel);
        await RunTicks(40);
    }

    private async Task PanelStep(string tool, string action)
    {
        await PlaceInHands(tool);
        await RunTicks(40);
        var button = GetControlFromChildren<Button>(b => b.Text == $"Surgery: {action} Torso ", GetWindow<MedicalPanelWindow>());
        await Client.WaitAssertion(() => Assert.That(button.Disabled, Is.False));
        await ClickControl(button);
        await RunTicks(10);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await AwaitDoAfters();
        await RunTicks(40);
    }

    [Test]
    public async Task PanelUsesHudSelectionAndShowsMissingSelectedPart()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await OpenPanel();
        var control = GetControlFromChildren<MedicalTargetingControl>(GetWidget<HotbarGui>(), true);
        await ClickControl(GetControlFromChildren<Button>(b => b.Name == "ArmLeft", control));
        await RunTicks(40);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<MedicalTargetingComponent>(SPlayer).Category, Is.EqualTo("ArmLeft")));
        await Server.WaitPost(() =>
        {
            var part = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "ArmLeft");
            var containers = SEntMan.System<SharedContainerSystem>();
            Assert.That(containers.Remove(part.Owner, containers.GetContainer(STarget.Value, BodyComponent.ContainerID)), Is.True);
        });
        await RunTicks(40);
        await Client.WaitAssertion(() =>
        {
            var window = GetWindow<MedicalPanelWindow>();
            Assert.That(TryGetControlFromChildren<MedicalBodySelector>(_ => true, window, out _), Is.False);
            Assert.That(GetControlFromChildren<Label>(l => l.Text == "Selected part is absent. Select an attached part.", window).Visible, Is.True);
            // The HUD remains an aiming preference, not a scan of this patient's missing limbs.
            var hud = GetControlFromChildren<MedicalTargetingControl>(GetWidget<HotbarGui>(), true);
            Assert.That(GetControlFromChildren<Button>(b => b.Name == "ArmLeft", hud).Disabled, Is.False);
            Assert.That(GetControlFromChildren<Button>(b => b.Name == "HandLeft", hud).Disabled, Is.False);
        });
    }

    [Test]
    public async Task PanelPerformsStagedSurgeryAndExplicitOrganRepair()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await PlaceInHands("MedicalTestScalpel");
        await OpenPanel();
        await Client.WaitAssertion(() => Assert.That(GetControlFromChildren<Button>(
            b => b.Text == "Surgery: Incise Torso ", GetWindow<MedicalPanelWindow>()).ToolTip,
            Is.EqualTo("Buckle the patient to an experimental operating table.")));
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedBuckleSystem>()
            .TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True));
        await PanelStep("MedicalTestScalpel", "Incise");
        await PanelStep("MedicalTestHemostat", "Clamp");
        await PanelStep("MedicalTestRetractor", "Retract");
        await PanelStep("MedicalTestBoneSaw", "SawBone");
        await PanelStep("MedicalTestRetractor", "SpreadBone");
        await Server.WaitPost(() =>
        {
            foreach (var organ in SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget!.Value))
                organ.Comp2.Trauma = organ.Comp2.Kind == MedicalOrganKind.Heart ? 60f : 20f;
        });
        var kit = await PlaceInHands("MedicalTestOrganRepairKit", 10);
        await RunTicks(40);
        var repair = GetControlFromChildren<Button>(b => b.Text == "Repair internal organ: Lungs", GetWindow<MedicalPanelWindow>());
        await ClickControl(repair);
        await RunTicks(10);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await AwaitDoAfters();
        await Server.WaitAssertion(() =>
        {
            var organs = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget!.Value).ToArray();
            Assert.That(organs.Single(o => o.Comp2.Kind == MedicalOrganKind.Heart).Comp2.Trauma, Is.EqualTo(60f));
            Assert.That(organs.Single(o => o.Comp2.Kind == MedicalOrganKind.Lungs).Comp2.Trauma, Is.Zero);
            Assert.That(SEntMan.GetComponent<StackComponent>(SEntMan.GetEntity(kit)).Count, Is.EqualTo(9));
        });
        await PanelStep("MedicalTestRetractor", "ApproximateBone");
        await PanelStep("MedicalTestBoneSetter", "RepairBone");
        await PanelStep("MedicalTestRetractor", "Approximate");
        await PanelStep("MedicalTestCautery", "Close");
    }

    [Test]
    public async Task PanelExplainsWhyNonviableOrganCannotBeTransplanted()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.System<SharedBuckleSystem>()
                .TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True);
            SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget.Value)
                .Single(p => p.Comp1.Category?.Id == "Torso").Comp2.SurgicalAccess = 3;
            SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget.Value)
                .Single(p => p.Comp1.Category?.Id == "Torso").Comp2.BoneAccess = 2;
        });
        var heart = await PlaceInHands("OrganMedicalTestHeart");
        await Server.WaitPost(() => SEntMan.GetComponent<MedicalOrganViabilityComponent>(SEntMan.GetEntity(heart)).Damage = 100f);
        await OpenPanel();
        await Client.WaitAssertion(() =>
        {
            var button = GetControlFromChildren<Button>(b => b.Text == "Surgery: Insert Torso Heart", GetWindow<MedicalPanelWindow>());
            Assert.That(button.Disabled, Is.True);
            Assert.That(button.ToolTip, Is.EqualTo("Replacement is nonviable. Storage cannot restore dead tissue."));
        });
    }

    [Test]
    public async Task OldPatientButtonsCannotOperateOnNewPatient()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var old = STarget!.Value;
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(old, null, SEntMan.GetEntity(table)), Is.True));
        await PlaceInHands("MedicalTestScalpel");
        await OpenPanel();
        var stale = GetControlFromChildren<Button>(b => b.Text == "Surgery: Incise Torso ", GetWindow<MedicalPanelWindow>());
        await SpawnTarget("MobMedicalTestHuman");
        await OpenPanel();
        await ClickControl(stale);
        await RunTicks(20);
        Assert.That(ActiveDoAfters.Count(), Is.Zero);
        await Server.WaitAssertion(() =>
        {
            foreach (var patient in new[] { old, STarget!.Value })
                Assert.That(SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient)
                    .All(p => p.Comp2.SurgicalAccess == 0), Is.True);
        });
    }

    [TestCase("tool")]
    [TestCase("revision")]
    [TestCase("unbuckle")]
    public async Task StaleOperationCannotStartAfterConditionsChange(string change)
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedBuckleSystem>()
            .TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True));
        await PlaceInHands("MedicalTestScalpel");
        await OpenPanel();
        var stale = GetControlFromChildren<Button>(b => b.Text == "Surgery: Incise Torso ", GetWindow<MedicalPanelWindow>());
        if (change == "tool")
            await PlaceInHands("MedicalTestHemostat");
        else
            await Server.WaitPost(() =>
            {
                if (change == "unbuckle")
                    SEntMan.System<SharedBuckleSystem>().Unbuckle(STarget!.Value, null);
                else
                    SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                        .Single(p => p.Comp1.Category?.Id == "Torso").Comp2.SurgeryRevision++;
            });
        await ClickControl(stale);
        await RunTicks(15);
        Assert.That(ActiveDoAfters.Count(), Is.Zero);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<BodySystem>()
            .EnumerateOrgans<MedicalWoundComponent>(STarget!.Value).All(p => p.Comp2.SurgicalAccess == 0), Is.True));
    }

    [Test]
    public async Task PanelOperationCancelsWhenPatientLeavesTable()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedBuckleSystem>()
            .TryBuckle(STarget!.Value, null, SEntMan.GetEntity(table)), Is.True));
        await PlaceInHands("MedicalTestScalpel");
        await OpenPanel();
        await ClickControl(GetControlFromChildren<Button>(b => b.Text == "Surgery: Incise Torso ", GetWindow<MedicalPanelWindow>()));
        await RunTicks(10);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await Server.WaitPost(() => SEntMan.System<SharedBuckleSystem>().Unbuckle(STarget!.Value, null));
        await RunSeconds(6f);
        Assert.That(ActiveDoAfters.Count(), Is.Zero);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<BodySystem>()
            .EnumerateOrgans<MedicalWoundComponent>(STarget!.Value).All(p => p.Comp2.SurgicalAccess == 0), Is.True));
    }

    [Test]
    public async Task OrdinaryPatientCannotExposeExperimentalPanelControls()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        // Exercise server authorization even when bypassing the client-side opt-in check.
        await Client.WaitPost(() => CEntMan.System<Content.Client.Medical.MedicalPanelSystem>().Open(CEntMan.GetEntity(Target!.Value)));
        await RunTicks(40);
        await Client.WaitAssertion(() =>
        {
            var window = GetWindow<MedicalPanelWindow>();
            Assert.That(TryGetControlFromChildren<Button>(b => b.Text == "Torso", window, out _), Is.False);
            Assert.That(GetControlFromChildren<Label>(l => l.Text != null && l.Text.StartsWith("Patient unavailable."), window).Visible, Is.True);
        });
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<PhysiologyComponent>(STarget!.Value), Is.False));
    }

    [Test]
    public async Task ConcurrentSurgeonsCannotCompleteTheSameRevisionTwice()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        await PlaceInHands("MedicalTestScalpel");
        EntityUid other = default;
        Content.Shared.DoAfter.DoAfter first = default!;
        Content.Shared.DoAfter.DoAfter second = default!;
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(patient, null, SEntMan.GetEntity(table)), Is.True);
            other = SEntMan.SpawnEntity("InteractionTestMob", SEntMan.GetCoordinates(PlayerCoords));
            var tool = SEntMan.SpawnEntity("MedicalTestScalpel", SEntMan.GetCoordinates(PlayerCoords));
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(other, tool), Is.True);
            var part = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient)
                .Single(p => p.Comp1.Category?.Id == "Torso");
            var surgery = SEntMan.System<MedicalSurgerySystem>();
            MedicalSurgeryDoAfterEvent Snapshot() => new(SEntMan.GetNetEntity(part.Owner), table,
                null, MedicalSurgeryAction.Incise, part.Comp2.SurgeryRevision);
            surgery.Start(patient, SPlayer, hands.GetActiveItem(SPlayer)!.Value, Snapshot());
            surgery.Start(patient, other, tool, Snapshot());
            first = SEntMan.GetComponent<DoAfterComponent>(SPlayer).DoAfters.Values.Single();
            second = SEntMan.GetComponent<DoAfterComponent>(other).DoAfters.Values.Single();
        });
        await RunSeconds(6f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(new[] { first, second }.Count(a => a.Completed && !a.Cancelled), Is.EqualTo(1));
            var wound = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .Single(p => p.Comp1.Category?.Id == "Torso").Comp2;
            Assert.That(wound.SurgicalAccess, Is.EqualTo(1));
            Assert.That(wound.SurgeryRevision, Is.EqualTo(1));
            Assert.That(wound.Injury.GetTotal().Float(), Is.EqualTo(5f).Within(0.1f));
        });
    }

    [Test]
    public async Task ChangingControlledBodyRevokesOldPanelAuthorization()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var table = await Spawn("MedicalTestOperatingTable");
        var tool = await PlaceInHands("MedicalTestScalpel");
        MedicalPanelOperation operation = default!;
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(patient, null, SEntMan.GetEntity(table)), Is.True);
            var part = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient)
                .Single(p => p.Comp1.Category?.Id == "Torso");
            operation = new MedicalPanelOperation
            {
                Tool = tool,
                Surgery = new MedicalSurgeryDoAfterEvent(SEntMan.GetNetEntity(part.Owner), table,
                    null, MedicalSurgeryAction.Incise, part.Comp2.SurgeryRevision),
            };
        });
        await Client.WaitPost(() => CEntMan.EntityNetManager!.SendSystemNetworkMessage(new MedicalPanelRequestEvent(Target!.Value, 12345)));
        await RunTicks(10);
        await Server.WaitPost(() =>
        {
            var replacement = SEntMan.SpawnEntity("InteractionTestMob", SEntMan.GetCoordinates(PlayerCoords));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, replacement);
        });
        await RunTicks(10);
        await Client.WaitPost(() => CEntMan.EntityNetManager!.SendSystemNetworkMessage(new MedicalPanelOperateEvent(12345, operation)));
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<DoAfterComponent>(SPlayer).DoAfters, Is.Empty);
            Assert.That(SEntMan.GetComponent<DoAfterComponent>(ServerSession!.AttachedEntity!.Value).DoAfters, Is.Empty);
            Assert.That(SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(STarget!.Value)
                .All(p => p.Comp2.SurgicalAccess == 0), Is.True);
        });
    }
}
