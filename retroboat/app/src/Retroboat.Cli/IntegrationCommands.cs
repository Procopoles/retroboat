using System;
using Retroboat.Integrations;
using Retroboat.Library;

namespace Retroboat.Cli
{
    /// <summary>
    /// retroboat-cli integrations ... (RB-037): o estado de cada integração e a troca automática de chave. O VERIFICAR AGORA da
    /// tela INTEGRAÇÕES do nosso ES chama o check; o ES lê o resultado em retroboat\estado\integracoes.xml.
    /// </summary>
    static class IntegrationCommands
    {
        public const string Usage =
@"Integrações (ScreenScraper, TheGamesDB, HfsDB, RetroAchievements, libretro, Wikidata, ArcadeDB, IGDB; RB-037):
  retroboat-cli integrations status --retrobat <pasta do RetroBat>   (a última verificação, sem ir à internet)
  retroboat-cli integrations check --retrobat <pasta do RetroBat> [--force]   (testa cada chave na API e troca sozinha a que parou, procurando no repositório do RetroBat)";

        public static int Status(RetroBatInstall install)
        {
            var last = IntegrationStatusFile.Read(install);
            if (last == null)
            {
                Console.WriteLine("Integrações ainda não verificadas. Rode: retroboat-cli integrations check --retrobat <pasta>");
                return 0;
            }

            Console.WriteLine("Verificadas em " + last.Value.CheckedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
            Print(last.Value.Items);
            return 0;
        }

        /// <returns>0 sempre que verificou (mesmo com integração com problema); o estado vai no arquivo e na saída.</returns>
        public static int Check(RetroBatInstall install, bool force)
        {
            var report = new IntegrationMonitor(install).Run(force);
            foreach (var line in report.Log)
                Console.WriteLine("  " + line);

            Print(report.Items);
            foreach (var message in report.Messages)
                Console.WriteLine("Aviso: " + message);

            return 0;
        }

        static void Print(System.Collections.Generic.IEnumerable<IntegrationStatus> items)
        {
            foreach (var item in items)
                Console.WriteLine(string.Format("{0,-22} {1,-16} {2}", item.Name, item.Text, item.Detail));
        }
    }
}
