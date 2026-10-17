using Content.Shared.Medical;
using NUnit.Framework;

namespace Content.Tests.Shared.Medical;

public sealed class NeurologicalTraumaTest
{
    [Test]
    public void FearScalesWithExposureAndRecoversWithoutIt()
    {
        Assert.That(NeurologicalTrauma.AdvanceFear(0f, 1, 2f), Is.EqualTo(8f));
        Assert.That(NeurologicalTrauma.AdvanceFear(0f, 5, 2f), Is.EqualTo(40f));
        Assert.That(NeurologicalTrauma.AdvanceFear(0f, 20, 2f), Is.EqualTo(40f));
        Assert.That(NeurologicalTrauma.AdvanceFear(95f, 5, 2f), Is.EqualTo(100f));
        Assert.That(NeurologicalTrauma.AdvanceFear(10f, 0, 2f), Is.Zero);
        Assert.That(NeurologicalTrauma.AdvanceFear(10f, 5, float.NaN), Is.EqualTo(10f));
        Assert.That(NeurologicalTrauma.PrecisionDelay(MedicalCognitiveSymptoms.None), Is.EqualTo(1f));
        Assert.That(NeurologicalTrauma.PrecisionDelay(MedicalCognitiveSymptoms.Tremor), Is.EqualTo(1.5f));
        Assert.That(NeurologicalTrauma.PrecisionDelay(MedicalCognitiveSymptoms.Tremor | MedicalCognitiveSymptoms.Dysmetria), Is.EqualTo(2f));
    }

    [TestCase("", "")]
    [TestCase("123?!", "123?!")]
    [TestCase("Help!", "eHlp!")]
    [TestCase("[bold]Help![/bold]", "[bold]eHlp![/bold]")]
    [TestCase("Пациент", "аПцинет")]
    public void AgraphiaChangesLettersWithoutChangingMarkup(string input, string expected)
    {
        Assert.That(NeurologicalTrauma.AgraphicWriting(input), Is.EqualTo(expected));
    }

    [TestCase("", "")]
    [TestCase("123?!", "123?!")]
    [TestCase("Help!", "...!")]
    [TestCase("This patient needs treatment.", "... patient ... treatment.")]
    [TestCase("Пациенту нужна срочная помощь.", "... нужна ... помощь.")]
    public void ExpressiveAphasiaOmitsWordsWithoutLosingPunctuation(string input, string expected)
    {
        Assert.That(NeurologicalTrauma.AphasicSpeech(input), Is.EqualTo(expected));
    }
}
