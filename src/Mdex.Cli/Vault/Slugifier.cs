using System.Globalization;
using System.Text;

namespace Mdex.Cli.Vault;

public static class Slugifier
{
    public static string Slugify(string input)
    {
        var lower = input.ToLower(CultureInfo.InvariantCulture);
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            bool keep = ch is >= 'a' and <= 'z' or >= '0' and <= '9'
                        || ch is 'ä' or 'ö' or 'ü' or 'ß' or '-';
            sb.Append(keep ? ch : '-');
        }
        var collapsed = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return collapsed;
    }
}
