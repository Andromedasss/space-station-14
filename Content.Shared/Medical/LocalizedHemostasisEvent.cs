namespace Content.Shared.Medical;

[ByRefEvent]
public record struct LocalizedHemostasisEvent(EntityUid? Part, float Amount, bool Handled = false);
