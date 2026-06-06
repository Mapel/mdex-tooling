namespace Mdex.Core.Security;

/// <summary>Capability ladder: each level includes the ones below it.</summary>
public enum Role { Read = 0, WriteIntent = 1, FullWrite = 2 }

public static class Roles
{
    public static string ToWire(Role role) => role switch
    {
        Role.Read => "read",
        Role.WriteIntent => "write-intent",
        Role.FullWrite => "fullwrite",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public static bool TryParse(string? wire, out Role role)
    {
        switch (wire)
        {
            case "read": role = Role.Read; return true;
            case "write-intent": role = Role.WriteIntent; return true;
            case "fullwrite": role = Role.FullWrite; return true;
            default: role = default; return false;
        }
    }
}
