namespace boottorrent_lib.util;

using System.IO;
using System.Text.RegularExpressions;

public class NameUtil
{
    public static string ToFilePathName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "unnamed_file";

        // Get all invalid filename chars from the system
        var invalidChars = Path.GetInvalidFileNameChars();
        
        // Build a regex that matches any of these invalid chars
        string invalidRegex = $"[{Regex.Escape(new string(invalidChars))}]";

        // Replace invalid chars with underscores
        string safeName = Regex.Replace(input, invalidRegex, "_");

        // Optionally, trim leading/trailing underscores or dots
        safeName = safeName.Trim('_', '.');

        // Prevent empty filenames
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "unnamed_file";

        return safeName;
    }
}