#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests.Helpers;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Medical;
using Content.Shared.Body;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Chat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Medical;

/// <summary>
/// Tests for defibrilators.
/// </summary>
[TestOf(typeof(DefibrillatorComponent))]
public sealed class DefibrillatorTest : InteractionTest
{
    private sealed class SpeechListenerSystem : TestListenerSystem<EntitySpokeEvent>;

    // We need two hands to use a defbrillator.
    protected override string PlayerPrototype => "MobHuman";

    private static readonly EntProtoId DefibrillatorProtoId = "Defibrillator";
    private static readonly EntProtoId TargetProtoId = "MobHuman";
    private static readonly EntProtoId BedProtoId = "Bed";

    /// <summary>
    /// Kills a target mob by brain death and verifies that a defibrillator cannot revive them.
    /// Physiology patients die from brain death; the defibrillator only corrects a shockable rhythm.
    /// </summary>
    [Test]
    public async Task KillAndReviveTest()
    {
        // Don't let the player and target suffocate.
        await AddAtmosphere();

        await SpawnTarget(TargetProtoId);

        var targetMobState = Comp<MobStateComponent>();

        // Check that the target is alive when spawned.
        Assert.That(targetMobState.CurrentState, Is.EqualTo(MobState.Alive), "Target mob was not alive when spawned.");

        // Kill the target by destroying the brain.
        await Server.WaitPost(() =>
        {
            var body = SEntMan.System<BodySystem>();
            foreach (var (_, _, brain) in body.EnumerateOrgans<CerebralHealthComponent>(STarget.Value))
                brain.Damage = PhysiologyModel.MaximumBrainDamage;
        });
        await RunTicks(3);

        // Check that the target is dead.
        Assert.That(targetMobState.CurrentState, Is.EqualTo(MobState.Dead), "Target mob did not die from brain death.");

        // Spawn a defib and activate it.
        var defib = await PlaceInHands(DefibrillatorProtoId, enableToggleable: true);
        var cooldown = Comp<DefibrillatorComponent>(defib).ZapDelay;

        // Wait for the cooldown.
        await RunSeconds((float)cooldown.TotalSeconds);

        // ZAP!
        await Interact();

        // Brain death is irreversible: the target must remain dead.
        Assert.That(targetMobState.CurrentState, Is.EqualTo(MobState.Dead), "A brain-dead target was revived by a defibrillator.");
    }

    /// <summary>
    /// Revives a target mob that is strapped to a bed. The bed gets caught in the zap chain, but the defibrillator
    /// should only report on the patient, not complain that the bed is an inanimate object.
    /// </summary>
    [Test]
    public async Task ReviveBuckledTest()
    {
        // Don't let the player and target suffocate.
        await AddAtmosphere();

        await SpawnTarget(TargetProtoId);
        var bed = ToServer(await Spawn(BedProtoId));

        var targetMobState = Comp<MobStateComponent>();
        var targetBuckle = Comp<BuckleComponent>();
        var buckleSystem = SEntMan.System<SharedBuckleSystem>();
        var loc = Server.ResolveDependency<ILocalizationManager>();

        await Server.WaitPost(() => buckleSystem.TryBuckle(STarget.Value, null, bed));
        await RunTicks(3);
        Assert.That(targetBuckle.BuckledTo, Is.EqualTo(bed), "Target mob was not buckled to the bed.");

        // Kill the target by destroying the brain. A defibrillator cannot bring the patient back.
        await Server.WaitPost(() =>
        {
            var body = SEntMan.System<BodySystem>();
            foreach (var (_, _, brain) in body.EnumerateOrgans<CerebralHealthComponent>(STarget.Value))
                brain.Damage = PhysiologyModel.MaximumBrainDamage;
        });
        await RunTicks(3);
        Assert.That(targetMobState.CurrentState, Is.EqualTo(MobState.Dead), "Target mob was not dead before being defibrillated.");

        // Spawn a defib, activate it and record everything it says.
        var defib = await PlaceInHands(DefibrillatorProtoId, enableToggleable: true);
        var sDefib = ToServer(defib);
        await Server.WaitPost(() => SEntMan.EnsureComponent<TestListenerComponent>(sDefib));
        var cooldown = Comp<DefibrillatorComponent>(defib).ZapDelay;
        await RunSeconds((float)cooldown.TotalSeconds);

        // ZAP!
        await Interact();

        Assert.That(targetMobState.CurrentState, Is.EqualTo(MobState.Dead), "Buckled brain-dead target mob was revived.");

        // The dummy has no mind, so the defib complains about that. It must not also complain about the bed.
        var spoken = GetEvents<EntitySpokeEvent>(sDefib).Select(ev => ev.Message).ToList();
        Assert.That(spoken, Is.Not.Empty, "Defibrillator did not report on the patient.");
        Assert.That(spoken, Does.Not.Contain(loc.GetString("defibrillator-not-living")), "Defibrillator reported on the bed instead of the patient.");
    }

    /// <summary>
    /// A glued defibrillator cannot be dropped when its user shocks themself, so it must keep both hands occupied.
    /// </summary>
    [Test]
    public async Task GluedDefibrillatorSelfZapKeepsBothHandsTest()
    {
        await AddAtmosphere();

        var defib = await PlaceInHands(DefibrillatorProtoId, enableToggleable: true);
        var sDefib = ToServer(defib);

        // Applying glue makes the held item unremoveable. Add the resulting component after pickup without depending
        // on the chemistry interaction in this integration test.
        await Server.WaitPost(() =>
        {
            var unremoveable = SEntMan.EnsureComponent<UnremoveableComponent>(sDefib);
            unremoveable.DeleteOnDrop = false;
            SEntMan.Dirty(sDefib, unremoveable);
        });

        var hands = SEntMan.System<SharedHandsSystem>();
        await RunTicks(1);
        Assert.Multiple(() =>
        {
            Assert.That(hands.EnumerateHeld(SPlayer).Count(), Is.EqualTo(2));
            Assert.That(hands.EnumerateHeld(SPlayer).Count(uid => SEntMan.HasComponent<VirtualItemComponent>(uid)), Is.EqualTo(1));
        });

        var cooldown = Comp<DefibrillatorComponent>(defib).ZapDelay;
        await RunSeconds((float) cooldown.TotalSeconds);

        // Shock the user with the defibrillator.
        await Interact(ToServer(Player), ToServer(PlayerCoords));

        Assert.Multiple(() =>
        {
            Assert.That(hands.EnumerateHeld(SPlayer).Count(), Is.EqualTo(2));
            Assert.That(hands.EnumerateHeld(SPlayer).Count(uid => SEntMan.HasComponent<VirtualItemComponent>(uid)), Is.EqualTo(1));
        });
    }
}
