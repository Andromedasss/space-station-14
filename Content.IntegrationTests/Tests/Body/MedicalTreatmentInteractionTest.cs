using System.Linq;
using Content.Client.HealthAnalyzer.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Medical;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Mobs.Systems;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests.Body;

public sealed class MedicalTreatmentInteractionTest : InteractionTest
{
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
