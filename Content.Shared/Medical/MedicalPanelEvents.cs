using Robust.Shared.Serialization;

namespace Content.Shared.Medical;

[Serializable, NetSerializable]
public sealed class MedicalPanelRequestEvent(NetEntity patient, int request) : EntityEventArgs
{
    public NetEntity Patient = patient;
    public int Request = request;
}

[Serializable, NetSerializable]
public sealed class MedicalPanelCloseEvent(int request) : EntityEventArgs
{
    public int Request = request;
}

[Serializable, NetSerializable]
public sealed class MedicalPanelStateEvent : EntityEventArgs
{
    public int Request;
    public NetEntity Patient;
    public string Name = string.Empty;
    public bool Accessible;
    public string SelectedZone = "Torso";
    public List<MedicalPanelPart> Parts = new();
    public List<MedicalPanelOperation> Operations = new();
}

// This panel exposes anatomy and surgical access, not analyzer-only diagnoses.
[Serializable, NetSerializable]
public sealed record MedicalPanelPart(string Category, int Access, List<string> Organs, int BoneAccess = 0);

[Serializable, NetSerializable]
public sealed class MedicalPanelOperation
{
    public string Label = string.Empty;
    public string? Requirement;
    public NetEntity Tool;
    public MedicalSurgeryDoAfterEvent? Surgery;
    public InternalTraumaTreatmentDoAfterEvent? Repair;
}

[Serializable, NetSerializable]
public sealed class MedicalPanelSelectEvent(int request, string category) : EntityEventArgs
{
    public int Request = request;
    public string Category = category;
}

[Serializable, NetSerializable]
public sealed class MedicalPanelOperateEvent(int request, MedicalPanelOperation operation) : EntityEventArgs
{
    public int Request = request;
    public MedicalPanelOperation Operation = operation;
}
