using System;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Retroboat.Library;
using Retroboat.Media;

namespace Retroboat.Cli
{
    /// <summary>
    /// retroboat-cli media ... (RB-036): o PROCURAR MÍDIAS do nosso ES chama este comando para buscar um jogo com a regra
    /// da importação automática. A saída é XML com os campos do gamelist do ES; as imagens vão como endereços, e quem
    /// baixa e grava é o ES.
    /// </summary>
    static class MediaCommands
    {
        public const string Usage =
@"Mídia de um jogo (o PROCURAR MÍDIAS do nosso ES usa este comando; RB-036):
  retroboat-cli media search --retrobat <pasta do RetroBat> --system SISTEMA --game NOME   (NOME: o arquivo sem extensão ou o nome do jogo; saída em XML)
  retroboat-cli media systems   (os sistemas do ES que as fontes conhecem, um por linha)";

        /// <returns>0 com resultado ou sem ele; 1 se nada foi achado porque as fontes não responderam.</returns>
        public static int Search(RetroBatInstall install, string system, string name)
        {
            var result = MediaSearch.ForInstall(install).Find(system, name);

            var root = new XElement("media", new XAttribute("system", system), new XAttribute("name", name));
            if (result.Found)
            {
                var game = new XElement("game");
                var info = result.Info;
                if (info != null)
                {
                    Add(game, "name", info.Title);
                    Add(game, "desc", info.Description);
                    Add(game, "genre", info.Genre);
                    Add(game, "developer", info.Developer);
                    Add(game, "publisher", info.Publisher);
                    Add(game, "releasedate", info.ReleaseDate);
                    Add(game, "players", info.Players);
                }

                foreach (var field in MediaRule.ImageFields(result.Images))
                    Add(game, field.Key, field.Value);

                root.Add(game);
            }

            foreach (var error in result.Errors)
                root.Add(new XElement("error", error));

            var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, OmitXmlDeclaration = true };
            using (var writer = XmlWriter.Create(Console.Out, settings))
                root.WriteTo(writer);

            Console.WriteLine();
            return !result.Found && result.Errors.Count > 0 ? 1 : 0;
        }

        public static int Systems()
        {
            foreach (var system in MediaSearch.Systems)
                Console.WriteLine(system);

            return 0;
        }

        static void Add(XElement game, string field, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                game.Add(new XElement(field, value));
        }
    }
}
