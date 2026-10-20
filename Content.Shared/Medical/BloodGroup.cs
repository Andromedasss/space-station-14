using Robust.Shared.GameStates;

namespace Content.Shared.Medical;

/// <summary>
/// Blood group of a patient or of a stored blood bag.
/// The value encodes the ABO group in the upper bits and the Rh factor in the lowest bit.
/// </summary>
public enum BloodGroup : byte
{
    ONegative = 0,
    OPositive = 1,
    ANegative = 2,
    APositive = 3,
    BNegative = 4,
    BPositive = 5,
    ABNegative = 6,
    ABPositive = 7,
}

/// <summary>
/// Compatibility rules of the blood groups.
/// </summary>
public static class BloodGroups
{
    /// <summary>
    /// A donor may give to a recipient when the donor carries no antigen the recipient lacks.
    /// O negative blood is therefore universal, AB positive blood only fits AB positive recipients.
    /// </summary>
    public static bool CanDonate(BloodGroup donor, BloodGroup recipient)
    {
        var donorAbo = AntigenMask(donor);
        var recipientAbo = AntigenMask(recipient);
        if ((donorAbo & ~recipientAbo) != 0)
            return false;

        // Rh positive blood carries the D antigen, only Rh positive recipients may take it.
        if (IsPositive(donor) && !IsPositive(recipient))
            return false;

        return true;
    }

    public static string Name(BloodGroup group) => group switch
    {
        BloodGroup.ONegative => "O negative",
        BloodGroup.OPositive => "O positive",
        BloodGroup.ANegative => "A negative",
        BloodGroup.APositive => "A positive",
        BloodGroup.BNegative => "B negative",
        BloodGroup.BPositive => "B positive",
        BloodGroup.ABNegative => "AB negative",
        BloodGroup.ABPositive => "AB positive",
        _ => "unknown",
    };

    public static BloodGroup Random(System.Random random) => (BloodGroup) random.Next(0, 8);

    private static int AntigenMask(BloodGroup group) => (int) group / 2 switch
    {
        0 => 0b00, // O carries neither antigen
        1 => 0b01, // A carries the A antigen
        2 => 0b10, // B carries the B antigen
        3 => 0b11, // AB carries both
        _ => 0,
    };

    private static bool IsPositive(BloodGroup group) => (int) group % 2 == 1;
}

/// <summary>
/// A sealed bag of donor blood.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class BloodBagComponent : Component
{
    [DataField, AutoNetworkedField]
    public BloodGroup Group = BloodGroup.ONegative;

    /// <summary>
    /// How much of a full blood volume the bag still holds, from 0 to 1.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Volume = 0.25f;
}

/// <summary>
/// A vial of blood drawn from a patient.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class MedicalBloodSampleComponent : Component
{
    /// <summary>The group of the patient the sample came from.</summary>
    [DataField, AutoNetworkedField]
    public BloodGroup Group = BloodGroup.ONegative;

    /// <summary>The sample has been through the laboratory and carries a result.</summary>
    [DataField, AutoNetworkedField]
    public bool Analyzed;

    /// <summary>Blood volume of the donor at the time of the draw.</summary>
    [DataField, AutoNetworkedField]
    public float BloodVolume;

    /// <summary>Drug concentrations found in the sample.</summary>
    [DataField, AutoNetworkedField]
    public float Sedative;

    [DataField, AutoNetworkedField]
    public float Analgesic;

    [DataField, AutoNetworkedField]
    public float Neurorestorative;

    [DataField, AutoNetworkedField]
    public float Anxiolytic;

    [DataField, AutoNetworkedField]
    public float Inotrope;

    /// <summary>Signs of infection in the donor.</summary>
    [DataField, AutoNetworkedField]
    public float Contamination;

    [DataField, AutoNetworkedField]
    public float Infection;
}
