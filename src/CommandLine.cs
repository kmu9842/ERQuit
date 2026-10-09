using System;
using System.Collections.Generic;
using System.Text;

namespace ERQuit
{
    internal static class CommandLine
    {
        internal static string Quote(string value)
        {
            if (value.IndexOf('\0') >= 0) throw new ArgumentException("NUL in argument");
            StringBuilder result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result.Append('\\', slashes * 2 + 1);
                else result.Append('\\', slashes);
                result.Append(c);
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            return result.Append('"').ToString();
        }

        internal static string Build(string executable, IEnumerable<string> arguments)
        {
            StringBuilder result = new StringBuilder(Quote(executable));
            foreach (string argument in arguments) result.Append(' ').Append(Quote(argument));
            if (result.Length >= 32767) throw new ArgumentException("게임 실행 인수가 너무 깁니다.");
            return result.ToString();
        }
    }
}
