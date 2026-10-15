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
