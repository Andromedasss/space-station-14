using Robust.Shared.GameStates;

namespace Content.Shared.Medical;

/// <summary>
/// Opts a test patient into server-authoritative physiology instead of damage thresholds.
/// Not intended for arbitrary mobs: the patient must have a bloodstream, respirator and medical organs.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PhysiologyComponent : Component;
