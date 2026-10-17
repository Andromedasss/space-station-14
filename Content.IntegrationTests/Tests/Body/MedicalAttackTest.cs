using System.Linq;
using Content.Client.HealthAnalyzer.UI;
using Content.Client.Medical;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.IntegrationTests.Tests.Helpers;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Medical;
using Content.Shared.Body;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.Medical;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee.Events;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.Tests.Body;

public sealed class MedicalAttackTest : InteractionTest
{
    public sealed class MedicalDamageListenerSystem : TestListenerSystem<DamageChangedEvent>;

    [Test]
    public async Task NetworkedTankHitsToHeadDamageBrainAndKillBeforeGibbing()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() => SEntMan.RemoveComponent<PassiveDamageComponent>(STarget!.Value));
        await Client.WaitPost(() => CEntMan.EnsureComponent<Robust.Client.GameObjects.SpriteComponent>(CPlayer));
        var weapon = await PlaceInHands("OxygenTank");
        var control = GetControlFromChildren<MedicalTargetingControl>(GetWidget<HotbarGui>(), true);
        await ClickControl(GetControlFromChildren<Button>(b => b.Name == "Head", control));
        await RunSeconds(2f);
        await SetCombatMode(true);
        for (var i = 0; i < 24; i++)
        {
            await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new LightAttackEvent(Target, weapon, TargetCoords)));
            await RunSeconds(1.5f);
            if (i != 5)
                continue;
            await Server.WaitAssertion(() =>
            {
                var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
                Assert.That(scan.Fractures, Does.Contain("Head"));
                Assert.That(scan.BrainDamage, Is.EqualTo(10f).Within(0.001f));
            });
        }
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            Assert.That(SEntMan.EntityExists(patient), Is.True);
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(patient)!.BrainDamage, Is.EqualTo(100f));
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(patient), Is.True);
            Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(patient).Float(), Is.EqualTo(240f));
        });
        await SetCombatMode(false);
        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(40);
        await Client.WaitAssertion(() =>
        {
            var window = GetWindow<HealthAnalyzerWindow>();
            Assert.That(GetControlFromChildren<Label>(l => l.Text != null && l.Text.StartsWith("Signs of possible internal"), window).Visible, Is.True);
            Assert.That(GetControlFromChildren<Label>(l => l.Text != null && l.Text.StartsWith("Abnormal neurological"), window).Visible, Is.True);
            Assert.That(TryGetControlFromChildren<Label>(l => l.Text != null && l.Text.StartsWith("Irreversible brain death"), window, out _), Is.False);
        });
    }

    [TestCase("WeaponPistolViper", "Piercing", 16f, true, null)]
    [TestCase("WeaponLaserGun", "Heat", 17f, true, null)]
    [TestCase("WeaponPistolViper", "Piercing", 16f, false, null)]
    [TestCase("WeaponLaserGun", "Heat", 17f, false, null)]
    [TestCase("WeaponPistolViper", "Piercing", 19.2f, true, "Slime")]
    [TestCase("WeaponLaserGun", "Heat", 20.4f, true, "Moth")]
    public async Task RealShotPreservesTotalDamageAndOnlyExperimentalPatientsGetLocalWounds(
        string gun, string damageType, float expected, bool experimental, string? modifier)
    {
        await AddAtmosphere();
        await SpawnTarget(experimental ? "MobMedicalTestHuman" : "MobHuman");
        if (modifier != null)
            await Server.WaitPost(() => SEntMan.System<DamageableSystem>().SetDamageModifierSetId(STarget!.Value, modifier));
        await PlaceInHands(gun);
        if (gun == "WeaponPistolViper")
            await UseInHand(); // Chamber the first round.
        await RunSeconds(2f);
        await AssertFiresEvent<DamageChangedEvent>(async () =>
        {
            await AttemptShoot(Target);
            await RunTicks(15);
        });
        string category = "";
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var hit = GetEvents<DamageChangedEvent>().Single();
            Assert.That(hit.TargetPart.HasValue, Is.EqualTo(experimental));
            var damage = SEntMan.System<DamageableSystem>().GetAllDamage(patient);
            Assert.That(damage.DamageDict[damageType].Float(), Is.EqualTo(expected));
            var scan = SEntMan.System<PhysiologySystem>().Scan(patient);
            if (!experimental)
            {
                Assert.That(scan, Is.Null);
                Assert.That(SEntMan.HasComponent<PhysiologyComponent>(patient), Is.False);
                return;
            }
            Assert.That(scan!.Injuries, Has.Count.EqualTo(1));
            Assert.That(scan.Injuries[0].Damage, Is.EqualTo(expected));
            category = scan.Injuries[0].Category;
            Assert.That(SEntMan.GetComponent<OrganComponent>(hit.TargetPart!.Value).Category!.Value.Id, Is.EqualTo(category));
            Assert.That(scan.Injuries[0].Bleeding, damageType == "Heat" ? Is.Zero : Is.GreaterThan(0f));
        });
        if (!experimental)
            return;

        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(20);
        await Client.WaitAssertion(() => Assert.That(GetControlFromChildren<Label>(
            label => label.Text != null && label.Text.StartsWith(category + ": injury"),
            GetWindow<HealthAnalyzerWindow>()).Text, Does.Contain(expected.ToString("F2"))));
        await Client.WaitPost(() => CEntMan.System<Content.Client.Verbs.VerbSystem>()
            .ExecuteVerb(Target!.Value, new Verb { Text = $"Target zone: {category}" }));
        await RunTicks(10);
        await InteractUsing(damageType == "Heat" ? "Ointment" : "Gauze", 10);
        if (expected > 20f)
            await InteractUsing("Ointment", 10);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!.Injuries, Is.Empty));
    }

    [Test]
    public async Task NetworkedWideSwingWoundsOnePartPerVictimWithoutMultiplyingDamage()
    {
        await AddAtmosphere();
        var first = await SpawnTarget("MobMedicalTestHuman");
        var second = await Spawn("MobMedicalTestHuman");
        var ordinary = await Spawn("MobHuman");
        var weapon = await PlaceInHands("Crowbar");
        await RunSeconds(2f);
        await SetCombatMode(true);
        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new HeavyAttackEvent(
            weapon, new() { first, second, ordinary }, TargetCoords)));
        await RunTicks(20);
        await Server.WaitAssertion(() =>
        {
            foreach (var net in new[] { first, second })
            {
                var patient = SEntMan.GetEntity(net);
                var scan = SEntMan.System<PhysiologySystem>().Scan(patient)!;
                Assert.That(scan.Injuries, Has.Count.EqualTo(1));
                Assert.That(scan.Injuries[0].Damage, Is.EqualTo(8f));
                Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(patient).Float(), Is.EqualTo(8f));
            }
            var human = SEntMan.GetEntity(ordinary);
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(human), Is.Null);
            Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(human).Float(), Is.EqualTo(8f));
        });
    }

    [Test]
    public async Task UntargetedSelectionIsRepeatableAndCannotHitDetachedParts()
    {
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var targeting = SEntMan.System<MedicalTargetingSystem>();
            var body = SEntMan.System<BodySystem>();
            var selected = targeting.UntargetedPart(patient, SPlayer);
            Assert.That(selected, Is.Not.Null);
            Assert.That(targeting.UntargetedPart(patient, SPlayer), Is.EqualTo(selected));
            var arm = body.EnumerateOrgans<MedicalWoundComponent>(patient)
                .Single(p => p.Comp1.Category?.Id == "ArmLeft");
            var containers = SEntMan.System<SharedContainerSystem>();
            Assert.That(containers.Remove(arm.Owner, containers.GetContainer(patient, BodyComponent.ContainerID)), Is.True);
            var remaining = body.EnumerateOrgans<MedicalWoundComponent>(patient).Select(p => p.Owner).ToHashSet();
            Assert.That(remaining, Has.Count.EqualTo(10));
            foreach (var source in body.EnumerateOrgans<OrganComponent>(patient))
                Assert.That(remaining, Does.Contain(targeting.UntargetedPart(patient, source.Owner)));
            Assert.That(targeting.UntargetedPart(SPlayer, patient), Is.Null);
        });
    }
}
