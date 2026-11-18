using System;
using System.Collections.Generic;

namespace Retroboat.Media.Background
{
    /// <summary>
    /// Uma mídia secundária esperando a vez (RB-041): de onde vem (uma fonte da internet ou a pasta de mídia da nuvem), para
    /// onde vai no PC e em que campos do gamelist ela entra quando chegar.
    /// </summary>
    public sealed class BackgroundMediaItem
    {
        /// <summary>Nome do sistema no ES (a pasta do gamelist).</summary>
        public string System { get; set; }

        /// <summary>A pasta do sistema na pasta de jogos, para os caminhos "./" do gamelist.</summary>
        public string RomFolder { get; set; }

        /// <summary>O arquivo do jogo nela (o &lt;path&gt; do gamelist sem "./").</summary>
        public string Game { get; set; }

        /// <summary>Campos do gamelist que apontam para o arquivo (quase sempre um só).</summary>
        public IReadOnlyList<string> Fields { get; set; } = new string[0];

        /// <summary>Endereço na internet (fontes do PERSONALIZADO). Nulo quando vem da nuvem.</summary>
        public string Url { get; set; }

        /// <summary>Arquivo na pasta de mídia da nuvem (Google Drive). Nulo quando vem da internet.</summary>
        public string CloudFile { get; set; }

        /// <summary>Onde o arquivo fica neste PC.</summary>
        public string Target { get; set; }

        /// <summary>
        /// O valor do campo quando a mídia entrou na fila. Ao chegar, o campo só é trocado se ainda estiver assim, vazio ou
        /// apontando para um arquivo que não existe: o que a pessoa escolheu nesse meio-tempo (PROCURAR MÍDIAS) vale.
        /// </summary>
        public string Replaces { get; set; }

        public DateTime AddedUtc { get; set; }

        /// <summary>Tentativas que falharam (sem internet, fonte fora do ar, Drive fechado).</summary>
        public int Attempts { get; set; }

        /// <summary>Antes disso, não tenta de novo.</summary>
        public DateTime NextTryUtc { get; set; }

        public string Field => Fields.Count > 0 ? Fields[0] : "";

        /// <summary>Um item por campo de cada jogo: o mais novo substitui o anterior.</summary>
        public string Key => System + "/" + Game + "/" + Field;

        /// <summary>De onde vem, para escalonar: um download por vez de cada servidor, e o Drive como um servidor só.</summary>
        public string Lane
        {
            get
            {
                if (Url == null)
                    return "drive";

                return Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : Url;
            }
        }
    }
}
