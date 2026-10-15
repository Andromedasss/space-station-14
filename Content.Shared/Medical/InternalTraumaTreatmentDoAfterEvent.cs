using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical;

[Serializable, NetSerializable]
public sealed partial class InternalTraumaTreatmentDoAfterEvent : DoAfterEvent
{
    public NetEntity Organ;
    public NetEntity Table;

    public InternalTraumaTreatmentDoAfterEvent(NetEntity organ, NetEntity table)
    {
        Organ = organ;
        Table = table;
    }

    public override DoAfterEvent Clone() => new InternalTraumaTreatmentDoAfterEvent(Organ, Table);
}
