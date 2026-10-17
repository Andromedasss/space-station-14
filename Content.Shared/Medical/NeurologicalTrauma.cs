using System.Text.RegularExpressions;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical;

// Acquired neurological symptoms, not congenital traits or a clinical diagnostic model.
[Serializable, NetSerializable]
public enum MedicalColorVision : byte
{
    Normal,
    Protanopia,
    Deuteranopia,
    Tritanopia,
    Achromatopsia,
}

[Flags, Serializable, NetSerializable]
public enum MedicalCognitiveSymptoms : byte
{
    None = 0,
    Prosopagnosia = 1,
    Alexia = 2,
    Agraphia = 4,
    Tremor = 8,
    Dysmetria = 16,
}

[Serializable, NetSerializable]
public enum MedicalPhobia : byte
{
    None,
    Doors,
    Uniforms,
    Blood,
    Fire,
    Corpses,
}

[Serializable, NetSerializable]
public enum MedicalNeurologicalCondition : byte
{
    ColorVision,
    Aphasia,
    Weakness,
    Prosopagnosia,
    Alexia,
    Agraphia,
    Tremor,
    Dysmetria,
    Phobia,
}

public static class NeurologicalTrauma
{
    public const float TemporaryChance = 0.7f;
    public const float RecoverySeconds = 240f;
    public const float SurgicalRecoverySeconds = 120f;
    public const float TherapeuticDose = 2f;
    public const float DamagePerRoll = 10f;
    public const float SymptomChance = 0.35f;
    public const float WeaknessSpeed = 0.7f;

    public static float AdvanceFear(float fear, int visibleTriggers, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f)
            return fear;
        return Math.Clamp(fear + seconds * (visibleTriggers > 0 ? 4f * Math.Min(visibleTriggers, 5) : -8f), 0f, 100f);
    }

    public static float PrecisionDelay(MedicalCognitiveSymptoms symptoms)
        => 1f + (symptoms.HasFlag(MedicalCognitiveSymptoms.Tremor) ? 0.5f : 0f)
            + (symptoms.HasFlag(MedicalCognitiveSymptoms.Dysmetria) ? 0.5f : 0f);

    public static string AgraphicWriting(string text)
    {
        return Writing.Replace(text, match =>
        {
            if (match.Value[0] == '[')
                return match.Value;
            var letters = match.Value.ToCharArray();
            for (var i = 0; i + 1 < letters.Length; i += 4)
                (letters[i], letters[i + 1]) = (letters[i + 1], letters[i]);
            return new string(letters);
        });
    }

    private static readonly Regex Words = new(@"\p{L}+", RegexOptions.Compiled);
    private static readonly Regex Writing = new(@"\[[^\]\r\n]*\]|\p{L}+", RegexOptions.Compiled);
    private static readonly Regex AlexiaWords = new(@"(?:<[^>]*>|\[[^\]]*\])|\p{L}+", RegexOptions.Compiled);

    /// <summary>Gameplay expressive aphasia: missing words, not altered commands or emotes.</summary>
    public static string AphasicSpeech(string message)
    {
        var index = 0;
        return Words.Replace(message, match => index++ % 2 == 0 ? "..." : match.Value);
    }

    /// <summary>Gameplay alexia: letters present but scrambled when reading.</summary>
    public static string AlexicText(string text)
    {
        return AlexiaWords.Replace(text, match =>
        {
            var value = match.Value;
            if (value.StartsWith("<") || value.StartsWith("["))
                return value;
            var letters = value.ToCharArray();
            Array.Reverse(letters);
            return new string(letters);
        });
    }
}
