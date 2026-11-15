using System;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Retroboat.Library;
using Retroboat.Media;
using Retroboat.Media.Sources;

namespace Retroboat.Cli
{
    /// <summary>
    /// retroboat-cli media ... (RB-036, RB-038): o PERSONALIZADO do nosso ES chama estes comandos. A busca devolve XML com os
    /// campos do gamelist do ES; as mídias vão como endereços, e quem baixa e grava é o ES.
    /// </summary>
    static class MediaCommands
    {
        public const string Usage =
@"Mídia de um jogo pelo PERSONALIZADO (o PROCURAR MÍDIAS do nosso ES usa estes comandos; RB-036, RB-038):
  retroboat-cli media search --retrobat <pasta do RetroBat> --system SISTEMA (--file ARQUIVO | --name NOME) [--profile PERFIL]
      ARQUIVO: o nome do arquivo do jogo (com extensão); NOME: o nome que a pessoa digitou. --game NOME vale como --file.
      PERFIL: as prioridades do ES (""thumbnail=hfsdb;video=none""); sem ele, o do es_settings.cfg. Saída em XML.
  retroboat-cli media systems [--retrobat <pasta do RetroBat>]   (os sistemas do ES que alguma fonte conhece, um por linha)
  retroboat-cli media fields   (campos e fontes do PERSONALIZADO, na ordem padrão, para a tela de prioridades do ES)";

        /// <returns>0 com resultado ou sem ele; 1 se nada foi achado porque as fontes não responderam.</returns>
        public static int Search(RetroBatInstall install, string system, string file, string typedName, string profileText)
        {
            var profile = profileText != null ? MediaProfile.Parse(profileText) : MediaProfile.ForInstall(install);
            var query = new MediaQuery(system, file ?? typedName, typedName);
            var result = CustomMediaSearch.ForInstall(install, profile).Find(query);

            var root = new XElement("media", new XAttribute("system", system), new XAttribute("name", query.SearchName));
            if (result.Found)
            {
                var game = new XElement("game");
                foreach (var field in MediaFields.Texts)
                {
                    if (result.Texts.TryGetValue(field.Id, out var text))
                        game.Add(new XElement(field.Id, text));
                }

                foreach (var field in MediaFields.Medias)
                {
                    if (!result.Media.TryGetValue(field.Id, out var media))
                        continue;

                    foreach (var gamelistField in field.GamelistFields)
                    {
                        var element = new XElement(gamelistField, media.Url);
                        if (media.Extension != null)
                            element.Add(new XAttribute("format", media.Extension));
                        game.Add(element);
                    }
                }

                foreach (var origin in result.Origins)
                    game.Add(new XElement("origin", new XAttribute("field", origin.Key), new XAttribute("source", origin.Value)));

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

        /// <param name="install">Com a instalação, só os sistemas de fontes com chave neste PC; sem ela, todos.</param>
        public static int Systems(RetroBatInstall install)
        {
            var systems = install != null
                ? CustomMediaSearch.ForInstall(install, MediaProfile.Default).Systems
                : LibretroThumbnails.Playlists.Keys.Concat(WikidataInfo.Platforms.Keys)
                    .Concat(new[] { MediaSourceIds.ScreenScraper, MediaSourceIds.HfsDb, MediaSourceIds.TheGamesDb }.SelectMany(MediaSystems.SystemsOf))
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList();

            foreach (var system in systems)
                Console.WriteLine(system);

            return 0;
        }

        /// <summary>
        /// Para a tela PRIORIDADE DAS FONTES do ES: "source|id|NOME" por fonte e "field|id|NOME|tipo|fonte1,fonte2,..." por campo
        /// (tipo: text, image ou video), com as fontes que têm o campo na ordem padrão (a primeira é a escolha padrão).
        /// </summary>
        public static int Fields()
        {
            foreach (var source in MediaSourceIds.Labels)
                Console.WriteLine("source|" + source.Key + "|" + source.Value);

            foreach (var field in MediaFields.All)
                Console.WriteLine("field|" + field.Id + "|" + field.Label + "|" + field.Kind.ToString().ToLowerInvariant() + "|" + string.Join(",", MediaProfile.Preset[field.Id]));

            return 0;
        }
    }
}
