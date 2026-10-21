// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GpStudio;

internal static class CParagraphs
{
    public static string Apply(string source)
    {
        var output = new List<string>();
        bool macro = false;
        bool comment = false;
        foreach (string line in source.Replace("\r\n", "\n").TrimEnd().Split('\n'))
        {
            string current = line.Trim();
            string previous = output.Count == 0 ? "" : output[^1].Trim();
            bool protectedLine = macro || comment || current.StartsWith("#") || current.StartsWith("/*") || current.StartsWith("//");
            if (!protectedLine && current.Length != 0 && previous.Length != 0)
            {
                bool endsCheck = (previous.StartsWith("return ") || previous.StartsWith("goto ")) && previous.EndsWith(';');
                bool nextStatement = current != "}" && current != "};" && !current.StartsWith("else") && !current.StartsWith("case ") && current != "default:";
                bool apiCall = Regex.IsMatch(current, @"^(?:res|result) = (?:TEE|TEEC)_\w+\(");
                bool boundary = previous != "{" && !previous.EndsWith(':') && previous.EndsWith(';');
                bool declaration = Regex.IsMatch(previous, @"^(?:(?:const|static|volatile)\s+)*(?:TEE_\w+|TEEC_\w+|u?int(?:8|16|32|64)_t|size_t|bool|int|char|struct\s+\w+)\s+\**\w+.*;$");
                bool nextDeclaration = Regex.IsMatch(current, @"^(?:(?:const|static|volatile)\s+)*(?:TEE_\w+|TEEC_\w+|u?int(?:8|16|32|64)_t|size_t|bool|int|char|struct\s+\w+)\s+");
                if ((endsCheck && nextStatement) || (apiCall && boundary) || current == "cleanup:" ||
                    (declaration && !nextDeclaration && nextStatement))
                    output.Add("");
            }
            output.Add(line);
            if (!comment && current.StartsWith("/*") && !current.Contains("*/")) comment = true;
            else if (comment && current.Contains("*/")) comment = false;
            macro = (macro || current.StartsWith("#")) && current.EndsWith('\\');
        }
        return string.Join("\n", output) + "\n";
    }
}
