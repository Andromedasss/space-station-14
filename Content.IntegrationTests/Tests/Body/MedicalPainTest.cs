using System.Linq;
using Content.Client.HealthAnalyzer.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Medical;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Medical;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests.Body;

public sealed class MedicalPainTest : InteractionTest
{
    [Test]
    public async Task InjectorRelievesPainWithoutHealingAndMetabolismRestoresSymptoms()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() =>
        {
            SEntMan.RemoveComponent<PassiveDamageComponent>(STarget!.Value);
            SEntMan.System<MedicalWoundSystem>().TryInjure(STarget!.Value, "ArmLeft", "Blunt", 100f);
            SEntMan.System<MedicalWoundSystem>().TryInjure(STarget.Value, "ArmRight", "Blunt", 100f);
        });
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<PhysiologyComponent>(STarget!.Value).Pain, Is.EqualTo(100));
            var movement = SEntMan.GetComponent<MovementSpeedModifierComponent>(STarget.Value);
            Assert.That(movement.WalkSpeedModifier, Is.EqualTo(1f).Within(0.001f));
            Assert.That(movement.SprintSpeedModifier, Is.EqualTo(0.85f).Within(0.001f));
        });
        await InteractUsing("MedicalTestAnalgesicMedipen");
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var scan = SEntMan.System<PhysiologySystem>().Scan(patient)!;
            Assert.That(scan.PainBurden, Is.EqualTo(100f));
            Assert.That(scan.AnalgesicDose, Is.InRange(4f, 5f));
            Assert.That(scan.Pain, Is.InRange(40f, 52f));
            Assert.That(scan.Injuries.Sum(i => i.Damage), Is.EqualTo(200f));
            Assert.That(scan.Fractures, Has.Count.EqualTo(2));
            Assert.That(SEntMan.GetComponent<PhysiologyComponent>(patient).LeftHandFunction, Is.Zero);
            Assert.That(SEntMan.System<MobStateSystem>().IsAlive(patient), Is.True);
        });
        await InteractUsing("HandheldHealthAnalyzer");
        await RunTicks(40);
        await Client.WaitAssertion(() =>
        {
            Assert.That(GetControlFromChildren<Label>(
                l => l.Text != null && l.Text.StartsWith("Pain burden:"), GetWindow<HealthAnalyzerWindow>()).Text,
                Does.Contain("experimental analgesic:"));
            var patient = CEntMan.GetEntity(Target!.Value);
            var physiology = CEntMan.GetComponent<PhysiologyComponent>(patient);
            Assert.That(physiology.Pain, Is.InRange(40, 52));
            Assert.That(CEntMan.GetComponent<MovementSpeedModifierComponent>(patient).SprintSpeedModifier,
                Is.EqualTo(PhysiologyModel.PainSprintModifier(physiology.Pain)).Within(0.001f));
        });
        await RunSeconds(120f);
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.AnalgesicDose, Is.Zero);
            Assert.That(scan.Pain, Is.EqualTo(100f));
            Assert.That(scan.Injuries.Sum(i => i.Damage), Is.EqualTo(200f));
        });
    }

    [Test]
    public async Task PainSamplesPhysicalSitesAndCannotAwakenAnIrreversibleBrain()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var body = SEntMan.System<BodySystem>();
            var pain = SEntMan.System<MedicalPainSystem>();
            var arm = body.EnumerateOrgans<MedicalWoundComponent>(patient)
                .Single(p => p.Comp1.Category?.Id == "ArmLeft");
            arm.Comp2.Fractured = true;
            arm.Comp2.SurgicalAccess = 3;
            arm.Comp2.IschemiaDebt = 90f;
            Assert.That(pain.Sample(patient).Burden, Is.EqualTo(45f));
            arm.Comp2.Prosthetic = true;
            Assert.That(pain.Sample(patient).Burden, Is.Zero);
            arm.Comp2.Prosthetic = false;
            arm.Comp2.Necrotic = true;
            Assert.That(pain.Sample(patient).Burden, Is.Zero);
            arm.Comp2.Necrotic = false;
            var brain = body.EnumerateOrgans<CerebralHealthComponent>(patient).Single();
            brain.Comp2.OxygenDebt = 16f;
            Assert.That(pain.Sample(patient).Pain, Is.Zero);
            Assert.That(pain.Sample(patient).Burden, Is.EqualTo(45f));
            brain.Comp2.Damage = 100f;
            SEntMan.System<BloodstreamSystem>().TryAddToBloodstream(patient,
                new Solution([new("MedicalTestAnalgesic", 20)]));
            SEntMan.System<PhysiologySystem>().Update(0f);
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(patient), Is.True);
            Assert.That(brain.Comp2.Damage, Is.EqualTo(100f));
        });
    }

    [Test]
    public async Task AnalgesiaDoesNotStopBleedingOrChangeOrdinaryPatients()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            SEntMan.System<MedicalWoundSystem>().TryInjure(patient, "LegLeft", "Slash", 20f);
            var leg = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalWoundComponent>(patient)
                .Single(p => p.Comp1.Category?.Id == "LegLeft");
            var bleeding = leg.Comp2.Bleeding;
            SEntMan.System<BloodstreamSystem>().TryAddToBloodstream(patient,
                new Solution([new("MedicalTestAnalgesic", 5)]));
            var scan = SEntMan.System<PhysiologySystem>().Scan(patient)!;
            Assert.That(scan.Pain, Is.Zero);
            Assert.That(leg.Comp2.Bleeding, Is.EqualTo(bleeding));
            Assert.That(leg.Comp2.Injury.GetTotal(), Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(scan.Mobility, Is.EqualTo(0.92f).Within(0.001f));
        });
        await SpawnTarget("MobHuman");
        await InteractUsing("MedicalTestAnalgesicMedipen");
        await RunSeconds(5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<PhysiologyComponent>(STarget!.Value), Is.False);
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget.Value), Is.Null);
            Assert.That(SEntMan.System<MobStateSystem>().IsAlive(STarget.Value), Is.True);
        });
    }
}
