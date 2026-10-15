namespace Content.Server.Medical;

/// <summary>
/// Injury and irreversible death belong to the physical brain, never the player session.
/// Only administrative rejuvenation can reset irreversible injury in this initial version.
/// </summary>
[RegisterComponent]
public sealed partial class CerebralHealthComponent : Component
{
    [DataField]
    public float OxygenDebt;

    [DataField]
    public float Damage;
}
