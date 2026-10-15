using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Medical;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;

namespace Content.IntegrationTests.Tests.Body;

public sealed class MedicalResuscitationTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task CompressionsProvideCirculationOnlyDuringUninterruptedAction()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitAssertion(() =>
        {
            var physiology = SEntMan.System<PhysiologySystem>();
            physiology.TryIntervene(STarget!.Value, "heart", 0f);
            physiology.Update(16f);
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(STarget.Value), Is.True);
        });
        await StartCompressions();
        await RunSeconds(10f);
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.CprActive, Is.True);
            Assert.That(scan.HeartFunction, Is.Zero);
            Assert.That(scan.CirculatoryFunction, Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(SEntMan.System<MobStateSystem>().IsAlive(STarget.Value), Is.True);
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget.Value, "blood", 0f);
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget.Value)!.OxygenDelivery, Is.Zero);
            SEntMan.System<PhysiologySystem>().TryIntervene(STarget.Value, "blood", 1f);
        });
        await CancelDoAfters();
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.CprActive, Is.False);
            Assert.That(scan.CirculatoryFunction, Is.Zero);
        });
        await RunTicks(30);
        await StartCompressions();
        await AwaitDoAfters();
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!.CprActive,
            Is.False, "Completed compressions must not leave a persistent circulation override."));
    }

    [Test]
    public async Task CompressionsStopWhenRescuerUsesHandAndCannotReviveDeadBrain()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await StartCompressions();
        await PlaceInHands("Crowbar");
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            var physiology = SEntMan.System<PhysiologySystem>();
            Assert.That(physiology.Scan(STarget!.Value)!.CprActive, Is.False);
        });
        await DropAll();
        await RunTicks(30);
        await Server.WaitAssertion(() =>
        {
            var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(STarget!.Value).Single();
            brain.Comp2.Damage = 100f;
            Assert.That(SEntMan.System<SharedVerbSystem>().GetLocalVerbs(STarget.Value, SPlayer, typeof(AlternativeVerb))
                .Any(v => v.Text == "Perform experimental chest compressions"), Is.False);
            SEntMan.System<PhysiologySystem>().Update(0f);
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(STarget.Value), Is.True);
        });
    }

    [Test]
    public async Task RealInjectorSupportsWeakHeartAndNormalMetabolismClearsDrug()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() => SEntMan.System<PhysiologySystem>().TryIntervene(STarget!.Value, "heart", 0.3f));
        await InteractUsing("MedicalTestInotropeMedipen");
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.InotropeDose, Is.InRange(2f, 5f));
            Assert.That(scan.HeartFunction, Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(scan.CirculatoryFunction, Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(scan.ShockableRhythm, Is.False);
        });
        await RunSeconds(60f);
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.InotropeDose, Is.LessThan(2f));
            Assert.That(scan.CirculatoryFunction, Is.EqualTo(0.3f).Within(0.001f));
        });
    }

    [Test]
    public async Task DoseBoundariesDoNotReplaceBloodOrHeartAndOverdoseNeedsRhythmTreatment()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitAssertion(() =>
        {
            var patient = STarget!.Value;
            var physiology = SEntMan.System<PhysiologySystem>();
            var blood = SEntMan.System<BloodstreamSystem>();
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(blood.TryAddToBloodstream(patient, new Solution([new("MedicalTestInotrope", 1)])), Is.True);
            physiology.TryIntervene(patient, "heart", 0.3f);
            Assert.That(physiology.Scan(patient)!.CirculatoryFunction, Is.EqualTo(0.3f).Within(0.001f));
            blood.TryAddToBloodstream(patient, new Solution([new("MedicalTestInotrope", 1)]));
            Assert.That(physiology.Scan(patient)!.CirculatoryFunction, Is.EqualTo(0.6f).Within(0.001f));
            physiology.TryIntervene(patient, "blood", 0f);
            Assert.That(physiology.Scan(patient)!.OxygenDelivery, Is.Zero);
            physiology.TryIntervene(patient, "blood", 1f);
            physiology.TryIntervene(patient, "heart", 0f);
            Assert.That(physiology.Scan(patient)!.CirculatoryFunction, Is.Zero);
            physiology.TryIntervene(patient, "heart", 0.3f);
            blood.TryAddToBloodstream(patient, new Solution([new("MedicalTestInotrope", 8)]));
            physiology.Update(0f);
            Assert.That(physiology.Scan(patient)!.ShockableRhythm, Is.True);
            Assert.That(physiology.Scan(patient)!.CirculatoryFunction, Is.Zero);
            Assert.That(solutions.TryGetSolution(patient, BloodstreamComponent.DefaultBloodSolutionName,
                out var solution, out _), Is.True);
            solutions.RemoveReagent(solution!.Value, "MedicalTestInotrope", FixedPoint2.New(100));
            Assert.That(physiology.Scan(patient)!.ShockableRhythm, Is.True, "Clearance does not itself reset the rhythm.");
            var defib = new MedicalDefibrillationEvent();
            SEntMan.EventBus.RaiseLocalEvent(patient, ref defib);
            Assert.That(defib.Handled && defib.Successful, Is.True);
            Assert.That(physiology.Scan(patient)!.CirculatoryFunction, Is.EqualTo(0.3f).Within(0.001f));
            defib = new MedicalDefibrillationEvent();
            SEntMan.EventBus.RaiseLocalEvent(patient, ref defib);
            Assert.That(defib.Successful, Is.False, "A nonshockable weak heart cannot be repaired by another shock.");
            blood.TryAddToBloodstream(patient, new Solution([new("MedicalTestInotrope", 10)]));
            physiology.Update(0f);
            defib = new MedicalDefibrillationEvent();
            SEntMan.EventBus.RaiseLocalEvent(patient, ref defib);
            Assert.That(defib.Successful, Is.True);
            physiology.Update(0f);
            Assert.That(physiology.Scan(patient)!.ShockableRhythm, Is.True,
                "Persistent toxic drug levels must cause recurrence after defibrillation.");
        });
    }

    [Test]
    public async Task ExperimentalDrugAndEquipmentDoNotOptOrdinaryPatientsIntoPhysiology()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        await InteractUsing("MedicalTestInotropeMedipen");
        await RunSeconds(5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<PhysiologyComponent>(STarget!.Value), Is.False);
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget.Value), Is.Null);
            Assert.That(SEntMan.System<MedicalLifeSupportSystem>().GetSupport(STarget.Value), Is.Null);
            Assert.That(SEntMan.System<SharedVerbSystem>().GetLocalVerbs(STarget.Value, SPlayer, typeof(AlternativeVerb))
                .Any(v => v.Text == "Perform experimental chest compressions"), Is.False);
            var defib = new MedicalDefibrillationEvent();
            SEntMan.EventBus.RaiseLocalEvent(STarget.Value, ref defib);
            Assert.That(defib.Handled, Is.False);
            Assert.That(SEntMan.System<DamageableSystem>().GetAllDamage(STarget.Value).GetTotal(),
                Is.EqualTo(FixedPoint2.Zero));
        });
    }

    [Test]
    public async Task ActualDefibrillatorCorrectsOnlyShockableRhythmWithoutHealingTissueOrBrain()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        await Server.WaitPost(() =>
        {
            var heart = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget!.Value)
                .Single(p => p.Comp2.Kind == MedicalOrganKind.Heart);
            heart.Comp2.Fibrillating = true;
            heart.Comp2.Trauma = 20f;
            var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(STarget.Value).Single();
            brain.Comp2.Damage = 12f;
            SEntMan.System<DamageableSystem>().TryChangeDamage(STarget.Value,
                new DamageSpecifier { DamageDict = { ["Asphyxiation"] = FixedPoint2.New(20) } }, true);
        });
        var defib = await PlaceInHands("Defibrillator", enableToggleable: true);
        await RunSeconds((float)Comp<DefibrillatorComponent>(defib).ZapDelay.TotalSeconds);
        await Interact();
        await Server.WaitAssertion(() =>
        {
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget!.Value)!;
            Assert.That(scan.ShockableRhythm, Is.False);
            Assert.That(scan.HeartFunction, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(scan.BrainDamage, Is.EqualTo(12f));
            Assert.That(SEntMan.System<DamageableSystem>().GetAllDamage(STarget.Value).DamageDict["Asphyxiation"],
                Is.GreaterThan(FixedPoint2.Zero), "Experimental defibrillation must not use generic ZapHeal.");
            var heart = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget.Value)
                .Single(p => p.Comp2.Kind == MedicalOrganKind.Heart);
            heart.Comp2.Fibrillating = true;
            var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(STarget.Value).Single();
            brain.Comp2.Damage = 100f;
        });
        await RunSeconds((float)Comp<DefibrillatorComponent>(defib).ZapDelay.TotalSeconds);
        await Interact();
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(STarget!.Value), Is.True);
            Assert.That(SEntMan.System<PhysiologySystem>().Scan(STarget.Value)!.BrainDamage, Is.EqualTo(100f));
        });
    }

    private async Task StartCompressions()
    {
        await Server.WaitPost(() => SEntMan.System<SharedVerbSystem>()
            .GetLocalVerbs(STarget!.Value, SPlayer, typeof(AlternativeVerb))
            .Single(v => v.Text == "Perform experimental chest compressions").Act!());
        await RunTicks(2);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1));
    }
}
