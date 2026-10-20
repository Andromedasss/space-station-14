using Robust.Shared.Audio;

namespace Content.Server.Medical;

[RegisterComponent]
public sealed partial class BedsideMonitorComponent : Component
{
    [DataField]
    public float Range = 2f;

    [DataField]
    public SoundSpecifier AlarmSound = new SoundPathSpecifier("/Audio/Items/Medical/healthscanner.ogg");

    public EntityUid? Patient;
    public TimeSpan NextAlarm;
}
