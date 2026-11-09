using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Retroboat.Cloud
{
    /// <summary>
    /// RB-021: identidade da biblioteca do Retroboat no Drive, em retroboat\biblioteca.xml. O arquivo é criado uma vez e
    /// nunca reescrito; cada PC lembra o id ([Cloud] Library no retroboat.ini) e só usa a pasta que tem esse id, em
    /// qualquer letra de unidade e em qualquer conta. Assim uma pasta "retroboat" de outra conta nunca é tomada pela
    /// biblioteca deste PC.
    /// </summary>
    public sealed class LibraryIdentity
    {
        public const string FileName = "biblioteca.xml";

        // A cópia que o Drive cria num conflito ("biblioteca (1).xml") também vale.
        const string Pattern = "biblioteca*.xml";
        const string Version = "1";

        LibraryIdentity(string id, DateTime createdUtc, string pc, string path)
        {
            Id = id;
            CreatedUtc = createdUtc;
            Pc = pc;
            FilePath = path;
        }

        public string Id { get; }
        public DateTime CreatedUtc { get; }

        /// <summary>PC que criou o arquivo.</summary>
        public string Pc { get; }

        public string FilePath { get; }

        /// <summary>Identidades válidas na pasta, a de nome exato primeiro. Arquivos danificados ficam de fora.</summary>
        public static List<LibraryIdentity> ReadAll(string retroboatPath)
        {
            if (!Directory.Exists(retroboatPath))
                return new List<LibraryIdentity>();

            return Directory.GetFiles(retroboatPath, Pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(f => string.Equals(Path.GetFileName(f), FileName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(TryRead)
                .Where(identity => identity != null)
                .ToList();
        }

        public static bool Has(string retroboatPath, string id)
        {
            return !string.IsNullOrEmpty(id) && ReadAll(retroboatPath).Any(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// A identidade da pasta: a que já existe ou, se não há nenhuma, uma nova. A nova é gravada num arquivo temporário e
        /// só então ganha o nome final, com um mover que falha se o nome já existe: nunca substitui um biblioteca.xml.
        /// </summary>
        /// <returns>null se já existe um biblioteca.xml, mas danificado (ele fica como está).</returns>
        public static LibraryIdentity Adopt(string retroboatPath, string pcName = null, Func<DateTime> utcNow = null)
        {
            var existing = ReadAll(retroboatPath).FirstOrDefault();
            if (existing != null)
                return existing;

            var target = Path.Combine(retroboatPath, FileName);
            if (File.Exists(target))
                return null;

            var created = (utcNow ?? (() => DateTime.UtcNow))();
            var pc = string.IsNullOrWhiteSpace(pcName) ? Environment.MachineName : pcName;
            var id = Guid.NewGuid().ToString("D");
            var temp = Path.Combine(retroboatPath, "." + Path.GetFileNameWithoutExtension(FileName) + "-" + Guid.NewGuid().ToString("N") + ".tmp");

            var doc = new XDocument(new XElement("biblioteca",
                new XAttribute("id", id),
                new XAttribute("criada", created.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                new XAttribute("pc", pc),
                new XAttribute("versao", Version)));

            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
                doc.Save(writer);

            try
            {
                File.Move(temp, target);
            }
            catch (IOException)
            {
                // Outro PC (ou outra abertura) criou no mesmo instante: vale o que está lá.
                TryDelete(temp);
                return ReadAll(retroboatPath).FirstOrDefault();
            }

            return TryRead(target);
        }

        static LibraryIdentity TryRead(string path)
        {
            try
            {
                var root = XDocument.Load(path).Root;
                if (root == null || root.Name != "biblioteca")
                    return null;

                var id = ((string)root.Attribute("id") ?? "").Trim();
                if (!Guid.TryParse(id, out _))
                    return null;

                DateTime.TryParse((string)root.Attribute("criada"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var created);
                return new LibraryIdentity(id, created, (string)root.Attribute("pc"), path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException)
            {
                return null;
            }
        }

        static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
