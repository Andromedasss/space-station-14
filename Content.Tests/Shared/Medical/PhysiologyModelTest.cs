using Content.Shared.Medical;
using NUnit.Framework;

namespace Content.Tests.Shared.Medical;

[TestFixture]
[TestOf(typeof(PhysiologyModel))]
public sealed class PhysiologyModelTest
{
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
