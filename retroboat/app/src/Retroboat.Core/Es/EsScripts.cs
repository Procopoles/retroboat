using System.IO;
using System.Linq;
using System.Text;
using Retroboat.Library;
using Retroboat.Saves;

namespace Retroboat.Es
{
    /// <summary>
    /// Scripts de evento do ES (.emulationstation\scripts\&lt;evento&gt;\), gravados pelo Retroboat.exe a cada abertura.
    /// Os eventos fora da lista assíncrona do ES (Scripting.cpp), como theme-changed e update-gamelists, fazem o
    /// ES esperar o script terminar antes de seguir.
    /// </summary>
    public static class EsScripts
    {
        /// <summary>A raiz do RetroBat vista do script: caminhos relativos mantêm o RetroBat portátil (pen drive, outra letra).</summary>
        public const string Root = @"%~dp0..\..\..\..";

        /// <summary>"Atualizar listas de jogos" no menu do ES; o /reloadgames da API não dispara este evento.</summary>
        public const string UpdateGamelistsEvent = "update-gamelists";
        public const string UpdateGamelistsFile = "retroboat-nuvem.bat";

        /// <summary>Caminho de um exe do Retroboat como o script o vê.</summary>
        public static string ExeFromRoot(RetroBatInstall install, string exeDirectory, string exeName)
        {
            if (RetroBatInstall.SamePath(exeDirectory, install.RetroboatDataPath))
                return Root + @"\retroboat\" + exeName;
            if (RetroBatInstall.SamePath(exeDirectory, install.Root))
                return Root + @"\" + exeName;

            return Path.Combine(exeDirectory, exeName);
        }

        /// <summary>Grava o script se estiver faltando ou diferente. Só ASCII: o cmd lê o .bat na página de código do console.</summary>
        /// <returns>true se gravou (ou gravaria, em <paramref name="dryRun"/>).</returns>
        public static bool Write(RetroBatInstall install, string eventName, string fileName, string script, bool dryRun = false)
        {
            var path = Path.Combine(install.UserEmulationStationPath, "scripts", eventName, fileName);
            var bytes = Encoding.ASCII.GetBytes(script);
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes))
                return false;

            if (!dryRun)
                SaveSync.WriteAtomically(path, bytes);
            return true;
        }

        /// <summary>
        /// "Atualizar listas de jogos" faz a mesma sincronização da abertura (Retroboat.exe --refresh) antes do ES reler
        /// as listas: jogos que chegaram ao Drive com o ES aberto ganham o marcador e já aparecem.
        /// </summary>
        /// <returns>true se gravou; false se já estava em dia ou se o Retroboat.exe não está em <paramref name="exeDirectory"/>.</returns>
        public static bool WriteUpdateGamelists(RetroBatInstall install, string exeDirectory, bool dryRun = false)
        {
            if (!File.Exists(Path.Combine(exeDirectory, "Retroboat.exe")))
                return false;

            var exe = ExeFromRoot(install, exeDirectory, "Retroboat.exe");
            var script =
                "@echo off\r\n" +
                "rem Retroboat: \"Atualizar listas de jogos\" do ES sincroniza com a nuvem como na abertura\r\n" +
                "rem (jogos novos do Drive, capas, BIOS, saves). O ES espera este script antes de reler as listas.\r\n" +
                "rem Gravado pelo Retroboat.exe; editar aqui nao adianta.\r\n" +
                "if exist \"" + exe + "\" \"" + exe + "\" --refresh --retrobat \"" + Root + "\" >nul 2>&1\r\n";

            return Write(install, UpdateGamelistsEvent, UpdateGamelistsFile, script, dryRun);
        }

        /// <summary>O ES abrindo, por qualquer caminho (Retroboat.exe, RetroBat.exe, o próprio emulationstation.exe).</summary>
        public const string StartEvent = "start";

        /// <summary>"-nowait": o ES roda numa thread e não espera (o "start" não está na lista assíncrona do ES).</summary>
        public const string SteamSwitchFile = "retroboat-botao-xbox-nowait.bat";

        /// <summary>
        /// RB-040: com o ES aberto, o botão Xbox troca entre o Retroboat e a Steam. O vigia precisa estar rodando mesmo com
        /// o RetroBat aberto direto, sem o Retroboat.exe; o evento "start" do ES o abre (<c>Retroboat.exe --steam-switch</c>),
        /// e ele fecha junto com o ES. O <c>start ""</c> devolve o controle ao ES na hora.
        /// </summary>
        /// <returns>true se gravou; false se já estava em dia ou se o Retroboat.exe não está em <paramref name="exeDirectory"/>.</returns>
        public static bool WriteSteamSwitch(RetroBatInstall install, string exeDirectory, bool dryRun = false)
        {
            if (!File.Exists(Path.Combine(exeDirectory, "Retroboat.exe")))
                return false;

            var exe = ExeFromRoot(install, exeDirectory, "Retroboat.exe");
            var script =
                "@echo off\r\n" +
                "rem Retroboat (RB-040): o botao Xbox troca entre o Retroboat e a Steam (Big Picture) enquanto o ES\r\n" +
                "rem estiver aberto. Este script abre o vigia do botao, que fecha junto com o ES.\r\n" +
                "rem Gravado pelo Retroboat.exe; editar aqui nao adianta.\r\n" +
                "if exist \"" + exe + "\" start \"\" \"" + exe + "\" --steam-switch --retrobat \"" + Root + "\"\r\n";

            return Write(install, StartEvent, SteamSwitchFile, script, dryRun);
        }

        /// <summary>"-nowait", como o do botão Xbox: o ES não espera.</summary>
        public const string BackgroundMediaFile = "retroboat-midias-nowait.bat";

        /// <summary>
        /// RB-041: mídias secundárias (vídeos, manuais) que ficaram na fila de uma sessão anterior (o PC desligou no meio)
        /// continuam quando o ES abre, por qualquer caminho. Sem fila (o arquivo não existe), o script não abre nada.
        /// </summary>
        /// <returns>true se gravou; false se já estava em dia ou se o Retroboat.exe não está em <paramref name="exeDirectory"/>.</returns>
        public static bool WriteBackgroundMedia(RetroBatInstall install, string exeDirectory, bool dryRun = false)
        {
            if (!File.Exists(Path.Combine(exeDirectory, "Retroboat.exe")))
                return false;

            var exe = ExeFromRoot(install, exeDirectory, "Retroboat.exe");
            var queue = Root + @"\retroboat\" + CloudStatusFolder + @"\" + Media.Background.BackgroundMediaQueue.FileName;
            var script =
                "@echo off\r\n" +
                "rem Retroboat (RB-041): videos, manuais e imagens extras que ficaram na fila continuam em segundo plano.\r\n" +
                "rem Sem fila, nada abre. Gravado pelo Retroboat.exe; editar aqui nao adianta.\r\n" +
                "if exist \"" + queue + "\" if exist \"" + exe + "\" start \"\" \"" + exe + "\" --media --retrobat \"" + Root + "\"\r\n";

            return Write(install, StartEvent, BackgroundMediaFile, script, dryRun);
        }

        const string CloudStatusFolder = Cloud.CloudStatusFile.Folder;
    }
}
