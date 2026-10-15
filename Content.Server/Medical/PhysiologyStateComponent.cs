using Content.Shared.DoAfter;

namespace Content.Server.Medical;

/// <summary>
/// Server-only observations. Kept bounded and sent only through an authorized debug command.
/// </summary>
[RegisterComponent]
public sealed partial class PhysiologyStateComponent : Component
{
    public bool AnatomyInitialized;

    [ViewVariables]
    public float BloodFraction;

    [ViewVariables]
    public float HeartFunction;

    [ViewVariables]
    public float CirculatoryFunction;

    [ViewVariables]
    public bool LifeSupport;

    public DoAfterId? Compressions;

    public bool CprActive;

    public float InotropeDose;

    [ViewVariables]
    public float LungFunction;

    [ViewVariables]
    public float RespiratorySupply;

    [ViewVariables]
    public float OxygenDelivery;

    [ViewVariables]
    public string LimitingFactor = "Not sampled";

    [ViewVariables]
    public Queue<string> History = new();

    public string? LastTransition;
}
