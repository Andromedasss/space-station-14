using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical;

[Serializable, NetSerializable]
public sealed partial class HealingDoAfterEvent : DoAfterEvent
{
    public NetEntity? Part;

    public override DoAfterEvent Clone() => new HealingDoAfterEvent { Part = Part };
}
