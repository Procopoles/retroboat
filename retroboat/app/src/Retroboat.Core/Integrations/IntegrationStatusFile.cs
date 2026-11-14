using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Library;

namespace Retroboat.Integrations
{
    /// <summary>O estado de uma integração, como a tela INTEGRAÇÕES mostra.</summary>
    public sealed class IntegrationStatus
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Purpose { get; set; }
        public IntegrationState State { get; set; }

        /// <summary>O valor à direita da linha, curto: "Funcionando", "Chave recusada"...</summary>
        public string Text { get; set; }

        /// <summary>A linha de baixo quando há o que explicar (troca automática, motivo da falha); senão, vale o <see cref="Purpose"/>.</summary>
        public string Detail { get; set; }

        /// <summary>A chave foi trocada sozinha nesta verificação.</summary>
        public bool ReplacedNow { get; set; }
    }

    /// <summary>
    /// &lt;RetroBat&gt;\retroboat\estado\integracoes.xml: o estado de cada integração para o nosso ES (RB-037). O Retroboat grava
    /// a cada verificação; o ES só lê e mostra, com os textos prontos daqui (o português fica num lugar só).
    /// </summary>
    public static class IntegrationStatusFile
    {
        public static string GetPath(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, "estado", "integracoes.xml");

        public static void Write(RetroBatInstall install, IReadOnlyList<IntegrationStatus> items, DateTime checkedAt)
        {
            var (summary, overall) = Summarize(items);
            var document = new XDocument(new XElement("integracoes",
                new XAttribute("verificado", checkedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                new XAttribute("resumo", summary),
                new XAttribute("estado", StateName(overall)),
                items.Select(i => new XElement("integracao",
                    new XAttribute("id", i.Id),
                    new XAttribute("nome", i.Name),
                    new XAttribute("uso", i.Purpose ?? ""),
                    new XAttribute("tipo", KindName(IntegrationCatalog.Find(i.Id)?.Kind)),
                    new XAttribute("estado", StateName(i.State)),
                    new XAttribute("texto", i.Text ?? ""),
                    new XAttribute("detalhe", i.Detail ?? "")))));

            var path = GetPath(install);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".retroboat-part";
            document.Save(temp);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }

        /// <summary>Quando foi a última verificação e o estado de cada integração; nulo sem o arquivo.</summary>
        public static (DateTime CheckedAt, List<IntegrationStatus> Items)? Read(RetroBatInstall install)
        {
            var path = GetPath(install);
            if (!File.Exists(path))
                return null;

            try
            {
                var root = XDocument.Load(path).Root;
                var checkedAt = DateTime.Parse((string)root.Attribute("verificado"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                var items = root.Elements("integracao").Select(e => new IntegrationStatus
                {
                    Id = (string)e.Attribute("id"),
                    Name = (string)e.Attribute("nome"),
                    Purpose = (string)e.Attribute("uso"),
                    State = ParseState((string)e.Attribute("estado")),
                    Text = (string)e.Attribute("texto"),
                    Detail = (string)e.Attribute("detalhe")
                }).ToList();
                return (checkedAt, items);
            }
            catch (Exception ex) when (ex is IOException || ex is System.Xml.XmlException || ex is FormatException || ex is ArgumentNullException)
            {
                return null; // Gravado pela metade (outra verificação no mesmo instante): conta como sem verificação.
            }
        }

        /// <summary>A linha da tela NUVEM: "Todas funcionando", "1 com problema"...</summary>
        public static (string Text, IntegrationState Overall) Summarize(IReadOnlyList<IntegrationStatus> items)
        {
            var problems = items.Where(i => IsProblem(i.State)).ToList();
            if (problems.Count == 0)
                return items.Any(i => i.State == IntegrationState.Ok) ? ("Todas funcionando", IntegrationState.Ok) : ("Não verificadas", IntegrationState.Unknown);

            // Nenhuma respondeu: é a internet, não as chaves.
            if (problems.Count == items.Count(i => i.State != IntegrationState.NoAccount) && problems.All(i => i.State == IntegrationState.Unreachable))
                return ("Sem internet", IntegrationState.Unreachable);

            var worst = problems.Any(i => i.State == IntegrationState.KeyRejected) ? IntegrationState.KeyRejected : problems[0].State;
            return (problems.Count == 1 ? problems[0].Name + " com problema" : problems.Count + " com problema", worst);
        }

        static bool IsProblem(IntegrationState state) =>
            state == IntegrationState.KeyRejected || state == IntegrationState.LimitReached || state == IntegrationState.Unreachable;

        /// <summary>O grupo da tela INTEGRAÇÕES: CHAVES DO RETROBAT, FONTES PÚBLICAS ou SUA CONTA.</summary>
        static string KindName(IntegrationKind? kind) =>
            kind == IntegrationKind.BuiltInKey ? "chave" : kind == IntegrationKind.Account ? "conta" : "publica";

        public static string StateName(IntegrationState state)
        {
            switch (state)
            {
                case IntegrationState.Ok: return "ok";
                case IntegrationState.KeyRejected: return "recusada";
                case IntegrationState.LimitReached: return "limite";
                case IntegrationState.Unreachable: return "semresposta";
                case IntegrationState.NoAccount: return "semconta";
                default: return "naoverificada";
            }
        }

        public static IntegrationState ParseState(string name)
        {
            foreach (IntegrationState state in Enum.GetValues(typeof(IntegrationState)))
                if (StateName(state) == name)
                    return state;

            return IntegrationState.Unknown;
        }
    }
}
