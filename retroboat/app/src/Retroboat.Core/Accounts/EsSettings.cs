using System;
using System.Collections.Generic;
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

        /// <summary>Grava um valor booleano (&lt;bool name="..." value="..." /&gt;).</summary>
        public void SetBool(string name, bool value)
        {
            var text = value ? "true" : "false";
            var element = Find(name);
            if (element == null)
                _document.Root.Add(new XElement("bool", new XAttribute("name", name), new XAttribute("value", text)));
            else
                element.SetAttributeValue("value", text);
        }

        /// <summary>Tipos de valor do es_settings.cfg (nomes dos elementos).</summary>
        public static readonly string[] Types = { "bool", "int", "float", "string" };

        /// <summary>Todas as chaves, com o tipo (bool, int, float ou string) e o valor, na ordem do arquivo.</summary>
        public IEnumerable<(string Type, string Name, string Value)> Entries()
        {
            foreach (var element in _document.Root.Elements())
            {
                var name = (string)element.Attribute("name");
                if (!string.IsNullOrEmpty(name) && Types.Contains(element.Name.LocalName))
                    yield return (element.Name.LocalName, name, (string)element.Attribute("value") ?? "");
            }
        }

        /// <summary>Grava a chave com o tipo dado, trocando a que existir (mesmo de outro tipo).</summary>
        public void SetTyped(string type, string name, string value)
        {
            if (!Types.Contains(type))
                throw new ArgumentException("Tipo de valor desconhecido no es_settings.cfg: " + type, nameof(type));

            var existing = FindAll(name).ToList();
            if (existing.Count == 1 && existing[0].Name.LocalName == type)
            {
                existing[0].SetAttributeValue("value", value);
                return;
            }

            var element = new XElement(type, new XAttribute("name", name), new XAttribute("value", value));
            if (existing.Count > 0)
                existing[0].AddBeforeSelf(element);
            else
                _document.Root.Add(element);

            foreach (var old in existing)
                old.Remove();
        }

        /// <summary>Remove a chave (de qualquer tipo): o ES volta ao padrão dele.</summary>
        public void Remove(string name)
        {
            foreach (var element in FindAll(name).ToList())
                element.Remove();
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

        XElement Find(string name) => FindAll(name).FirstOrDefault();

        IEnumerable<XElement> FindAll(string name) =>
            _document.Root.Elements().Where(e => string.Equals((string)e.Attribute("name"), name, StringComparison.Ordinal));
    }
}
