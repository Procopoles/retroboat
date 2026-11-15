using System;
using System.Collections.Generic;
using System.Linq;

namespace Retroboat.Media
{
    public enum MediaFieldKind
    {
        /// <summary>Texto do gamelist (nome, descrição, gênero...).</summary>
        Text,

        /// <summary>Imagem baixada para a pasta images do sistema.</summary>
        Image,

        /// <summary>Vídeo baixado para a pasta videos do sistema.</summary>
        Video,

        /// <summary>Manual em PDF, baixado para a pasta manuals do sistema.</summary>
        Document
    }

    /// <summary>Um campo que o PERSONALIZADO busca, com o nome que aparece no ES e os campos do gamelist que ele preenche.</summary>
    public sealed class MediaField
    {
        public MediaField(string id, string label, MediaFieldKind kind, string suffix, params string[] gamelistFields)
        {
            Id = id;
            Label = label;
            Kind = kind;
            Suffix = suffix;
            GamelistFields = gamelistFields.Length > 0 ? gamelistFields : new[] { id };
        }

        /// <summary>Identificador estável: a chave no perfil (es_settings) e o campo principal do gamelist.</summary>
        public string Id { get; }

        /// <summary>Como aparece na tela de prioridades do ES.</summary>
        public string Label { get; }

        public MediaFieldKind Kind { get; }

        /// <summary>Sufixo do arquivo baixado, como o scraper do ES faz ("Jogo-thumb.png"). Nulo nos campos de texto.</summary>
        public string Suffix { get; }

        /// <summary>Campos do gamelist que o valor preenche (a capa vai em thumbnail e boxart).</summary>
        public IReadOnlyList<string> GamelistFields { get; }

        public bool IsMedia => Kind != MediaFieldKind.Text;
    }

    /// <summary>
    /// Os campos do PERSONALIZADO (RB-038), na ordem da tela de prioridades do ES. Cada um tem a sua lista de fontes, do
    /// <see cref="MediaProfile"/>.
    /// </summary>
    public static class MediaFields
    {
        public const string Name = "name";
        public const string Description = "desc";
        public const string Rating = "rating";
        public const string Genre = "genre";
        public const string Family = "family";
        public const string Developer = "developer";
        public const string Publisher = "publisher";
        public const string ReleaseDate = "releasedate";
        public const string Players = "players";
        public const string Cover = "thumbnail";
        public const string BoxBack = "boxback";
        public const string Screenshot = "image";
        public const string TitleScreen = "titleshot";
        public const string Logo = "marquee";
        public const string FanArt = "fanart";
        public const string Cartridge = "cartridge";
        public const string Video = "video";
        public const string Manual = "manual";
        public const string Map = "map";
        public const string Bezel = "bezel";

        /// <summary>Todos os campos do gamelist do ES que um scraper preenche e que alguma fonte tem.</summary>
        public static readonly IReadOnlyList<MediaField> All = new[]
        {
            new MediaField(Cover, "CAPA", MediaFieldKind.Image, "thumb", "thumbnail", "boxart"),
            new MediaField(BoxBack, "CAIXA TRASEIRA", MediaFieldKind.Image, "boxback"),
            new MediaField(Screenshot, "TELA DO JOGO", MediaFieldKind.Image, "image"),
            new MediaField(TitleScreen, "TELA DE TÍTULO", MediaFieldKind.Image, "titleshot"),
            new MediaField(Logo, "LOGO", MediaFieldKind.Image, "marquee"),
            new MediaField(FanArt, "FAN ART", MediaFieldKind.Image, "fanart"),
            new MediaField(Cartridge, "DISCO / CARTUCHO", MediaFieldKind.Image, "cartridge"),
            new MediaField(Video, "VÍDEO", MediaFieldKind.Video, "video"),
            new MediaField(Manual, "MANUAL", MediaFieldKind.Document, "manual"),
            new MediaField(Map, "MAPA", MediaFieldKind.Image, "map"),
            new MediaField(Bezel, "MOLDURA (16:9)", MediaFieldKind.Image, "bezel"),
            new MediaField(Name, "NOME", MediaFieldKind.Text, null),
            new MediaField(Description, "DESCRIÇÃO", MediaFieldKind.Text, null),
            new MediaField(Rating, "AVALIAÇÃO", MediaFieldKind.Text, null),
            new MediaField(Genre, "GÊNERO", MediaFieldKind.Text, null),
            new MediaField(Family, "SÉRIE", MediaFieldKind.Text, null),
            new MediaField(Developer, "DESENVOLVEDORA", MediaFieldKind.Text, null),
            new MediaField(Publisher, "EDITORA", MediaFieldKind.Text, null),
            new MediaField(ReleaseDate, "LANÇAMENTO", MediaFieldKind.Text, null),
            new MediaField(Players, "JOGADORES", MediaFieldKind.Text, null)
        };

        public static MediaField Find(string id) => All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

        public static IEnumerable<MediaField> Texts => All.Where(f => !f.IsMedia);

        public static IEnumerable<MediaField> Medias => All.Where(f => f.IsMedia);
    }
}
