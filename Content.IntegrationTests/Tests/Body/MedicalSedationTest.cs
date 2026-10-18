using System.Linq;
using Content.Client.HealthAnalyzer.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Medical;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Medical;
using Content.Shared.Mobs.Systems;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests.Body;

public sealed class MedicalSedationTest : InteractionTest
{
    [Test]
    public async Task FailedOrgansRetainRoutedDrugsAndBlockActualStomachAbsorption()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var physiology = SEntMan.System<PhysiologySystem>();
            physiology.TryIntervene(patient, "kidneys", 0f);
            physiology.TryIntervene(patient, "liver", 0f);
            physiology.TryIntervene(patient, "intestines", 0f);
            Assert.That(SEntMan.System<BloodstreamSystem>().TryAddToBloodstream(patient,
                new Solution([new("MedicalTestSedative", 2), new("MedicalTestAnalgesic", 2)])), Is.True);
            var stomach = SEntMan.System<BodySystem>().EnumerateOrgans<Content.Shared.Body.Components.StomachComponent>(patient).Single();
            Assert.That(SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(stomach.Owner, "stomach", out var solution, out _), Is.True);
            Assert.That(SEntMan.System<SharedSolutionContainerSystem>().TryAddSolution(solution!.Value,
                new Solution([new("MedicalTestInotrope", 2)])), Is.True);
        });
        await RunSeconds(6f);
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.SedativeDose, Is.EqualTo(2f));
            Assert.That(scan.AnalgesicDose, Is.EqualTo(2f));
            Assert.That(scan.InotropeDose, Is.Zero);
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget.Value, "kidneys", 1f);
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget.Value, "liver", 1f);
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget.Value, "intestines", 1f);
        });
        await RunSeconds(6f);
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.SedativeDose, Is.LessThan(2f));
            Assert.That(scan.AnalgesicDose, Is.LessThan(2f));
            var stomach = SEntMan.System<BodySystem>().EnumerateOrgans<Content.Shared.Body.Components.StomachComponent>(STarget.Value).Single();
            Assert.That(SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(stomach.Owner, "stomach", out _, out var solution), Is.True);
            Assert.That(solution!.GetTotalPrototypeQuantity("MedicalTestInotrope").Float(), Is.LessThan(2f));
        });
    }

    [Test]
    public async Task AnestheticInjectorCausesReversibleUnconsciousnessWithoutHealing()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() => SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "ArmLeft", "Blunt", 20f));
        await InteractUsing("MedicalTestSedativeMedipen");
        await RunTicks(15);
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var scan = SEntMan.System<PhysiologySystem>().Scan(patient)!;
            Assert.That(scan.SedativeDose, Is.InRange(4f, 5f));
            Assert.That(scan.Sedated, Is.True);
            Assert.That(scan.RespiratoryDrive, Is.InRange(0.625f, 0.75f));
            Assert.That(scan.Pain, Is.Zero);
            Assert.That(scan.PainBurden, Is.GreaterThan(0f));
            Assert.That(scan.Injuries.Sum(i => i.Damage), Is.GreaterThan(19f));
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(patient), Is.True);
        });
        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(30);
        await Client.WaitAssertion(() => Assert.That(GetControlFromChildren<Label>(
            l => l.Text != null && l.Text.StartsWith("Experimental sedative:"), GetWindow<HealthAnalyzerWindow>()).Visible, Is.True));
        await RunSeconds(110f);
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var scan = SEntMan.System<PhysiologySystem>().Scan(patient)!;
            Assert.That(scan.SedativeDose, Is.Zero);
            Assert.That(scan.Sedated, Is.False);
            Assert.That(scan.RespiratoryDrive, Is.EqualTo(1f));
            Assert.That(scan.BrainDamage, Is.Zero);
            Assert.That(SEntMan.System<MobStateSystem>().IsAlive(patient), Is.True);
        });
    }

    [Test]
    public async Task OverdoseStopsBreathingAndCausesHypoxiaInAirWithoutAffectingOrdinaryHumans()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            SEntMan.System<BloodstreamSystem>().TryAddToBloodstream(patient, new Solution([new("MedicalTestSedative", 30)]));
            Assert.That(SEntMan.System<MedicalSedationSystem>().RespiratoryDrive(patient), Is.Zero);
            Assert.That(SEntMan.System<RespiratorSystem>().IsBreathing(patient), Is.False);
        });
        await RunSeconds(50f);
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.OxygenDebt, Is.GreaterThan(30f));
            Assert.That(scan.BrainDamage, Is.GreaterThan(0f));
            Assert.That(SEntMan.GetComponent<RespiratorComponent>(STarget.Value).Saturation, Is.LessThanOrEqualTo(0f));
        });
    }

    [Test]
    public async Task SedationCannotHideBrainDeathOrRestoreDamagedBrain()
    {
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(patient).Single();
            brain.Comp2.Damage = 100f;
            SEntMan.System<BloodstreamSystem>().TryAddToBloodstream(patient, new Solution([new("MedicalTestSedative", 5)]));
            SEntMan.System<PhysiologySystem>().Update(0f);
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(patient), Is.True);
            Assert.That(brain.Comp2.Damage, Is.EqualTo(100f));
        });
    }
}
