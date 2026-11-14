using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Retroboat.Media;
using Xunit;

namespace Retroboat.Tests
{
    /// <summary>PROCURAR MÍDIAS do ES (RB-036): um jogo, com a mesma regra da importação automática.</summary>
    public class MediaSearchTests
    {
        const string Ps2 = "Sony - PlayStation 2";

        sealed class Thumbnails : IThumbnailSource
        {
            readonly Dictionary<ThumbnailKind, string[]> _names;
            public Exception Fail;

            public Thumbnails(string[] boxarts, string[] snaps = null, string[] titles = null)
            {
                _names = new Dictionary<ThumbnailKind, string[]>
                {
                    [ThumbnailKind.Boxart] = boxarts,
                    [ThumbnailKind.Snap] = snaps ?? new string[0],
                    [ThumbnailKind.Title] = titles ?? new string[0]
                };
            }

            public IReadOnlyCollection<string> List(string playlist, ThumbnailKind kind)
            {
                if (Fail != null)
                    throw Fail;

                return playlist == Ps2 ? _names[kind] : new string[0];
            }

            public byte[] Download(string playlist, ThumbnailKind kind, string name) => throw new InvalidOperationException("a busca não baixa: quem baixa é o ES");

            public string UrlOf(string playlist, ThumbnailKind kind, string name) => "https://libretro.test/" + kind + "/" + name + ".png";
        }

        sealed class Info : IGameInfoSource
        {
            readonly Dictionary<string, GameInfo> _games = new Dictionary<string, GameInfo>(StringComparer.OrdinalIgnoreCase);
            public Exception Fail;
            public List<string> Asked = new List<string>();

            public Info Add(string name, GameInfo info)
            {
                _games[name] = info;
                return this;
            }

            public IDictionary<string, GameInfo> Find(string system, IReadOnlyCollection<string> stems)
            {
                Asked.AddRange(stems);
                if (Fail != null)
                    throw Fail;

                return stems.Where(s => system == "ps2" && _games.ContainsKey(s)).ToDictionary(s => s, s => _games[s]);
            }
        }

        static readonly GameInfo Libretro = new GameInfo
        {
            Title = "God of War",
            Description = "God of War is an action-oriented adventure game.",
            Genre = "Ação",
            ReleaseDate = "20050322T000000",
            Developer = "SCE Santa Monica",
            Publisher = "Sony Computer Entertainment",
            Players = "1",
            Origin = GameInfoOrigin.Libretro
        };

        static readonly GameInfo Wikipedia = new GameInfo
        {
            Title = "God of War",
            Description = "God of War é um jogo eletrônico de ação-aventura desenvolvido pela Santa Monica Studio e publicado pela " +
                          "Sony Computer Entertainment. Foi lançado para PlayStation 2 em março de 2005.",
            DescriptionInPortuguese = true,
            Genre = "Ação-aventura",
            Developer = "Santa Monica Studio",
            Origin = GameInfoOrigin.Wikidata
        };

        static Thumbnails GodOfWarImages() => new Thumbnails(
            new[] { "God of War (Europe, Australia)", "God of War (USA)", "God of War II (USA)" },
            new[] { "God of War (USA)" });

        [Fact]
        public void Finds_images_and_info_for_the_file_with_the_import_rule()
        {
            var search = new MediaSearch(GodOfWarImages(), new IGameInfoSource[]
            {
                new Info().Add("God of War (USA)", Libretro),
                new Info().Add("God of War (USA)", Wikipedia)
            });

            var result = search.Find("ps2", "God of War (USA)");

            Assert.True(result.Found);
            Assert.Empty(result.Errors);
            Assert.Equal("https://libretro.test/Boxart/God of War (USA).png", result.Images[ThumbnailKind.Boxart]);
            Assert.Equal("https://libretro.test/Snap/God of War (USA).png", result.Images[ThumbnailKind.Snap]);
            Assert.False(result.Images.ContainsKey(ThumbnailKind.Title));

            // Cada campo pela fonte que mais acertou nele, como no AutoInfo.
            Assert.Equal(Wikipedia.Description, result.Info.Description);
            Assert.Equal("Ação-aventura", result.Info.Genre);
            Assert.Equal("Santa Monica Studio", result.Info.Developer);
            Assert.Equal("Sony Computer Entertainment", result.Info.Publisher);
            Assert.Equal("20050322T000000", result.Info.ReleaseDate);
            Assert.Equal("1", result.Info.Players);
        }

        [Fact]
        public void A_typed_name_finds_the_game_like_the_import_does()
        {
            var info = new Info().Add("God of War", Libretro);
            var result = new MediaSearch(GodOfWarImages(), new IGameInfoSource[] { info }).Find("ps2", "  God of War ");

            // Sem região no nome: a dos EUA, como a importação escolhe.
            Assert.Equal("https://libretro.test/Boxart/God of War (USA).png", result.Images[ThumbnailKind.Boxart]);
            Assert.Equal("God of War", info.Asked.Single());
            Assert.Equal("God of War", result.Info.Title);
        }

        [Fact]
        public void An_unknown_game_is_not_found_and_is_not_an_error()
        {
            var result = new MediaSearch(GodOfWarImages(), new IGameInfoSource[] { new Info() }).Find("ps2", "Jogo Que Não Existe (USA)");

            Assert.False(result.Found);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void A_source_that_is_down_does_not_hide_the_others()
        {
            var images = GodOfWarImages();
            images.Fail = new WebException("sem conexão");
            var down = new Info { Fail = new WebException("Wikidata fora do ar") };

            var result = new MediaSearch(images, new IGameInfoSource[] { new Info().Add("God of War (USA)", Libretro), down }).Find("ps2", "God of War (USA)");

            Assert.True(result.Found);
            Assert.Equal(2, result.Errors.Count);
            Assert.Empty(result.Images);
            Assert.Equal("Sony Computer Entertainment", result.Info.Publisher);
        }

        [Fact]
        public void A_system_without_sources_finds_nothing_and_is_not_an_error()
        {
            var result = new MediaSearch(GodOfWarImages(), new IGameInfoSource[] { new Info() }).Find("sistema-novo", "Jogo (USA)");

            Assert.False(result.Found);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void Image_fields_follow_the_import_and_use_the_cover_without_a_game_screen()
        {
            var coverOnly = MediaRule.ImageFields(new Dictionary<ThumbnailKind, string> { [ThumbnailKind.Boxart] = "capa" });
            Assert.Equal(new[] { "thumbnail", "boxart", "image" }, coverOnly.Keys.ToArray());
            Assert.All(coverOnly.Values, v => Assert.Equal("capa", v));

            var all = MediaRule.ImageFields(new Dictionary<ThumbnailKind, string>
            {
                [ThumbnailKind.Boxart] = "capa",
                [ThumbnailKind.Snap] = "tela",
                [ThumbnailKind.Title] = "titulo"
            });
            Assert.Equal("capa", all["thumbnail"]);
            Assert.Equal("tela", all["image"]);
            Assert.Equal("titulo", all["titleshot"]);
        }
    }
}
