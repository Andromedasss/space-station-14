#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Medical;
using Content.Shared.Body;
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Medical;

/// <summary>
/// Tests for defibrilators.
/// </summary>
[TestOf(typeof(DefibrillatorComponent))]
public sealed class DefibrillatorTest : InteractionTest
{
    // We need two hands to use a defbrillator.
    protected override string PlayerPrototype => "MobHuman";

    private static readonly EntProtoId DefibrillatorProtoId = "Defibrillator";
    private static readonly EntProtoId TargetProtoId = "MobHuman";

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
}
