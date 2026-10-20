using System.Linq;
using Content.Client.Medical;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Body;
using Content.Server.Medical;
using Content.Server.Power.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Medical;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Body;

public sealed class BedsideMonitorTest : InteractionTest
{
    [Test]
    public async Task MonitorRequiresAnchoringAndRetainsValidPatientUntilUnbuckled()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var second = await Spawn("MobMedicalTestHuman");
        var bed = await Spawn("MedicalTestOperatingTable");
        var secondBed = await Spawn("MedicalTestOperatingTable");
        var monitor = await Spawn("BedsideMonitor", PlayerCoords);
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.GetEntity(monitor);
            var other = SEntMan.GetEntity(second);
            var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(uid);
            power.NeedsPower = false;
            power.Powered = true;
            var system = SEntMan.System<BedsideMonitorSystem>();
            var buckle = SEntMan.System<SharedBuckleSystem>();
            Assert.That(buckle.TryBuckle(STarget!.Value, null, SEntMan.GetEntity(bed)), Is.True);
            Assert.That(system.GetMonitorState(uid).Patient, Is.EqualTo(Target));
            Assert.That(buckle.TryBuckle(other, null, SEntMan.GetEntity(secondBed)), Is.True);
            Assert.That(system.GetMonitorState(uid).Patient, Is.EqualTo(Target));
            buckle.Unbuckle(STarget.Value, null);
            Assert.That(system.GetMonitorState(uid).Patient, Is.EqualTo(second));
            var component = SEntMan.GetComponent<BedsideMonitorComponent>(uid);
            component.Range = 0.01f;
            Assert.That(system.GetMonitorState(uid).Patient, Is.Null);
            component.Range = 2f;
            Assert.That(system.GetMonitorState(uid).Patient, Is.EqualTo(second));
            var xform = SEntMan.System<SharedTransformSystem>();
            xform.Unanchor(uid, SEntMan.GetComponent<TransformComponent>(uid));
            Assert.That(system.GetMonitorState(uid).Powered, Is.False);
            Assert.That(system.GetMonitorState(uid).Patient, Is.Null);
            xform.AnchorEntity(uid, SEntMan.GetComponent<TransformComponent>(uid));
            Assert.That(system.GetMonitorState(uid).Patient, Is.EqualTo(second));
            SEntMan.DeleteEntity(other);
            Assert.That(system.GetMonitorState(uid).Patient, Is.Null);
        });
    }

    [Test]
    public async Task MonitorUpdatesVitalsAndAlarmsAndClearsOnPowerLoss()
    {
        await AddAtmosphere();
        await SpawnTarget("MobMedicalTestHuman");
        var bed = await Spawn("MedicalTestOperatingTable");
        var monitor = await Spawn("BedsideMonitor", PlayerCoords);
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.GetEntity(monitor);
            var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(uid);
            power.NeedsPower = false;
            power.Powered = true;
            Assert.That(SEntMan.GetComponent<TransformComponent>(uid).Anchored, Is.True);
            var system = SEntMan.System<BedsideMonitorSystem>();
            Assert.That(system.GetMonitorState(uid).Patient, Is.Null);
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(STarget!.Value, null, SEntMan.GetEntity(bed)), Is.True);
            var state = system.GetMonitorState(uid);
            Assert.That(state.Patient, Is.EqualTo(SEntMan.GetNetEntity(STarget.Value)));
            var scan = SEntMan.System<PhysiologySystem>().Scan(STarget.Value)!;
            Assert.That(state.Pulse, Is.EqualTo(scan.Pulse));
            Assert.That(state.Systolic, Is.EqualTo(scan.Systolic));
            Assert.That(state.Diastolic, Is.EqualTo(scan.Diastolic));
            Assert.That(state.OxygenDelivery, Is.EqualTo(scan.OxygenDelivery));
            Assert.That(state.Alarm, Is.EqualTo(BedsideMonitorAlarm.None));
        });
        await Activate(monitor);
        await RunTicks(70);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.System<BedsideMonitorSystem>().GetMonitorState(SEntMan.GetEntity(monitor));
            Assert.That(state.Powered, Is.True, "Monitor lost power during simulation");
            Assert.That(SEntMan.GetComponent<BuckleComponent>(STarget!.Value).BuckledTo, Is.Not.Null, "Patient became unbuckled");
            Assert.That(SEntMan.System<SharedTransformSystem>().InRange(
                SEntMan.GetComponent<TransformComponent>(SEntMan.GetEntity(monitor)).Coordinates,
                SEntMan.GetComponent<TransformComponent>(STarget.Value).Coordinates, 2f), Is.True, "Patient moved out of range");
            Assert.That(state.Patient, Is.Not.Null, "Monitor lost its patient during simulation");
        });
        await Client.WaitAssertion(() => Assert.That(GetControlFromChildren<Label>(l => l.Text != null
            && l.Text.StartsWith("Pulse:"), GetWindow<BedsideMonitorWindow>()).Text, Does.Not.Contain("--")));
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.GetEntity(monitor);
            var system = SEntMan.System<BedsideMonitorSystem>();
            var physiology = SEntMan.System<PhysiologySystem>();
            physiology.TryIntervene(STarget!.Value, "blood", 0.5f);
            Assert.That(system.GetMonitorState(uid).Alarm, Is.EqualTo(BedsideMonitorAlarm.Shock));
            physiology.TryIntervene(STarget.Value, "blood", 1f);
            physiology.TryIntervene(STarget.Value, "lungs", 0.3f);
            Assert.That(system.GetMonitorState(uid).Alarm, Is.EqualTo(BedsideMonitorAlarm.Hypoxia));
            physiology.TryIntervene(STarget.Value, "heart", 0f);
            Assert.That(system.GetMonitorState(uid).Alarm, Is.EqualTo(BedsideMonitorAlarm.Asystole));
            var heart = SEntMan.System<BodySystem>().EnumerateOrgans<MedicalOrganComponent>(STarget.Value)
                .Single(p => p.Comp2.Kind == MedicalOrganKind.Heart);
            heart.Comp2.Fibrillating = true;
            Assert.That(system.GetMonitorState(uid).Alarm, Is.EqualTo(BedsideMonitorAlarm.Fibrillation));
            SEntMan.GetComponent<ApcPowerReceiverComponent>(uid).PowerDisabled = true;
            var off = system.GetMonitorState(uid);
            Assert.That(off.Powered, Is.False);
            Assert.That(off.Patient, Is.Null);
            Assert.That(off.Pulse, Is.Zero);
            Assert.That(off.Alarm, Is.EqualTo(BedsideMonitorAlarm.None));
        });
        await RunTicks(70);
        await Client.WaitAssertion(() => Assert.That(GetControlFromChildren<Label>(l => l.Text != null
            && l.Text.StartsWith("Pulse:"), GetWindow<BedsideMonitorWindow>()).Text, Does.Contain("--")));
    }
}
