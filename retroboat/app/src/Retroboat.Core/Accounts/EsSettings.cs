using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Retroboat.Accounts
{
    /// <summary>
    /// Lê e altera valores de texto do es_settings.cfg (&lt;string name="..." value="..." /&gt;). Só deve ser usado
    /// com o ES fechado: o ES regrava o arquivo inteiro quando sai.
    /// </summary>
    public sealed class EsSettings
    {
        readonly string _path;
        readonly XDocument _document;

        EsSettings(string path, XDocument document)
        {
            _path = path;
            _document = document;
        }

        public static EsSettings Load(string path)
        {
            var document = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("config"));
            return new EsSettings(path, document);
        }

        public string Get(string name) => (string)Find(name)?.Attribute("value");

        /// <summary>Grava o valor; vazio remove a chave, e o ES volta ao padrão dele.</summary>
        public void Set(string name, string value)
        {
            var element = Find(name);
            if (string.IsNullOrEmpty(value))
            {
                element?.Remove();
                return;
            }

            if (element == null)
                _document.Root.Add(new XElement("string", new XAttribute("name", name), new XAttribute("value", value)));
            else
                element.SetAttributeValue("value", value);
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temp = _path + ".retroboat-part";
            var options = new XmlWriterSettings { Indent = true, IndentChars = "\t", Encoding = new UTF8Encoding(false) };
            using (var writer = XmlWriter.Create(temp, options))
                _document.Save(writer);

            if (File.Exists(_path))
                File.Delete(_path);

            File.Move(temp, _path);
        }

        XElement Find(string name) =>
            _document.Root.Elements().FirstOrDefault(e => string.Equals((string)e.Attribute("name"), name, StringComparison.Ordinal));
    }
}
