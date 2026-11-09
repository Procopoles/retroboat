using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Retroboat.Cloud;
using Retroboat.Launch;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Settings;

namespace Retroboat.Launcher
{
    /// <summary>
    /// RB-021: setup em tela cheia, no padrão das telas para controle (A escolhe, B volta). Passo 1: a biblioteca da
    /// nuvem; passo 2: a pasta de jogos deste PC; passo 3: as opções principais. O que vai para o Drive sai do
    /// <see cref="LibrarySetup"/>; aqui só ficam as telas.
    /// </summary>
    static class SetupFlow
    {
        const string Title = "RETROBOAT · CONFIGURAÇÃO";
        const int Back = -1;
        const int Next = 1;

        /// <returns>false se a pessoa desistiu do primeiro uso (nada é aberto); true nos outros casos.</returns>
        public static bool Run(RetroBatInstall install, RetroboatSettings settings, string settingsPath, SetupScope scope)
        {
            bool firstRun = string.IsNullOrEmpty(settings.RomsPath);
            var draft = Copy(settings);
            int steps = scope == SetupScope.Full ? 3 : 1;
            int step = 1;
            Log.Info("Setup: " + (scope == SetupScope.Full ? (firstRun ? "primeiro uso" : "completo") : "biblioteca da nuvem"));

            while (step >= 1 && step <= steps)
            {
                int move;
                switch (step)
                {
                    case 1: move = LibraryStep(install, draft, StepTitle(1, steps)); break;
                    case 2: move = FolderStep(install, draft, StepTitle(2, steps)); break;
                    default: move = OptionsStep(draft, StepTitle(3, steps)); break;
                }

                step += move;
            }

            if (step < 1)
            {
                if (firstRun)
                {
                    Log.Info("Setup cancelado no primeiro uso; nada foi aberto.");
                    return false;
                }

                // Biblioteca escolhida e depois desistida fica como estava; o biblioteca.xml criado no Drive é inofensivo.
                Log.Info("Setup fechado sem mudanças.");
                return true;
            }

            Apply(draft, settings);
            settings.Save(settingsPath);
            Log.Info("Setup concluído: biblioteca " + (settings.CloudLibrary ?? "(nenhuma)") + ", pasta de jogos " + settings.RomsPath +
                     ", vídeos " + settings.CloudVideos + ", manuais " + settings.CloudManuals + ", capas automáticas " + settings.AutoMedia +
                     ", abrir o Drive " + settings.CloudAppAutoOpen);
            return true;
        }

        static string StepTitle(int step, int steps) => steps == 1 ? Title : Title + " · " + step + " DE " + steps;

        // ---- Passo 1: biblioteca da nuvem ----

        static int LibraryStep(RetroBatInstall install, RetroboatSettings draft, string title)
        {
            while (true)
            {
                var candidates = Busy(title, "Procurando a biblioteca no Google Drive...", () => LibrarySetup.Describe(CloudLocator.FindDrives()));

                // Nenhuma biblioteca à vista: o Drive pode estar terminando de carregar. Só depois da segunda busca o setup
                // oferece criar uma.
                if (candidates.Count > 0 && !candidates.Any(c => c.HasLibrary))
                {
                    ChoiceForm.Ask(title, "Procurando a biblioteca no Google Drive...",
                        "Se você já usa o Retroboat em outro PC com esta conta, ela aparece quando o Drive terminar de carregar.",
                        new string[0], autoClose: () => true, canGoBack: false, autoCloseInterval: (int)LibrarySetup.SecondSearchDelay.TotalMilliseconds);
                    candidates = LibrarySetup.Describe(CloudLocator.FindDrives());
                }

                var options = new List<string>();
                var actions = new List<Func<int?>>();

                foreach (var c in candidates.Where(c => c.HasLibrary))
                {
                    bool inUse = c.Identity != null && string.Equals(c.Identity.Id, draft.CloudLibrary, StringComparison.OrdinalIgnoreCase);
                    options.Add("Usar a biblioteca de " + c.Drive.DisplayName + " · " + c.Summary + (inUse ? " · em uso" : ""));
                    var drive = c.Drive;
                    actions.Add(() => UseLibrary(draft, drive, title));
                }

                foreach (var c in candidates.Where(c => !c.HasLibrary))
                {
                    options.Add("Criar uma biblioteca nova em " + c.Drive.DisplayName);
                    var drive = c.Drive;
                    actions.Add(() => CreateLibrary(install, draft, drive, title));
                }

                options.Add("Procurar de novo");
                actions.Add(() => null);

                options.Add(string.IsNullOrEmpty(draft.CloudLibrary) ? "Continuar sem nuvem por agora" : "Manter como está");
                actions.Add(() => Next);

                string heading, text;
                int libraries = candidates.Count(c => c.HasLibrary);
                if (candidates.Count == 0)
                {
                    heading = "Google Drive não encontrado";
                    text = "Abra o Google Drive para computador e entre na sua conta para usar os jogos, saves e configurações da nuvem.";
                }
                else if (libraries == 0)
                {
                    heading = "Nenhuma biblioteca no Google Drive";
                    text = "A biblioteca guarda os seus jogos, saves e configurações na nuvem, para qualquer PC.";
                }
                else if (libraries == 1)
                {
                    heading = "Biblioteca da nuvem encontrada";
                    text = "Os jogos, saves e configurações vêm desta biblioteca.";
                }
                else
                {
                    heading = "Escolha a biblioteca da nuvem";
                    text = "Há uma biblioteca do Retroboat em mais de uma conta do Google Drive. Este PC vai usar só a escolhida.";
                }

                int choice = ChoiceForm.Ask(title, heading, text, options);
                if (choice == ChoiceForm.Back)
                    return Back;

                var result = actions[choice]();
                if (result.HasValue)
                    return result.Value;
            }
        }

        static int? UseLibrary(RetroboatSettings draft, CloudDrive drive, string title)
        {
            var identity = Busy(title, "Abrindo a biblioteca...", () => LibrarySetup.Use(draft, drive));
            if (identity == null)
            {
                ChoiceForm.Ask(title, "Não deu para usar esta biblioteca",
                    "O arquivo " + Path.Combine(drive.RetroboatPath, LibraryIdentity.FileName) + " está danificado. O Retroboat não mexeu nele.",
                    new[] { "Voltar" });
                return null;
            }

            Log.Info("Setup: biblioteca escolhida: " + drive.RetroboatPath + " (id " + identity.Id + ")");
            return Next;
        }

        static int? CreateLibrary(RetroBatInstall install, RetroboatSettings draft, CloudDrive drive, string title)
        {
            int confirm = ChoiceForm.Ask(title, "Criar uma biblioteca nova em " + drive.DisplayName + "?",
                "O Retroboat cria a pasta retroboat com as pastas dos consoles. Nada que já está no seu Drive é mexido.\r\n\r\n" +
                "Se você já usa o Retroboat em outro PC com esta conta, volte e escolha Procurar de novo: o Drive pode estar terminando de carregar.",
                new[] { "Criar a biblioteca", "Voltar" });
            if (confirm != 0)
                return null;

            bool created = false;
            var identity = Busy(title, "Criando a biblioteca...", () => LibrarySetup.Create(install, draft, drive, out created));
            if (identity == null)
            {
                ChoiceForm.Ask(title, "Não deu para criar a biblioteca",
                    "O arquivo " + Path.Combine(drive.RetroboatPath, LibraryIdentity.FileName) + " está danificado. O Retroboat não mexeu nele.",
                    new[] { "Voltar" });
                return null;
            }

            Log.Info((created ? "Setup: biblioteca criada em " : "Setup: a pasta apareceu antes de criar; usando a que existe em ") +
                     drive.RetroboatPath + " (id " + identity.Id + ")");
            return Next;
        }

        // ---- Passo 2: pasta de jogos ----

        static int FolderStep(RetroBatInstall install, RetroboatSettings draft, string title)
        {
            var current = draft.RomsPath;
            var suggestions = Busy(title, "Procurando os discos...", () => LibrarySetup.SuggestRomsFolders(install, current));
            var options = suggestions.Select(s => s.Label).ToList();
            options.Add("Escolher outra pasta (mouse e teclado)...");

            string text = "Os jogos que você baixa da nuvem ficam nesta pasta. Favoritos, metadados e saves não são gravados nela.";
            if (!string.IsNullOrEmpty(current))
                text += "\r\nTrocando de pasta, os jogos já baixados ficam na antiga; os jogos da nuvem voltam a aparecer como Na nuvem.";

            while (true)
            {
                int choice = ChoiceForm.Ask(title, "Onde ficam os jogos neste PC?", text, options);
                if (choice == ChoiceForm.Back)
                    return Back;

                if (choice < suggestions.Count)
                {
                    draft.RomsPath = suggestions[choice].Path;
                    return Next;
                }

                var picked = PickFolder(current ?? install.DefaultRomsPath);
                if (picked == null)
                    continue;

                if (!LibrarySetup.IsValidRomsFolder(picked))
                {
                    ChoiceForm.Ask(title, "Esta pasta não serve", "Escolha uma pasta num disco deste PC, fora do Google Drive.", new[] { "Voltar" });
                    continue;
                }

                draft.RomsPath = Path.GetFullPath(picked).TrimEnd('\\');
                return Next;
            }
        }

        static string PickFolder(string start)
        {
            using (var owner = new Form { TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterScreen, Size = new System.Drawing.Size(1, 1), Opacity = 0 })
            using (var dialog = new FolderBrowserDialog { Description = "Pasta dos jogos deste PC", ShowNewFolderButton = true })
            {
                owner.Show();
                if (Directory.Exists(start))
                    dialog.SelectedPath = start;

                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        // ---- Passo 3: opções principais ----

        static int OptionsStep(RetroboatSettings draft, string title)
        {
            int selected = 0;
            while (true)
            {
                var options = new[]
                {
                    "Baixar os vídeos dos jogos (ocupa mais espaço): " + YesNo(draft.CloudVideos),
                    "Baixar os manuais (PDF): " + YesNo(draft.CloudManuals),
                    "Capas e informações automáticas: " + YesNo(draft.AutoMedia),
                    "Abrir o Google Drive sozinho quando precisar: " + YesNo(draft.CloudAppAutoOpen),
                    "Concluir"
                };

                int choice = ChoiceForm.Ask(title, "Opções principais", "A liga ou desliga. Dá para mudar depois no menu NUVEM do ES.", options, selected: selected);
                switch (choice)
                {
                    case ChoiceForm.Back: return Back;
                    case 0: draft.CloudVideos = !draft.CloudVideos; break;
                    case 1: draft.CloudManuals = !draft.CloudManuals; break;
                    case 2: draft.AutoMedia = !draft.AutoMedia; break;
                    case 3: draft.CloudAppAutoOpen = !draft.CloudAppAutoOpen; break;
                    default: return Next;
                }

                selected = choice;
            }
        }

        static string YesNo(bool value) => value ? "SIM" : "NÃO";

        // ---- Apoio ----

        static T Busy<T>(string title, string heading, Func<T> work) => ChoiceForm.Busy(title, heading, work);

        static RetroboatSettings Copy(RetroboatSettings s) => new RetroboatSettings
        {
            RomsPath = s.RomsPath,
            CloudPath = s.CloudPath,
            CloudLibrary = s.CloudLibrary,
            CloudVideos = s.CloudVideos,
            CloudManuals = s.CloudManuals,
            AutoMedia = s.AutoMedia,
            CloudAppAutoOpen = s.CloudAppAutoOpen,
            EsBiosCheckHandled = s.EsBiosCheckHandled
        };

        static void Apply(RetroboatSettings from, RetroboatSettings to)
        {
            to.RomsPath = from.RomsPath;
            to.CloudLibrary = from.CloudLibrary;
            to.CloudVideos = from.CloudVideos;
            to.CloudManuals = from.CloudManuals;
            to.AutoMedia = from.AutoMedia;
            to.CloudAppAutoOpen = from.CloudAppAutoOpen;
        }
    }
}
