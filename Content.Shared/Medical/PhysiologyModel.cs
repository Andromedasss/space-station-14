namespace Content.Shared.Medical;

/// <summary>
/// Dimensionless, deliberately simplified physiology for the experimental human patient.
/// Respiratory supply is NOT a clinical oxygen saturation measurement.
/// </summary>
public static class PhysiologyModel
{
    public const float AdequateDelivery = 0.5f;
    public const float UnconsciousDebt = 15f;
    public const float InjuryDebt = 30f;
    public const float MaximumDebt = 120f;
    public const float MaximumBrainDamage = 100f;
    public const float UnconsciousBrainDamage = 50f;
    public const float AnestheticDose = 4f;

    // Gameplay Q10 model, not a clinical prediction. Cooling buys time, never reverses injury.
    public static float MetabolicDemand(float kelvin) => Math.Clamp(MathF.Pow(2f, (kelvin - 310.15f) / 10f), 0.25f, 2f);

    public static float TemperaturePump(float kelvin) => Math.Clamp((kelvin - 288.15f) / 15f, 0.1f, 1f);

    public static float TemperatureMobility(float kelvin) => Math.Clamp((kelvin - 293.15f) / 15f, 0.2f, 1f);

    /// <summary>
    /// Heart rate and blood pressure of the experimental patient.
    /// The rate answers the stress on the body: blood loss and hypoxia drive it up, pain and
    /// fear add to it, fever quickens it, chilling and sedation slow it down, and a failing
    /// heart or a collapsing circulation weakens it towards a thready pulse. The pressure
    /// follows the volume and the circulation, with pain and fever tightening the vessels.
    /// </summary>
    public static (float Pulse, float Systolic, float Diastolic) Vitals(float blood, float heart,
        float circulation, float kelvin, float pain, float sedation, float oxygenDebt, float fear)
    {
        var volume = Math.Clamp(blood, 0f, 1f);
        var pump = Math.Clamp(heart, 0f, 1f);
        var perfusion = volume * Math.Clamp(circulation, 0f, 1f);

        var celsius = kelvin - 273.15f;
        var fever = Math.Max(0f, celsius - 37f);
        var chill = Math.Max(0f, 37f - celsius);
        // Pain and fever tighten the vessels and raise the pressure.
        var tone = 1f + 0.0015f * pain + 0.001f * fever;

        // The pressure follows the volume and the circulation. Assisted circulation keeps the
        // pressure up even when the heart itself cannot beat.
        var pressure = Math.Clamp(perfusion * tone, 0f, 1.5f);
        var systolic = Math.Clamp(120f * pressure, 0f, 240f);
        var diastolic = Math.Clamp(80f * pressure, 0f, 150f);

        // The pulse is the heart's own beat. Without a working heart or blood there is none.
        if (pump <= 0f || volume <= 0.05f)
            return (0f, systolic, diastolic);

        var bloodLoss = Math.Clamp(1f - volume, 0f, 1f);
        var hypoxia = Math.Clamp(oxygenDebt / UnconsciousDebt, 0f, 1f);
        // A perfusion this low is terminal shock, the beat thins out instead of racing.
        var collapse = Math.Clamp(1f - perfusion / 0.35f, 0f, 1f);

        var pulse = 80f
            + 78f * bloodLoss
            + 52f * hypoxia
            + 0.24f * pain
            + 0.28f * fear
            + 10f * fever
            - 2.2f * chill
            - 2.4f * Math.Min(sedation, 10f)
            - 30f * (1f - pump)
            - 30f * collapse;
        return (Math.Clamp(pulse, 0f, 210f), systolic, diastolic);
    }

    public static float SedativeRespiratoryDrive(float dose)
    {
        return Math.Clamp((10f - Math.Max(2f, dose)) / 8f, 0f, 1f);
    }

    public static float NewlyExposedDamage(float previous, float applied, float threshold)
    {
        if (!float.IsFinite(previous) || !float.IsFinite(applied) || !float.IsFinite(threshold) || threshold < 0f)
            return 0f;
        return Math.Max(0f, Math.Max(0f, previous + applied - threshold) - Math.Max(0f, previous - threshold));
    }

    /// <summary>
    /// Only the newly applied portion beyond the head's tissue protection reaches the brain.
    /// Small repeated hits count, while healing external tissue never repairs existing brain injury.
    /// </summary>
    public static float HeadTrauma(string type, float previousInjury, float appliedDamage)
    {
        var (threshold, transfer) = type switch
        {
            "Blunt" => (40f, 0.5f),
            "Piercing" => (10f, 1f),
            "Slash" => (60f, 0.25f),
            _ => (0f, 0f),
        };
        return (Math.Max(0f, previousInjury + appliedDamage - threshold)
            - Math.Max(0f, previousInjury - threshold)) * transfer;
    }

    // Gameplay burden, not a clinical pain scale. Dead tissue and mechanical parts do not signal pain.
    public static float PartPain(float injury, bool fractured, int access, float ischemia, bool insensate)
    {
        if (insensate)
            return 0f;
        return Math.Clamp(Math.Max(0f, injury) * 0.5f + (fractured ? 20f : 0f)
            + Math.Clamp(access, 0, 3) * 5f + Math.Clamp(ischemia / 9f, 0f, 20f), 0f, 100f);
    }

    public static float PerceivedPain(float burden, float analgesicDose)
    {
        // Non-sedating analgesia has a ceiling. Extra doses do not confer extra benefit.
        return Math.Clamp(burden - Math.Clamp(analgesicDose, 0f, 5f) * 12f, 0f, 100f);
    }

    public static float PainSprintModifier(float pain)
    {
        return 1f - 0.15f * Math.Clamp((pain - 40f) / 60f, 0f, 1f);
    }

    public static float Delivery(float bloodFraction, float heartFunction, float lungFunction, float respiratorySupply)
    {
        return Math.Clamp(bloodFraction, 0f, 1f) * Math.Clamp(heartFunction, 0f, 1f)
            * Math.Clamp(lungFunction, 0f, 1f) * Math.Clamp(respiratorySupply, 0f, 1f);
    }

    /// <summary>
    /// Integrates exposure above the injury threshold, including partial steps crossing it.
    /// Brain injury does not spontaneously heal and cannot exceed the irreversible limit.
    /// </summary>
    public static (float Debt, float BrainDamage) Advance(float debt, float brainDamage, float delivery, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(seconds));

        var rate = delivery < AdequateDelivery
            ? 1f - Math.Clamp(delivery, 0f, 1f) / AdequateDelivery
            : -2f;
        var nextDebt = Math.Clamp(debt + rate * seconds, 0f, MaximumDebt);
        var exposure = 0f;
        if (rate > 0f)
            exposure = Math.Clamp(seconds - Math.Max(0f, InjuryDebt - debt) / rate, 0f, seconds);
        else if (debt > InjuryDebt)
            exposure = Math.Min(seconds, (debt - InjuryDebt) / -rate);

        return (nextDebt, Math.Clamp(brainDamage + exposure, 0f, MaximumBrainDamage));
    }
}
