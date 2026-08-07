namespace ADDS.PIM.Domain.Security;

/// <summary>
/// Second-factor methods which can satisfy a target group's policy. Values are
/// persisted as a flags value; unknown values are never accepted.
/// </summary>
[Flags]
public enum SecondFactorType
{
    None = 0,
    Fido2 = 1,
    Totp = 2
}

public static class SecondFactorTypeExtensions
{
    private const SecondFactorType KnownValues = SecondFactorType.Fido2 | SecondFactorType.Totp;

    public static bool IsValidPolicyValue(this SecondFactorType value)
        => value != SecondFactorType.None && (value & ~KnownValues) == 0;
}
