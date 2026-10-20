using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Medical;
using Content.Shared.Administration.Systems;
using Content.Shared.Body;
using Content.Shared.Medical;
using Content.Shared.Movement.Components;
using Content.Shared.Speech;
using Content.Shared.IdentityManagement;
using Content.Shared.Throwing;
using Content.Shared.DoAfter;
using Content.Shared.Paper;
using System.Numerics;
using Robust.Shared.Containers;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests.Body;

public sealed class MedicalNeurologyTest : GameTest
{
    [Test]
    [RunOnSide(Side.Server)]
    public void TemporaryDeficitsRecoverOnlyWithPerfusionAndPersistentDeficitsNeedRehabilitation()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var physiology = SEntMan.System<PhysiologySystem>();
        var neurology = SEntMan.System<MedicalNeurologySystem>();
        var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(patient).Single().Comp2;
        physiology.TryIntervene(patient, "vision", 4);
        physiology.TryIntervene(patient, "aphasia", 1);
        physiology.TryIntervene(patient, "weakness", 1);
        physiology.TryIntervene(patient, "cognition", 31);
        physiology.TryIntervene(patient, "phobia", 3);
        brain.Damage = 20;
        brain.Fear = 70;
        var state = SComp<PhysiologyStateComponent>(patient);
        state.MetabolicDemand = 1;
        state.Temperature = 310.15f;
        state.OxygenDelivery = 0;
        foreach (var condition in Enum.GetValues<MedicalNeurologicalCondition>())
            brain.RecoveryRemaining[condition] = 10;
        neurology.AdvanceRecovery(patient, brain, 20);
        Assert.That(brain.RecoveryRemaining.Values.All(v => v == 10), Is.True);
        state.OxygenDelivery = 1;
        brain.OxygenDebt = 16;
        neurology.AdvanceRecovery(patient, brain, 20);
        Assert.That(brain.RecoveryRemaining.Count, Is.EqualTo(9));
        brain.OxygenDebt = 0;
        brain.RecoveryRemaining.Remove(MedicalNeurologicalCondition.Aphasia);
        neurology.AdvanceRecovery(patient, brain, 10);
        Assert.That(brain.Aphasia, Is.True, "Persistent deficits do not recover spontaneously.");
        Assert.That(brain.CognitiveSymptoms, Is.EqualTo(MedicalCognitiveSymptoms.None));
        Assert.That(brain.ColorVision, Is.EqualTo(MedicalColorVision.Normal));
        Assert.That(brain.MuscleWeakness, Is.False);
        Assert.That(brain.Phobia, Is.EqualTo(MedicalPhobia.None));
        Assert.That(brain.Fear, Is.Zero);
        Assert.That(brain.Damage, Is.EqualTo(20), "Symptom recovery never repairs physical brain injury.");
        Assert.That(neurology.Rehabilitate(brain), Is.True);
        Assert.That(brain.Aphasia, Is.True, "Surgery starts recovery, not instant symptom removal.");
        Assert.That(brain.RecoveryRemaining[MedicalNeurologicalCondition.Aphasia], Is.EqualTo(120));
        Assert.That(SEntMan.System<Content.Shared.Body.Systems.BloodstreamSystem>().TryAddToBloodstream(patient,
            new Content.Shared.Chemistry.Components.Solution([new("MedicalTestNeurorestorative", 5)])), Is.True);
        neurology.AdvanceRecovery(patient, brain, 29);
        Assert.That(brain.Aphasia, Is.True);
        neurology.AdvanceRecovery(patient, brain, 1);
        Assert.That(brain.Aphasia, Is.False);
        Assert.That(brain.Damage, Is.EqualTo(20));
        brain.Aphasia = true;
        brain.Damage = 100;
        Assert.That(neurology.Rehabilitate(brain), Is.False);
        brain.RecoveryRemaining[MedicalNeurologicalCondition.Aphasia] = 1;
        neurology.AdvanceRecovery(patient, brain, 100);
        Assert.That(brain.Aphasia, Is.True);
        neurology.Reset((SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(patient).Single().Owner, brain));
        Assert.That(brain.RecoveryRemaining, Is.Empty);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void CognitiveSymptomsAffectIdentityWritingThrowsAndHandWorkOnly()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var other = SSpawn("MobMedicalTestHuman");
        var physiology = SEntMan.System<PhysiologySystem>();
        Assert.That(physiology.TryIntervene(patient, "cognition", 31f), Is.True);
        Assert.That(Identity.Name(other, SEntMan, patient), Is.EqualTo("Unfamiliar person"));
        Assert.That(Identity.Name(other, SEntMan), Is.Not.EqualTo("Unfamiliar person"), "Voice/chat identity must remain unchanged.");
        Assert.That(Identity.Name(patient, SEntMan, patient), Is.Not.EqualTo("Unfamiliar person"));
        var paper = SSpawn("Paper");
        var message = new PaperComponent.PaperInputTextMessage("Help!") { Actor = patient };
        SEntMan.EventBus.RaiseLocalEvent(paper, message);
        Assert.That(SComp<PaperComponent>(paper).Content, Is.EqualTo("eHlp!"));
        message = new PaperComponent.PaperInputTextMessage("eHlp! More!") { Actor = patient };
        SEntMan.EventBus.RaiseLocalEvent(paper, message);
        Assert.That(SComp<PaperComponent>(paper).Content, Is.EqualTo("eHlp! oMre!"));
        message = new PaperComponent.PaperInputTextMessage("eHlp! oMre!") { Actor = patient };
        SEntMan.EventBus.RaiseLocalEvent(paper, message);
        Assert.That(SComp<PaperComponent>(paper).Content, Is.EqualTo("eHlp! oMre!"));
        var throwing = new BeforeThrowEvent(paper, Vector2.UnitX, 1f, patient);
        SEntMan.EventBus.RaiseLocalEvent(patient, ref throwing);
        Assert.That(throwing.Direction, Is.Not.EqualTo(Vector2.UnitX));
        Assert.That(throwing.Direction.Length(), Is.InRange(0.65f, 1.35f));
        var normalThrow = new BeforeThrowEvent(paper, Vector2.UnitX, 1f, other);
        SEntMan.EventBus.RaiseLocalEvent(other, ref normalThrow);
        Assert.That(normalThrow.Direction, Is.EqualTo(Vector2.UnitX));
        var work = new DoAfterArgs(SEntMan, patient, 4f, new AwaitedDoAfterEvent(), patient)
        {
            NeedHand = true,
            BreakOnHandChange = false,
            BreakOnDropItem = false,
            RequireCanInteract = false,
        };
        var doAfters = SEntMan.System<SharedDoAfterSystem>();
        Assert.That(doAfters.TryStartDoAfter(work, out var id), Is.True);
        Assert.That(SComp<DoAfterComponent>(patient).DoAfters[id!.Value.Index].Args.Delay.TotalSeconds, Is.EqualTo(8f));
        Assert.That(work.Delay.TotalSeconds, Is.EqualTo(4f), "Do not mutate and repeatedly multiply the caller's arguments.");
        var speech = new AccentGetEvent(patient, "This patient needs treatment.");
        SEntMan.EventBus.RaiseLocalEvent(patient, ref speech);
        Assert.That(speech.Message, Is.EqualTo("This patient needs treatment."));
        Assert.That(physiology.TryIntervene(other, "cognition", 31f), Is.True);
        Assert.That(physiology.TryIntervene(patient, "cognition", 32f), Is.False);
        Assert.That(physiology.TryIntervene(patient, "phobia", 6f), Is.False);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void OnlyNewMechanicalAndHypoxicInjuryEarnsRolls()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var physiology = SEntMan.System<PhysiologySystem>();
        var brain = body.EnumerateOrgans<CerebralHealthComponent>(patient).Single();
        SEntMan.System<MedicalWoundSystem>().TryInjure(patient, "Head", "Blunt", 45f);
        Assert.That(brain.Comp2.Damage, Is.EqualTo(2.5f));
        Assert.That(brain.Comp2.NeurologicalInjuryRemainder, Is.EqualTo(2.5f));
        for (var i = 0; i < 20; i++)
        {
            physiology.Scan(patient);
            physiology.Inspect(patient);
            physiology.Update(0f);
        }
        Assert.That(brain.Comp2.NeurologicalInjuryRemainder, Is.EqualTo(2.5f));
        physiology.TryIntervene(patient, "heart", 0f);
        physiology.Update(35f);
        Assert.That(brain.Comp2.Damage, Is.EqualTo(7.5f).Within(0.01f));
        Assert.That(brain.Comp2.NeurologicalInjuryRemainder, Is.EqualTo(7.5f).Within(0.01f));
        Assert.That(brain.Comp2.ColorVision, Is.EqualTo(MedicalColorVision.Normal));
        Assert.That(brain.Comp2.Aphasia || brain.Comp2.MuscleWeakness, Is.False);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void RollsAreIndependentOfInjuryChunkSizeAndRejectInvalidIncrements()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var brain = SEntMan.System<BodySystem>().EnumerateOrgans<CerebralHealthComponent>(patient).Single();
        var neurology = SEntMan.System<MedicalNeurologySystem>();
        var random = Server.ResolveDependency<IRobustRandom>();
        random.SetSeed(12345);
        neurology.AddInjury((brain.Owner, brain.Comp2), 90f);
        var expected = (brain.Comp2.ColorVision, brain.Comp2.Aphasia, brain.Comp2.MuscleWeakness, brain.Comp2.CognitiveSymptoms, brain.Comp2.Phobia);
        neurology.Reset((brain.Owner, brain.Comp2));
        random.SetSeed(12345);
        for (var i = 0; i < 360; i++)
            neurology.AddInjury((brain.Owner, brain.Comp2), 0.25f);
        Assert.That((brain.Comp2.ColorVision, brain.Comp2.Aphasia, brain.Comp2.MuscleWeakness, brain.Comp2.CognitiveSymptoms, brain.Comp2.Phobia), Is.EqualTo(expected));
        Assert.That(brain.Comp2.NeurologicalInjuryRemainder, Is.Zero);
        foreach (var invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            neurology.AddInjury((brain.Owner, brain.Comp2), invalid);
        Assert.That(brain.Comp2.NeurologicalInjuryRemainder, Is.Zero);
        Assert.That((brain.Comp2.ColorVision, brain.Comp2.Aphasia, brain.Comp2.MuscleWeakness, brain.Comp2.CognitiveSymptoms, brain.Comp2.Phobia), Is.EqualTo(expected));
        random.SetSeed(12345);
        neurology.AddInjury((brain.Owner, brain.Comp2), 1000f);
        Assert.That(brain.Comp2.ColorVision, Is.Not.EqualTo(MedicalColorVision.Normal));
        Assert.That(brain.Comp2.Aphasia && brain.Comp2.MuscleWeakness, Is.True);
        Assert.That(brain.Comp2.CognitiveSymptoms, Is.EqualTo((MedicalCognitiveSymptoms) 31));
        Assert.That(brain.Comp2.Phobia, Is.Not.EqualTo(MedicalPhobia.None));
        Assert.That(brain.Comp2.RecoveryRemaining.Count, Is.InRange(1, 8), "Acquired deficits include temporary and persistent courses.");
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void PhysicalTransplantMovesSymptomsAndRejuvenationClearsThem()
    {
        var patient = SSpawn("MobMedicalTestHuman");
        var donor = SSpawn("MobMedicalTestHuman");
        var body = SEntMan.System<BodySystem>();
        var physiology = SEntMan.System<PhysiologySystem>();
        var containers = SEntMan.System<SharedContainerSystem>();
        var outgoing = body.EnumerateOrgans<CerebralHealthComponent>(patient).Single();
        var incoming = body.EnumerateOrgans<CerebralHealthComponent>(donor).Single();
        physiology.TryIntervene(donor, "vision", 2f);
        physiology.TryIntervene(donor, "aphasia", 1f);
        physiology.TryIntervene(donor, "weakness", 1f);
        physiology.TryIntervene(donor, "cognition", 31f);
        physiology.TryIntervene(donor, "phobia", 1f);
        incoming.Comp2.Fear = 65f;
        incoming.Comp2.RecoveryRemaining[MedicalNeurologicalCondition.Alexia] = 135f;
        Assert.That(SComp<MovementSpeedModifierComponent>(donor).WalkSpeedModifier, Is.EqualTo(0.7f).Within(0.001f));
        var speech = new AccentGetEvent(donor, "This patient needs treatment.");
        SEntMan.EventBus.RaiseLocalEvent(donor, ref speech);
        Assert.That(speech.Message, Is.EqualTo("... patient ... treatment."));
        Assert.That(containers.Remove(incoming.Owner, containers.GetContainer(donor, BodyComponent.ContainerID)), Is.True);
        Assert.That(SComp<PhysiologyComponent>(donor).Aphasia, Is.False);
        Assert.That(SComp<PhysiologyComponent>(donor).CognitiveSymptoms, Is.EqualTo(MedicalCognitiveSymptoms.None));
        Assert.That(SComp<PhysiologyComponent>(donor).Phobia, Is.EqualTo(MedicalPhobia.None));
        Assert.That(SComp<PhysiologyComponent>(donor).MuscleWeakness, Is.False);
        Assert.That(SComp<PhysiologyComponent>(donor).NeurologicalVision, Is.EqualTo(MedicalColorVision.Normal));
        Assert.That(containers.Remove(outgoing.Owner, containers.GetContainer(patient, BodyComponent.ContainerID)), Is.True);
        Assert.That(containers.Insert(incoming.Owner, containers.GetContainer(patient, BodyComponent.ContainerID)), Is.True);
        var scan = physiology.Scan(patient)!;
        Assert.That(scan.Aphasia && scan.MuscleWeakness, Is.True);
        Assert.That(scan.CognitiveSymptoms, Is.EqualTo((MedicalCognitiveSymptoms) 31));
        Assert.That(scan.Phobia, Is.EqualTo(MedicalPhobia.Doors));
        Assert.That(scan.Fear, Is.EqualTo(65f));
        Assert.That(scan.NeurologicalConditions.Single(c => c.Condition == MedicalNeurologicalCondition.Alexia).RecoverySeconds, Is.EqualTo(135f));
        Assert.That(scan.NeurologicalVision, Is.EqualTo(MedicalColorVision.Deuteranopia));
        Assert.That(SComp<MovementSpeedModifierComponent>(patient).WalkSpeedModifier, Is.EqualTo(0.7f).Within(0.001f));
        SEntMan.System<RejuvenateSystem>().PerformRejuvenate(patient);
        Assert.That(incoming.Comp2.ColorVision, Is.EqualTo(MedicalColorVision.Normal));
        Assert.That(incoming.Comp2.Aphasia || incoming.Comp2.MuscleWeakness, Is.False);
        Assert.That(incoming.Comp2.CognitiveSymptoms, Is.EqualTo(MedicalCognitiveSymptoms.None));
        Assert.That(incoming.Comp2.Phobia, Is.EqualTo(MedicalPhobia.None));
        Assert.That(incoming.Comp2.Fear, Is.Zero);
        Assert.That(incoming.Comp2.RecoveryRemaining, Is.Empty);
        Assert.That(SComp<PhysiologyComponent>(patient).MuscleWeakness, Is.False);
        Assert.That(SComp<MovementSpeedModifierComponent>(patient).WalkSpeedModifier, Is.EqualTo(1f).Within(0.001f));
        var ordinary = SSpawn("MobMedicalTestHuman");
        Assert.That(physiology.TryIntervene(ordinary, "weakness", 1f), Is.True);
        speech = new AccentGetEvent(ordinary, "This patient needs treatment.");
        SEntMan.EventBus.RaiseLocalEvent(ordinary, ref speech);
        Assert.That(speech.Message, Is.EqualTo("This patient needs treatment."));
    }
}
