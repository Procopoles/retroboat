using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Settings;

namespace Retroboat.Cloud
{
    public enum LibraryChoiceOutcome
    {
        /// <summary>O PC já tem a biblioteca escolhida ([Cloud] Library).</summary>
        AlreadyChosen,

        /// <summary>[Cloud] Path configurado à mão: a escolha não se aplica.</summary>
        Configured,

        /// <summary>Uma biblioteca só, em uma conta: adotada sem perguntar.</summary>
        Adopted,

        /// <summary>Nenhuma conta do Google Drive montada.</summary>
        NoDrive,

        /// <summary>Drive montado, sem pasta "retroboat" em nenhuma conta. Criar é do setup, com confirmação.</summary>
        NoLibrary,

        /// <summary>Pasta "retroboat" em duas ou mais contas: quem escolhe é a pessoa, no setup.</summary>
        Several,

        /// <summary>A única pasta tem um biblioteca.xml danificado (ou o Drive falhou ao criá-lo): nada foi escolhido.</summary>
        Unidentified
    }

    public sealed class LibraryChoiceResult
    {
        public LibraryChoiceOutcome Outcome { get; set; }

        /// <summary>A conta da biblioteca adotada.</summary>
        public CloudDrive Drive { get; set; }

        public LibraryIdentity Identity { get; set; }

        /// <summary>As contas com pasta "retroboat" (para o log e, depois, para o setup).</summary>
        public List<CloudDrive> Candidates { get; } = new List<CloudDrive>();

        public string Error { get; set; }
    }

    /// <summary>
    /// RB-021: escolha da biblioteca da nuvem sem perguntar, quando não há dúvida. Com exatamente uma conta com a pasta
    /// "retroboat", adota essa biblioteca: lê o biblioteca.xml ou cria um (nunca substitui) e grava o id em
    /// [Cloud] Library. Em qualquer outro caso não escreve nada no Drive e deixa a escolha para o setup.
    /// </summary>
    public static class LibraryChoice
    {
        /// <summary>Muda <paramref name="settings"/> só quando adota; quem chama grava o retroboat.ini.</summary>
        public static LibraryChoiceResult ChooseAutomatically(RetroboatSettings settings, string pcName = null)
        {
            return ChooseAutomatically(settings, CloudLocator.FindDrives(), pcName);
        }

        internal static LibraryChoiceResult ChooseAutomatically(RetroboatSettings settings, IEnumerable<CloudDrive> drives, string pcName = null)
        {
            var result = new LibraryChoiceResult();
            if (!string.IsNullOrWhiteSpace(settings.CloudPath))
            {
                result.Outcome = LibraryChoiceOutcome.Configured;
                return result;
            }

            if (!string.IsNullOrWhiteSpace(settings.CloudLibrary))
            {
                result.Outcome = LibraryChoiceOutcome.AlreadyChosen;
                return result;
            }

            var all = drives.ToList();
            result.Candidates.AddRange(all.Where(d => d.HasRetroboatFolder));

            if (all.Count == 0)
                result.Outcome = LibraryChoiceOutcome.NoDrive;
            else if (result.Candidates.Count == 0)
                result.Outcome = LibraryChoiceOutcome.NoLibrary;
            else if (result.Candidates.Count > 1)
                result.Outcome = LibraryChoiceOutcome.Several;
            else
                Adopt(settings, result.Candidates[0], pcName, result);

            return result;
        }

        static void Adopt(RetroboatSettings settings, CloudDrive drive, string pcName, LibraryChoiceResult result)
        {
            try
            {
                result.Identity = LibraryIdentity.Adopt(drive.RetroboatPath, pcName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result.Error = ex.Message;
            }

            if (result.Identity == null)
            {
                result.Outcome = LibraryChoiceOutcome.Unidentified;
                result.Error = result.Error ?? Path.Combine(drive.RetroboatPath, LibraryIdentity.FileName) + " está danificado; ele não foi mexido.";
                return;
            }

            result.Outcome = LibraryChoiceOutcome.Adopted;
            result.Drive = drive;
            settings.CloudLibrary = result.Identity.Id;
        }
    }
}
