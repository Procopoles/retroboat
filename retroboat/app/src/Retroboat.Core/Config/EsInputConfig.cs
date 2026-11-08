using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Retroboat.Library;
using Retroboat.Saves;

namespace Retroboat.Config
{
    /// <summary>
    /// Os controles configurados no ES (es_input.cfg) na nuvem, um controle por entrada. O emulatorLauncher monta os
    /// controles de cada emulador a partir deste arquivo, então configurar um controle num PC vale nos outros.
    ///
    /// O ES do Windows reconhece o controle só pelo GUID (InputManager::writeDeviceConfig), então a entrada é
    /// "&lt;tipo&gt;|&lt;GUID&gt;" e o valor é o &lt;inputConfig&gt; inteiro. Qual controle está ligado em cada jogador
    /// (INPUT P1GUID...) depende do PC e fica de fora (EsSettingsRules).
    ///
    /// Só grava com o ES fechado: ele regrava o arquivo ao configurar um controle.
    /// </summary>
    public sealed class EsInputConfig : IConfigSource
    {
        public const string FileName = "es_input.cfg";

        readonly string _path;

        public EsInputConfig(RetroBatInstall install)
        {
            Root = install.UserEmulationStationPath;
            _path = Path.Combine(Root, FileName);
        }

        public string Name => "controles";
        public string DisplayName => "controles";
        public string Root { get; }
        public bool IsAvailable => Directory.Exists(Root);

        public ConfigSnapshot Read()
        {
            var snapshot = new ConfigSnapshot();
            if (!File.Exists(_path))
                return snapshot;

            snapshot.ExistingGroups.Add(FileName);
            foreach (var config in XDocument.Load(_path).Root.Elements("inputConfig"))
            {
                var id = IdOf(config);
                if (id != null && !snapshot.Values.ContainsKey(id))
                    snapshot.Values[id] = config.ToString(SaveOptions.DisableFormatting);
            }

            return snapshot;
        }

        public string GroupOf(string id) => FileName;

        public bool IsPortable(string id, string value)
        {
            if (value == null)
                return true;

            try
            {
                var element = XElement.Parse(value);
                return element.Name.LocalName == "inputConfig" && IdOf(element) == id;
            }
            catch (XmlException)
            {
                return false;
            }
        }

        public DateTime ChangedUtc(string id) => File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;

        public IEnumerable<string> FilesFor(IEnumerable<string> ids) => new[] { _path };

        public void Write(IReadOnlyDictionary<string, string> changes)
        {
            var document = File.Exists(_path) ? XDocument.Load(_path) : new XDocument(new XElement("inputList"));
            var root = document.Root;
            foreach (var change in changes)
            {
                var existing = root.Elements("inputConfig").Where(e => IdOf(e) == change.Key).ToList();
                var replacement = change.Value == null ? null : XElement.Parse(change.Value);
                if (replacement != null)
                {
                    if (existing.Count > 0)
                        existing[0].AddBeforeSelf(replacement);
                    else
                        root.Add(replacement);
                }

                foreach (var old in existing)
                    old.Remove();
            }

            var options = new XmlWriterSettings { Indent = true, IndentChars = "\t", Encoding = new UTF8Encoding(false) };
            using (var memory = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(memory, options))
                    document.Save(writer);

                SaveSync.WriteAtomically(_path, memory.ToArray());
            }
        }

        static string IdOf(XElement config)
        {
            var type = (string)config.Attribute("type");
            var guid = (string)config.Attribute("deviceGUID");
            return string.IsNullOrEmpty(type) || string.IsNullOrEmpty(guid) ? null : type + "|" + guid;
        }
    }
}
