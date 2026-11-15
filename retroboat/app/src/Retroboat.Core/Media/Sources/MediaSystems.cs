using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Retroboat.Media.Sources
{
    /// <summary>Sistema do ES → id da plataforma em cada fonte (MediaSystems.xml, recurso do Retroboat.Core.dll).</summary>
    public static class MediaSystems
    {
        const string ResourceName = "Retroboat.Media.Sources.MediaSystems.xml";

        static readonly Lazy<Dictionary<string, Dictionary<string, string[]>>> Table = new Lazy<Dictionary<string, Dictionary<string, string[]>>>(Load);

        /// <summary>Os ids do sistema na fonte (vazio se a fonte não tem o sistema).</summary>
        public static IReadOnlyList<string> Ids(string source, string system)
        {
            if (system != null && Table.Value.TryGetValue(system, out var sources) && sources.TryGetValue(source, out var ids))
                return ids;

            return new string[0];
        }

        /// <summary>Os sistemas do ES que a fonte conhece.</summary>
        public static IEnumerable<string> SystemsOf(string source) =>
            Table.Value.Where(s => s.Value.ContainsKey(source)).Select(s => s.Key);

        static Dictionary<string, Dictionary<string, string[]>> Load()
        {
            using (var stream = typeof(MediaSystems).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("Recurso " + ResourceName + " não encontrado no Retroboat.Core.dll.");

                var table = new Dictionary<string, Dictionary<string, string[]>>(StringComparer.OrdinalIgnoreCase);
                foreach (var system in XDocument.Load(stream).Root.Elements("system"))
                {
                    var sources = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                    foreach (var attribute in system.Attributes().Where(a => a.Name.LocalName != "name"))
                    {
                        var ids = attribute.Value.Split(',').Select(i => i.Trim()).Where(i => i.Length > 0).ToArray();
                        if (ids.Length > 0)
                            sources[attribute.Name.LocalName] = ids;
                    }

                    table[(string)system.Attribute("name")] = sources;
                }

                return table;
            }
        }
    }
}
