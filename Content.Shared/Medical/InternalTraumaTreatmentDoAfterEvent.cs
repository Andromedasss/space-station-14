using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical;

[Serializable, NetSerializable]
public sealed partial class InternalTraumaTreatmentDoAfterEvent : DoAfterEvent
{
    public NetEntity Organ;
    public NetEntity Table;
    public NetEntity Part;
    public int Revision;

    public InternalTraumaTreatmentDoAfterEvent(NetEntity organ, NetEntity table, NetEntity part, int revision)
    {
        Organ = organ;
        Table = table;
        Part = part;
        Revision = revision;
    }

    public override DoAfterEvent Clone() => new InternalTraumaTreatmentDoAfterEvent(Organ, Table, Part, Revision);
}
