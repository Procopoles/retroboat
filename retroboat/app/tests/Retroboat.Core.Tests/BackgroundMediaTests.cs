using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Es;
using Retroboat.Library;
using Retroboat.Media;
using Retroboat.Media.Background;
using Retroboat.Media.Sources;
using Xunit;

namespace Retroboat.Tests
{
    /// <summary>
    /// RB-041: a atualização da biblioteca só espera o essencial (textos, capa, tela, logo, fan art); vídeo, manual e imagens
    /// extras chegam em segundo plano e aparecem no ES sozinhos. Vale para os jogos novos (fontes da internet) e para a
    /// primeira abertura num PC sem nenhuma mídia (cópia do Drive).
    /// </summary>
    public class BackgroundMediaTests
    {
        const string Game = "God Hand (USA).iso";
        const string Stem = "God Hand (USA)";

        static string Gamelist(RetroBatInstall install) => Path.Combine(install.GamelistsPath, "ps2", "gamelist.xml");

        static XElement Find(RetroBatInstall install) => CloudLibrarySync.FindGame(XDocument.Load(Gamelist(install)), Game);

        static void Edit(RetroBatInstall install, Action<XElement> change)
        {
            var gamelist = XDocument.Load(Gamelist(install));
            change(CloudLibrarySync.FindGame(gamelist, Game));
            gamelist.Save(Gamelist(install));
        }

        static RetroBatInstall Pc(TempDir pcs, string name, TempDir cloud)
        {
            var install = new RetroBatInstall(pcs.Combine(name));
            new CloudLibrarySync(install, install.DefaultRomsPath, new CloudLibrary(cloud.Combine("jogos"))).Apply();
            return install;
        }

        static string Media(RetroBatInstall install, string folder, string file) => Path.Combine(install.UserEmulationStationPath, "media", "ps2", folder, file);

        /// <summary>O HfsDB com capa, vídeo e manual; o ScreenScraper com tela, tela de título, logo e textos.</summary>
        static (FakeMediaSource Hfs, FakeMediaSource Ss) Sources()
        {
            var hfs = new FakeMediaSource(MediaSourceIds.HfsDb)
                .Media(Stem, MediaFields.Cover, ".jpg").Media(Stem, MediaFields.Video, ".mp4").Media(Stem, MediaFields.Manual, ".pdf");
            var ss = new FakeMediaSource(MediaSourceIds.ScreenScraper)
                .Media(Stem, MediaFields.Screenshot).Media(Stem, MediaFields.TitleScreen).Media(Stem, MediaFields.Logo)
                .Text(Stem, MediaFields.Name, "God Hand").Text(Stem, MediaFields.Genre, "Ação");
            return (hfs, ss);
        }

        static BackgroundMediaWorker Worker(RetroBatInstall install, FakeMediaHttp http, IEsGames es = null, Func<bool> esOpen = null, Func<string> pause = null) =>
            new BackgroundMediaWorker(install, new BackgroundMediaQueue(install), new BackgroundMediaFetcher(http), es ?? new FakeEsGames(), pause ?? (() => null), esOpen ?? (() => false))
            {
                SettleEvery = TimeSpan.FromMilliseconds(20),
                WaitAfterSync = TimeSpan.Zero
            };

        [Fact]
        public void A_new_game_waits_only_for_the_essentials_and_the_video_comes_in_the_background()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var install = Pc(pcs, "A", cloud);
                var (hfs, ss) = Sources();
                var http = new FakeMediaHttp();
                var queue = new BackgroundMediaQueue(install);

                var result = new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(hfs, ss), http: http, background: queue).Apply();

                // A espera: textos, capa, tela e logo.
                var game = Find(install);
                Assert.Equal("God Hand", (string)game.Element("name"));
                Assert.NotNull(game.Element("thumbnail"));
                Assert.NotNull(game.Element("image"));
                Assert.NotNull(game.Element("marquee"));
                Assert.Null(game.Element("video"));
                Assert.Null(game.Element("manual"));
                Assert.Null(game.Element("titleshot"));
                Assert.Equal(3, result.FilesDownloaded);
                Assert.Equal(3, result.FilesQueued);
                Assert.DoesNotContain(http.Downloads, d => d.Contains("/video/") || d.Contains("/manual/") || d.Contains("/titleshot/"));

                // Na fila: a imagem extra primeiro, depois o vídeo e por último o manual.
                Assert.Equal(new[] { "titleshot", "video", "manual" }, queue.Items().Select(i => i.Field));
                Assert.True(File.Exists(BackgroundMediaQueue.PathFor(install)));

                // O jogo já conta como buscado: a próxima abertura não volta às fontes.
                int calls = hfs.Calls;
                new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(hfs, ss), http: http, background: queue).Apply();
                Assert.Equal(calls, hfs.Calls);

                // Em segundo plano.
                var run = Worker(install, http).Run();

                Assert.Equal(3, run.Applied);
                Assert.True(run.FromInternet);
                game = Find(install);
                Assert.Equal("~/.emulationstation/media/ps2/videos/God Hand (USA)-video.mp4", (string)game.Element("video"));
                Assert.Equal("~/.emulationstation/media/ps2/manuals/God Hand (USA)-manual.pdf", (string)game.Element("manual"));
                Assert.Equal("~/.emulationstation/media/ps2/images/God Hand (USA)-titleshot.png", (string)game.Element("titleshot"));
                Assert.True(File.Exists(Media(install, "videos", Stem + "-video.mp4")));
                Assert.False(File.Exists(Media(install, "videos", Stem + "-video.mp4" + BackgroundMediaFetcher.PartSuffix)));
                Assert.True(queue.IsEmpty);
                Assert.False(File.Exists(BackgroundMediaQueue.PathFor(install))); // fila vazia: o script de início do ES não abre nada
            }
        }

        [Fact]
        public void The_first_open_on_a_pc_without_media_copies_only_the_essentials_and_queues_the_rest()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var all = new CloudMediaOptions { Videos = true, Manuals = true };

                // PC A tem tudo e envia para a nuvem.
                var a = Pc(pcs, "A", cloud);
                var (hfs, ss) = Sources();
                new AutoImport(a, a.DefaultRomsPath, MediaTest.Search(hfs, ss), http: new FakeMediaHttp()).Apply();
                new CloudMediaSync(a, a.DefaultRomsPath, cloud.Path, "PC-A", all).Apply();
                Assert.True(File.Exists(cloud.Combine(@"midia\ps2\videos\God Hand (USA)-video.mp4")));

                // PC B, primeira abertura: nenhuma mídia.
                var b = Pc(pcs, "B", cloud);
                var queue = new BackgroundMediaQueue(b);
                var progress = new List<string>();

                var result = new CloudMediaSync(b, b.DefaultRomsPath, cloud.Path, "PC-B", all, queue).Apply((done, total) => progress.Add(done + "/" + total));

                var game = Find(b);
                Assert.Equal("God Hand", (string)game.Element("name"));
                Assert.True(File.Exists(Media(b, "images", Stem + "-thumb.jpg")));
                Assert.NotNull(game.Element("thumbnail"));
                Assert.NotNull(game.Element("boxart"));
                Assert.NotNull(game.Element("image"));
                Assert.NotNull(game.Element("marquee"));
                Assert.Null(game.Element("video"));
                Assert.Null(game.Element("manual"));
                Assert.Null(game.Element("titleshot"));
                Assert.False(File.Exists(Media(b, "videos", Stem + "-video.mp4")));
                Assert.Equal(3, result.FilesDownloaded); // a capa é um arquivo só para thumbnail e boxart
                Assert.Equal(3, result.FilesQueued);
                Assert.Equal("3/3", progress.Last());
                Assert.All(queue.Items(), i => Assert.NotNull(i.CloudFile));

                var run = Worker(b, new FakeMediaHttp()).Run();

                Assert.Equal(3, run.Applied);
                Assert.False(run.FromInternet); // já está na nuvem
                game = Find(b);
                Assert.Equal("~/.emulationstation/media/ps2/videos/God Hand (USA)-video.mp4", (string)game.Element("video"));
                Assert.NotNull(game.Element("manual"));
                Assert.NotNull(game.Element("titleshot"));
                Assert.Equal(new FileInfo(cloud.Combine(@"midia\ps2\videos\God Hand (USA)-video.mp4")).Length, new FileInfo(Media(b, "videos", Stem + "-video.mp4")).Length);

                // A próxima sincronização vê tudo em dia: nada de novo na fila, nada enviado de volta.
                var again = new CloudMediaSync(b, b.DefaultRomsPath, cloud.Path, "PC-B", all, queue).Apply();
                Assert.Equal(0, again.FilesQueued);
                Assert.Equal(0, again.FilesDownloaded);
                Assert.True(queue.IsEmpty);
            }
        }

        [Fact]
        public void Videos_turned_off_for_the_cloud_are_not_queued()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var a = Pc(pcs, "A", cloud);
                var (hfs, ss) = Sources();
                new AutoImport(a, a.DefaultRomsPath, MediaTest.Search(hfs, ss), http: new FakeMediaHttp()).Apply();
                new CloudMediaSync(a, a.DefaultRomsPath, cloud.Path, "PC-A").Apply();

                var b = Pc(pcs, "B", cloud);
                var queue = new BackgroundMediaQueue(b);
                new CloudMediaSync(b, b.DefaultRomsPath, cloud.Path, "PC-B", new CloudMediaOptions { Manuals = true }, queue).Apply();

                Assert.Equal(new[] { "titleshot", "manual" }, queue.Items().Select(i => i.Field));
            }
        }

        [Fact]
        public void What_the_person_chose_meanwhile_wins_and_the_unused_file_is_deleted()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var install = Pc(pcs, "A", cloud);
                var (hfs, ss) = Sources();
                var http = new FakeMediaHttp();
                new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(hfs, ss), http: http, background: new BackgroundMediaQueue(install)).Apply();

                // PROCURAR MÍDIAS no ES antes de o vídeo chegar.
                var chosen = pcs.WriteFile(@"A\emulationstation\.emulationstation\media\ps2\videos\escolhido.mp4", new byte[] { 5 });
                Edit(install, g => g.SetElementValue("video", "~/.emulationstation/media/ps2/videos/escolhido.mp4"));

                Worker(install, http).Run();

                Assert.Equal("~/.emulationstation/media/ps2/videos/escolhido.mp4", (string)Find(install).Element("video"));
                Assert.True(File.Exists(chosen));
                Assert.False(File.Exists(Media(install, "videos", Stem + "-video.mp4")));
                Assert.NotNull(Find(install).Element("manual")); // o resto entra normalmente
            }
        }

        [Fact]
        public void A_file_that_no_longer_exists_at_the_source_leaves_the_queue()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var install = Pc(pcs, "A", cloud);
                var (hfs, ss) = Sources();
                var http = new FakeMediaHttp();
                http.Missing.Add(hfs.UrlOf(MediaFields.Video, Stem, ".mp4"));
                new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(hfs, ss), http: http, background: new BackgroundMediaQueue(install)).Apply();

                var run = Worker(install, http).Run();

                Assert.Equal(1, run.Gone);
                Assert.Null(Find(install).Element("video"));
                Assert.True(new BackgroundMediaQueue(install).IsEmpty);
            }
        }

        [Fact]
        public void Without_internet_the_media_waits_longer_each_time_and_finally_gives_up()
        {
            using (var dir = new TempDir())
            {
                var install = new RetroBatInstall(dir.Combine("RetroBat"));
                var queue = new BackgroundMediaQueue(install);
                var item = new BackgroundMediaItem { System = "ps2", Game = Game, Fields = new[] { "video" }, Url = "https://hfsdb.test/v.mp4", Target = dir.Combine("v.mp4") };

                Assert.Equal(1, queue.Add(new[] { item }));
                Assert.Equal(0, queue.Add(new[] { item })); // a mesma mídia não entra duas vezes
                var start = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

                Assert.True(queue.Postpone(item, start));
                Assert.Equal(start.AddMinutes(1), queue.Items().Single().NextTryUtc);
                Assert.True(queue.Postpone(item, start));
                Assert.Equal(start.AddMinutes(5), queue.Items().Single().NextTryUtc);
                for (int i = 2; i < BackgroundMediaQueue.MaxAttempts - 1; i++)
                    Assert.True(queue.Postpone(item, start));

                Assert.False(queue.Postpone(item, start));
                Assert.True(queue.IsEmpty);
            }
        }

        [Fact]
        public void A_newer_source_for_the_same_field_replaces_the_one_in_the_queue()
        {
            using (var dir = new TempDir())
            {
                var install = new RetroBatInstall(dir.Combine("RetroBat"));
                var queue = new BackgroundMediaQueue(install);
                var old = new BackgroundMediaItem { System = "ps2", Game = Game, Fields = new[] { "video" }, Url = "https://a.test/v.mp4", Target = dir.Combine("v.mp4") };
                var newer = new BackgroundMediaItem { System = "ps2", Game = Game, Fields = new[] { "video" }, Url = "https://b.test/v.mp4", Target = dir.Combine("v.mp4") };

                queue.Add(new[] { old });
                queue.Add(new[] { newer });
                queue.Complete(old); // o download antigo terminou depois: não tira o novo

                Assert.Equal("https://b.test/v.mp4", queue.Items().Single().Url);
            }
        }

        [Fact]
        public void Nothing_downloads_while_the_person_waits_for_a_sync_a_game_downloads_or_a_game_is_open()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var install = Pc(pcs, "A", cloud);
                var (hfs, ss) = Sources();
                var http = new FakeMediaHttp();
                new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(hfs, ss), http: http, background: new BackgroundMediaQueue(install)).Apply();
                int before = http.Downloads.Count;

                var reasons = new Queue<string>(new[] { "jogo aberto", "jogo aberto", "download de jogo" });
                var downloadsWhilePaused = new List<int>();
                var run = Worker(install, http, pause: () =>
                {
                    lock (reasons)
                    {
                        if (reasons.Count == 0)
                            return null;

                        downloadsWhilePaused.Add(http.Downloads.Count);
                        return reasons.Dequeue();
                    }
                }).Run();

                Assert.All(downloadsWhilePaused, count => Assert.Equal(before, count));
                Assert.Equal(3, run.Applied);
            }
        }

        [Fact]
        public void With_the_es_open_the_video_appears_without_touching_what_the_es_has_in_memory()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var install = Pc(pcs, "A", cloud);
                var (hfs, ss) = Sources();
                var http = new FakeMediaHttp();
                new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(hfs, ss), http: http, background: new BackgroundMediaQueue(install)).Apply();
                Edit(install, g => g.Add(new XElement("scrap", new XAttribute("name", "Retroboat"), new XAttribute("date", "20261117T100000"))));

                var es = new FakeEsGames();
                es.Add("ps2", Path.Combine(install.DefaultRomsPath, "ps2", Game).Replace('\\', '/'), new Dictionary<string, string> { ["playcount"] = "3" });

                Worker(install, http, es, esOpen: () => true).Run();

                // Um POST por leva que chegou (o fake baixa tudo de uma vez, então quase sempre um só).
                Assert.All(es.Posted, p => Assert.Equal("ps2", p.System));
                var sent = es.Posted.SelectMany(p => XElement.Parse(p.Xml).Elements("game")).ToList();
                Assert.All(sent, n => Assert.Equal("./" + Game, (string)n.Element("path")));
                Assert.Contains(sent, n => (string)n.Element("video") == "~/.emulationstation/media/ps2/videos/God Hand (USA)-video.mp4");
                Assert.Contains(sent, n => n.Element("manual") != null);
                Assert.Contains(sent, n => n.Element("titleshot") != null);
                Assert.All(sent, n => Assert.NotNull(n.Element("scrap"))); // senão o ES esquece o PROCURAR MÍDIAS
                Assert.All(sent, n => Assert.Null(n.Element("playcount"))); // o que o ES tem na memória fica
                Assert.All(sent, n => Assert.Null(n.Element("name")));
                Assert.NotNull(Find(install).Element("video")); // e no gamelist também
            }
        }

        [Fact]
        public void A_video_the_person_chose_in_this_es_session_is_not_replaced_in_the_es()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var install = Pc(pcs, "A", cloud);
                var (hfs, ss) = Sources();
                var http = new FakeMediaHttp();
                new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(hfs, ss), http: http, background: new BackgroundMediaQueue(install)).Apply();

                // PROCURAR MÍDIAS nesta sessão: o ES tem o vídeo na memória; o gamelist só recebe ao sair.
                var chosen = pcs.WriteFile(@"A\roms\ps2\videos\escolhido.mp4", new byte[] { 5 });
                var es = new FakeEsGames();
                es.Add("ps2", Path.Combine(install.DefaultRomsPath, "ps2", Game), new Dictionary<string, string> { ["video"] = chosen });

                Worker(install, http, es, esOpen: () => true).Run();

                var sent = es.Posted.SelectMany(p => XElement.Parse(p.Xml).Elements("game")).ToList();
                Assert.All(sent, n => Assert.Null(n.Element("video")));
                Assert.Contains(sent, n => n.Element("manual") != null);
            }
        }

        [Fact]
        public void If_the_es_closes_before_receiving_the_media_goes_back_into_the_gamelist()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var install = Pc(pcs, "A", cloud);
                var (hfs, ss) = Sources();
                var http = new FakeMediaHttp();
                new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(hfs, ss), http: http, background: new BackgroundMediaQueue(install)).Apply();

                // Um jogo aberto no ES (ocupado) e depois o ES fecha, regravando o gamelist com o que tinha na memória.
                bool askedIdle = false, closed = false;
                var es = new FakeEsGames { Idle = () => { askedIdle = true; return false; } };
                bool EsOpen()
                {
                    if (askedIdle && !closed)
                    {
                        closed = true;
                        Edit(install, g => g.Element("video")?.Remove());
                    }

                    return !closed;
                }

                Worker(install, http, es, EsOpen).Run();

                Assert.Empty(es.Posted);
                Assert.Equal("~/.emulationstation/media/ps2/videos/God Hand (USA)-video.mp4", (string)Find(install).Element("video"));
            }
        }

        [Fact]
        public void Startup_sync_leaves_the_video_for_the_background()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var install = new RetroBatInstall(pcs.Combine("A"));
                var (hfs, ss) = Sources();
                var http = new FakeMediaHttp();

                var report = StartupSync.Run(install, cloud.Path, install.DefaultRomsPath, new CloudMediaOptions { Automatic = true },
                    MediaTest.Search(hfs, ss), http);

                Assert.Equal(3, report.AutoImport.FilesQueued);
                Assert.DoesNotContain(http.Downloads, d => d.Contains("/video/"));
                Assert.False(new BackgroundMediaQueue(install).IsEmpty);
                Assert.True(SyncLock.SinceLastSync(install) < TimeSpan.FromMinutes(1));
            }
        }
    }

    /// <summary>O ES aberto em memória: os jogos que ele tem e o que recebeu pelo POST /addgames.</summary>
    sealed class FakeEsGames : IEsGames
    {
        readonly Dictionary<string, List<(EsGame Game, Dictionary<string, string> Fields)>> _systems =
            new Dictionary<string, List<(EsGame, Dictionary<string, string>)>>(StringComparer.OrdinalIgnoreCase);

        public Func<bool> Idle = () => true;

        public List<(string System, string Xml)> Posted = new List<(string, string)>();

        public void Add(string system, string path, Dictionary<string, string> fields)
        {
            if (!_systems.TryGetValue(system, out var games))
                _systems[system] = games = new List<(EsGame, Dictionary<string, string>)>();

            games.Add((new EsGame { Id = "id" + games.Count, Path = path }, fields));
        }

        public bool IsIdle() => Idle();

        public IReadOnlyList<EsGame> Games(string system) =>
            _systems.TryGetValue(system, out var games) ? games.Select(g => g.Game).ToList() : new List<EsGame>();

        public IReadOnlyDictionary<string, string> Game(string system, string id) =>
            _systems.TryGetValue(system, out var games) ? games.FirstOrDefault(g => g.Game.Id == id).Fields ?? new Dictionary<string, string>() : new Dictionary<string, string>();

        public bool AddGames(string system, string gamelistXml)
        {
            Posted.Add((system, gamelistXml));
            return true;
        }
    }
}
