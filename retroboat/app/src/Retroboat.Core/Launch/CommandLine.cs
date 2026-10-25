using System;

namespace Retroboat.Launch
{
    /// <summary>
    /// Separa a linha de comando do retroboat-launch em "programa a abrir" e "argumentos", preservando a
    /// linha original. Os argumentos que o ES montou (%ROM% entre aspas etc.) são repassados exatamente
    /// como vieram.
    /// </summary>
    public static class CommandLine
    {
        /// <summary>Primeiro item da linha (com ou sem aspas) e o resto, sem alterações.</summary>
        public static string SplitFirst(string commandLine, out string rest)
        {
            var text = (commandLine ?? "").TrimStart();
            if (text.Length == 0)
            {
                rest = "";
                return "";
            }

            string first;
            int end;
            if (text[0] == '"')
            {
                int close = text.IndexOf('"', 1);
                end = close < 0 ? text.Length : close + 1;
                first = text.Substring(1, (close < 0 ? text.Length : close) - 1);
            }
            else
            {
                end = 0;
                while (end < text.Length && !char.IsWhiteSpace(text[end]))
                    end++;

                first = text.Substring(0, end);
            }

            rest = text.Substring(end).TrimStart();
            return first;
        }

        /// <summary>Valor que vem depois de uma opção, ex.: o caminho depois de "-rom".</summary>
        public static string GetOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }
    }
}
