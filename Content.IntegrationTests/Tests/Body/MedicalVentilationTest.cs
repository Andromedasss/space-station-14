using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Medical;
using Content.Shared.Body;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Medical;

namespace Content.IntegrationTests.Tests.Body;

public sealed class MedicalVentilationTest : InteractionTest
{
    [Test]
    public async Task RepeatedAssistedBreathsConsumeGasAndPreventHypoxiaDuringRespiratoryArrest()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() => SEntMan.System<BloodstreamSystem>().TryAddToBloodstream(STarget!.Value,
            new Solution([new("MedicalTestSedative", 30)])));
        var bag = await PlaceInHands("MedicalTestAmbuBag");
        float initial = 0f;
        await Server.WaitPost(() => initial = SEntMan.GetComponent<MedicalVentilationComponent>(SEntMan.GetEntity(bag)).GasSupply.TotalMoles);
        for (var i = 0; i < 20; i++)
        {
            await Interact();
            await RunSeconds(1f);
        }
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.RespiratoryDrive, Is.Zero);
            Assert.That(scan.BrainDamage, Is.Zero);
            Assert.That(scan.OxygenDebt, Is.LessThan(5f));
            Assert.That(SEntMan.GetComponent<MedicalVentilationComponent>(SEntMan.GetEntity(bag)).GasSupply.TotalMoles,
                Is.LessThan(initial));
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget.Value, "blood", 0f);
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget.Value)!.OxygenDelivery, Is.Zero);
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget.Value, "blood", 1f);
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget.Value, "heart", 0f);
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget.Value)!.OxygenDelivery, Is.Zero);
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget.Value, "heart", 1f);
        });
        await RunSeconds(50f);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!.BrainDamage,
            Is.GreaterThan(0f), "Stopping manual ventilation must not leave a lasting respiratory override."));
    }

    [TestCase("drop")]
    [TestCase("empty")]
    [TestCase("dead")]
    [TestCase("lungs")]
    public async Task InvalidOrInterruptedVentilationCannotSupplyBreaths(string change)
    {
        await SpawnTarget("MobMedicalTestHuman");
        var bag = await PlaceInHands("MedicalTestAmbuBag");
        if (change != "drop")
        {
            await Server.WaitPost(() =>
            {
                var patient = STarget!.Value;
                if (change == "empty")
                    SEntMan.GetComponent<MedicalVentilationComponent>(SEntMan.GetEntity(bag)).GasSupply.Clear();
                else if (change == "dead")
                    SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(patient).Single().Comp2.Damage = 100f;
                else
                    SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(patient)
                        .Single(o => o.Comp2.Kind == MedicalOrganKind.Lungs).Comp2.Trauma = 100f;
            });
            await Interact(false);
            Assert.That(ActiveDoAfters, Is.Empty);
            return;
        }
        float before = 0f;
        await Server.WaitPost(() => before = SEntMan.GetComponent<MedicalVentilationComponent>(SEntMan.GetEntity(bag)).GasSupply.TotalMoles);
        await Interact(false);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
        await Drop();
        await RunSeconds(3f);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<MedicalVentilationComponent>(SEntMan.GetEntity(bag)).GasSupply.TotalMoles,
            Is.EqualTo(before)));
    }
}
