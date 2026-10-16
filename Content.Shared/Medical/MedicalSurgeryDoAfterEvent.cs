using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical;

[Serializable, NetSerializable]
public enum MedicalSurgeryAction : byte
{
    Incise,
    Clamp,
    Retract,
    Close,
    Extract,
    Insert,
    RepairFracture,
    RepairVessel,
    RepairProsthetic,
    Approximate,
    SawBone,
    SpreadBone,
    ApproximateBone,
    RepairBone,
}

[Serializable, NetSerializable]
public sealed partial class MedicalSurgeryDoAfterEvent : DoAfterEvent
{
    public NetEntity Part;
    public NetEntity Table;
    public NetEntity? Organ;
    public MedicalSurgeryAction Action;
    public int Revision;

    public MedicalSurgeryDoAfterEvent(NetEntity part, NetEntity table, NetEntity? organ,
        MedicalSurgeryAction action, int revision)
    {
        Part = part;
        Table = table;
        Organ = organ;
        Action = action;
        Revision = revision;
    }

    public override DoAfterEvent Clone() => new MedicalSurgeryDoAfterEvent(Part, Table, Organ, Action, Revision);
}
