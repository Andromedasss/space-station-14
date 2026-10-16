using Content.Shared.Medical;
using NUnit.Framework;

namespace Content.Tests.Shared.Medical;

[TestFixture]
[TestOf(typeof(PhysiologyModel))]
public sealed class PhysiologyModelTest
{
    [TestCase(310.15f, 1f)]
    [TestCase(300.15f, 0.5f)]
    [TestCase(280.15f, 0.25f)]
    [TestCase(330.15f, 2f)]
    public void TemperatureDemandIsBounded(float kelvin, float expected)
    {
        Assert.That(PhysiologyModel.MetabolicDemand(kelvin), Is.EqualTo(expected).Within(0.001f));
    }

    [Test]
    public void AssistedCirculationDoesNotInventASpontaneousPulse()
    {
        var vitals = PhysiologyModel.Vitals(1f, 0f, 0.6f, 310.15f, 0f, 0f);
        Assert.That(vitals.Pulse, Is.Zero);
        Assert.That(vitals.Systolic, Is.EqualTo(72f).Within(0.001f));
        Assert.That(vitals.Diastolic, Is.EqualTo(48f).Within(0.001f));
    }

    [TestCase(0f, 1f)]
    [TestCase(2f, 1f)]
    [TestCase(4f, 0.75f)]
    [TestCase(6f, 0.5f)]
    [TestCase(10f, 0f)]
    [TestCase(30f, 0f)]
    public void SedativeDoseSuppressesRespiratoryDrive(float dose, float expected)
    {
        Assert.That(PhysiologyModel.SedativeRespiratoryDrive(dose), Is.EqualTo(expected));
    }

    [TestCase("Blunt", 0f, 10f, 0f)]
    [TestCase("Blunt", 35f, 10f, 2.5f)]
    [TestCase("Blunt", 60f, 10f, 5f)]
    [TestCase("Piercing", 0f, 16f, 6f)]
    [TestCase("Slash", 50f, 20f, 2.5f)]
    [TestCase("Heat", 100f, 20f, 0f)]
    public void HeadTraumaUsesOnlyNewDamageAboveProtection(string type, float previous, float applied, float expected)
    {
        Assert.That(PhysiologyModel.HeadTrauma(type, previous, applied), Is.EqualTo(expected));
    }

    [TestCase(0f, false, 0, 0f, false, 0f)]
    [TestCase(20f, false, 0, 0f, false, 10f)]
    [TestCase(0f, true, 3, 90f, false, 45f)]
    [TestCase(100f, true, 3, 180f, false, 100f)]
    [TestCase(100f, true, 3, 180f, true, 0f)]
    public void PainDependsOnTissueFractureAccessAndIschemia(float injury, bool fracture, int access,
        float ischemia, bool insensate, float expected)
    {
        Assert.That(PhysiologyModel.PartPain(injury, fracture, access, ischemia, insensate), Is.EqualTo(expected));
    }

    [TestCase(100f, 0f, 100f)]
    [TestCase(100f, 5f, 40f)]
    [TestCase(100f, 20f, 40f)]
    [TestCase(20f, 5f, 0f)]
    [TestCase(20f, -1f, 20f)]
    public void AnalgesiaHasACeilingAndCannotCreateNegativePain(float burden, float dose, float expected)
    {
        Assert.That(PhysiologyModel.PerceivedPain(burden, dose), Is.EqualTo(expected));
    }

    [TestCase(0f, 1f)]
    [TestCase(40f, 1f)]
    [TestCase(70f, 0.925f)]
    [TestCase(100f, 0.85f)]
    [TestCase(1000f, 0.85f)]
    public void PainSprintPenaltyIsBounded(float pain, float expected)
    {
        Assert.That(PhysiologyModel.PainSprintModifier(pain), Is.EqualTo(expected).Within(0.001f));
    }

    [TestCase(1f, 1f, 1f, 1f, 1f)]
    [TestCase(1f, 0f, 1f, 1f, 0f)]
    [TestCase(0f, 1f, 1f, 1f, 0f)]
    [TestCase(1f, 1f, 0f, 1f, 0f)]
    [TestCase(1f, 1f, 1f, 0f, 0f)]
    [TestCase(0.5f, 0.5f, 1f, 1f, 0.25f)]
    [TestCase(2f, 1f, 1f, 1f, 1f)]
    public void DeliveryRequiresAllInputs(float blood, float heart, float lungs, float supply, float expected)
    {
        Assert.That(PhysiologyModel.Delivery(blood, heart, lungs, supply), Is.EqualTo(expected));
    }

    [Test]
    public void HealthyPatientDoesNotAccumulateInjury()
    {
        Assert.That(PhysiologyModel.Advance(0f, 0f, 1f, 600f), Is.EqualTo((0f, 0f)));
    }

    [Test]
    public void InjuryStartsAfterGracePeriod()
    {
        Assert.That(PhysiologyModel.Advance(0f, 0f, 0f, 30f), Is.EqualTo((30f, 0f)));
        Assert.That(PhysiologyModel.Advance(0f, 0f, 0f, 40f), Is.EqualTo((40f, 10f)));
    }

    [Test]
    public void RestoredSupplyClearsDebtButNotInjury()
    {
        Assert.That(PhysiologyModel.Advance(40f, 10f, 1f, 20f), Is.EqualTo((0f, 15f)));
    }

    [Test]
    public void BrainDeathCannotSpontaneouslyHeal()
    {
        var dead = PhysiologyModel.Advance(0f, 0f, 0f, 300f);
        Assert.That(dead, Is.EqualTo((120f, 100f)));
        Assert.That(PhysiologyModel.Advance(dead.Debt, dead.BrainDamage, 1f, 300f), Is.EqualTo((0f, 100f)));
    }

    [TestCase(0f, 0f, 0f, 80f)]
    [TestCase(29f, 0f, 0f, 8f)]
    [TestCase(40f, 10f, 1f, 30f)]
    [TestCase(0f, 0f, 0.25f, 90f)]
    public void ResultsDoNotDependOnStepSize(float debt, float damage, float delivery, float seconds)
    {
        var expected = PhysiologyModel.Advance(debt, damage, delivery, seconds);
        for (var i = 0; i < 100; i++)
            (debt, damage) = PhysiologyModel.Advance(debt, damage, delivery, seconds / 100f);
        Assert.Multiple(() =>
        {
            Assert.That(debt, Is.EqualTo(expected.Debt).Within(0.001f));
            Assert.That(damage, Is.EqualTo(expected.BrainDamage).Within(0.001f));
        });
    }

    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void InvalidDurationIsRejected(float seconds)
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => PhysiologyModel.Advance(0f, 0f, 0f, seconds));
    }
}
