using Mdex.Core.Security;
using Mdex.Tokens;

var store = new TokenStore(TokenPaths.Default());
var admin = new TokenAdmin(store);

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("mdex-token new --role <read|write-intent|fullwrite> --label <name>");
    Console.WriteLine("mdex-token list");
    Console.WriteLine("mdex-token revoke <label>");
    Console.WriteLine();
    Console.WriteLine($"Token store: {TokenPaths.Default()}");
    return 0;
}

try
{
    if (args is ["new", ..])
    {
        var role = ParseRole(FlagValue(args, "--role"));
        var newLabel = FlagValue(args, "--label") ?? throw new AppError("new needs --label <name>");
        var raw = admin.New(role, newLabel);
        Console.WriteLine($"Token created for '{newLabel}' (role {Roles.ToWire(role)}). Shown once — store it now:");
        Console.WriteLine();
        Console.WriteLine($"    {raw}");
        Console.WriteLine();
        Console.WriteLine("The gateway picks up the new token automatically (file watch); no restart needed.");
        return 0;
    }

    if (args is ["list"])
    {
        var entries = admin.List();
        if (entries.Count == 0) { Console.WriteLine("No tokens."); return 0; }
        foreach (var e in entries)
            Console.WriteLine($"{e.Label,-20}  {e.Role,-12}  {e.Created}");
        return 0;
    }

    if (args is ["revoke", var label])
    {
        if (!admin.Revoke(label)) throw new AppError($"no token labelled '{label}'");
        Console.WriteLine($"Revoked '{label}'. The gateway drops it automatically (file watch); no restart needed.");
        return 0;
    }

    throw new AppError($"unknown command: {string.Join(' ', args)}");
}
catch (AppError ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static string? FlagValue(string[] args, string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static Role ParseRole(string? wire)
{
    if (wire is null) throw new AppError("new needs --role <read|write-intent|fullwrite>");
    if (!Roles.TryParse(wire, out var role)) throw new AppError($"invalid role '{wire}'");
    return role;
}

sealed class AppError(string message) : Exception(message);
