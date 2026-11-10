using System.IO;
using System.Linq;
using Retroboat.Accounts;
using Retroboat.Config;
using Retroboat.Library;
using Xunit;

namespace Retroboat.Tests
{
    public class EsCollectionsConfigTests
    {
        static RetroBatInstall Pc(TempDir pcs, string name)
        {
            var install = new RetroBatInstall(pcs.Combine(name));
            Directory.CreateDirectory(install.UserEmulationStationPath);
            return install;
        }

        static string Collection(RetroBatInstall install, string file) => Path.Combine(install.UserEmulationStationPath, "collections", file);

        static void Write(RetroBatInstall install, string file, params string[] lines)
        {
            var path = Collection(install, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, string.Concat(lines.Select(l => l + "\r\n")));
        }

        static string[] Read(RetroBatInstall install, string file) =>
            File.ReadAllText(Collection(install, file)).Split(new[] { "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries);

        static ConfigSyncResult Sync(RetroBatInstall install, string roms, TempDir cloud, string pc) =>
            new ConfigSync(new EsCollectionsConfig(install, roms), install.RetroboatDataPath, cloud.Path, pc).Sync();

        [Fact]
        public void A_custom_collection_follows_to_a_pc_with_another_games_folder()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var romsA = pcs.Combine(@"JogosA");
                // Como o ES grava: o mesmo disco da pasta do ES vira "~/../..", outro disco fica absoluto.
                Write(a, "custom-Corrida.cfg",
                    "~/../../JogosA/ps2/Burnout 3 (USA).iso",
                    romsA.Replace('\\', '/') + "/psx/Gran Turismo 2 (USA).zip",
                    "./roms/windows/Jogo de PC.lnk",
                    "Z:/Outro/lugar/Jogo.iso");
                Assert.Equal(new[] { "custom-Corrida.cfg" }, Sync(a, romsA, cloud, "PC-A").Sent);

                var b = Pc(pcs, "B");
                var romsB = @"D:\Jogos";
                var received = Sync(b, romsB, cloud, "PC-B");

                Assert.Equal(new[] { "custom-Corrida.cfg" }, received.Received);
                Assert.Equal(new[]
                {
                    "D:/Jogos/ps2/Burnout 3 (USA).iso",
                    "D:/Jogos/psx/Gran Turismo 2 (USA).zip",
                    "./roms/windows/Jogo de PC.lnk"
                }, Read(b, "custom-Corrida.cfg"));

                // O jogo de fora das pastas não foi para a nuvem.
                Assert.DoesNotContain("Outro", File.ReadAllText(cloud.Combine(@"configuracoes\colecoes\PC-A.xml")));

                // O ES do B regrava a coleção do jeito dele: nada muda na nuvem.
                Write(b, "custom-Corrida.cfg", "D:/Jogos/ps2/Burnout 3 (USA).iso", "D:/Jogos/psx/Gran Turismo 2 (USA).zip", "./roms/windows/Jogo de PC.lnk");
                Assert.False(Sync(b, romsB, cloud, "PC-B").HasChanges);
            }
        }

        [Fact]
        public void A_game_added_on_one_pc_keeps_the_local_only_games_of_the_other()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                Write(a, "custom-Amigos.cfg", "C:/JogosA/ps2/Jogo 1.iso");
                Sync(a, @"C:\JogosA", cloud, "PC-A");
                Sync(b, @"D:\Jogos", cloud, "PC-B");
                Write(b, "custom-Amigos.cfg", "D:/Jogos/ps2/Jogo 1.iso", "E:/Emuladores/so-no-B.exe");
                Sync(b, @"D:\Jogos", cloud, "PC-B");

                Write(a, "custom-Amigos.cfg", "C:/JogosA/ps2/Jogo 1.iso", "C:/JogosA/ps2/Jogo 2.iso");
                Sync(a, @"C:\JogosA", cloud, "PC-A");
                Sync(b, @"D:\Jogos", cloud, "PC-B");

                Assert.Equal(new[] { "D:/Jogos/ps2/Jogo 1.iso", "D:/Jogos/ps2/Jogo 2.iso", "E:/Emuladores/so-no-B.exe" }, Read(b, "custom-Amigos.cfg"));
            }
        }

        [Fact]
        public void Deleting_a_collection_on_one_pc_deletes_it_on_the_others()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                Write(a, "custom-Velhos.cfg", "C:/JogosA/ps2/Jogo.iso");
                Write(a, "Só RPG.xcc", "<filter><genre>RPG</genre></filter>");
                Sync(a, @"C:\JogosA", cloud, "PC-A");
                Sync(b, @"D:\Jogos", cloud, "PC-B");
                Assert.True(File.Exists(Collection(b, "custom-Velhos.cfg")));
                Assert.True(File.Exists(Collection(b, "Só RPG.xcc")));

                File.Delete(Collection(a, "custom-Velhos.cfg"));
                Sync(a, @"C:\JogosA", cloud, "PC-A");
                Sync(b, @"D:\Jogos", cloud, "PC-B");

                Assert.False(File.Exists(Collection(b, "custom-Velhos.cfg")));
                Assert.True(File.Exists(Collection(b, "Só RPG.xcc")));
            }
        }

        [Fact]
        public void Without_a_games_folder_nothing_is_synced()
        {
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                Assert.False(new EsCollectionsConfig(a, "").IsAvailable);
                Assert.True(new EsCollectionsConfig(a, @"C:\Jogos").IsAvailable);
            }
        }

        [Fact]
        public void The_list_of_visible_custom_collections_now_travels_with_the_es_options()
        {
            Assert.True(EsSettingsRules.IsPortableKey("CollectionSystemsCustom"));
            Assert.True(EsSettingsRules.IsPortableKey("CollectionSystemsAuto"));
        }
    }
}
