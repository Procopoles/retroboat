using System;
using System.IO;
using Retroboat.SteamSwitch;
using Xunit;

namespace Retroboat.Tests
{
    public class SteamSwitchTests
    {
        static readonly DateTime T0 = new DateTime(2026, 11, 17, 20, 0, 0, DateTimeKind.Utc);

        sealed class Run
        {
            public readonly SwitchFlow Flow = new SwitchFlow();
            public SwitchScreen Screen = SwitchScreen.Es;
            public bool HasSteam = true;
            public GuideButtonConflict Conflict;
            public int ConflictChecks;

            public SwitchStep At(double seconds, bool pressed = false) =>
                Flow.Tick(T0.AddSeconds(seconds), pressed, () => Screen, () => HasSteam, () => { ConflictChecks++; return Conflict; });
        }

        [Fact]
        public void One_press_in_the_es_asks_and_a_second_opens_the_big_picture()
        {
            var run = new Run();

            Assert.Equal(SwitchStep.AskSteam, run.At(0, pressed: true));
            Assert.Equal(SwitchStep.None, run.At(1));
            Assert.Equal(SwitchStep.OpenSteam, run.At(2, pressed: true));

            run.Screen = SwitchScreen.BigPicture;
            Assert.Equal(SwitchStep.SteamOpened, run.At(2.5));
            Assert.False(run.Flow.IsBusy);
        }

        [Fact]
        public void The_question_goes_away_without_a_second_press()
        {
            var run = new Run();

            run.At(0, pressed: true);
            Assert.Equal(SwitchStep.HideAsk, run.At(SwitchFlow.ConfirmWindow.TotalSeconds + 0.1));
            Assert.False(run.Flow.IsBusy);

            // O próximo aperto pergunta de novo, não confirma.
            Assert.Equal(SwitchStep.AskSteam, run.At(10, pressed: true));
        }

        [Fact]
        public void The_question_goes_away_when_another_window_comes_to_the_front()
        {
            var run = new Run();

            run.At(0, pressed: true);
            run.Screen = SwitchScreen.Other; // um jogo abriu, o usuário foi para a área de trabalho...
            Assert.Equal(SwitchStep.HideAsk, run.At(1));
            Assert.Equal(SwitchStep.None, run.At(1.5, pressed: true));
        }

        [Fact]
        public void In_the_big_picture_the_button_brings_the_es_back()
        {
            var run = new Run { Screen = SwitchScreen.BigPicture };

            Assert.Equal(SwitchStep.AskEs, run.At(0, pressed: true));
            Assert.Equal(SwitchStep.ReturnToEs, run.At(1, pressed: true));
            Assert.Equal(SwitchStep.None, run.At(1.2));
            Assert.Equal(SwitchStep.FocusEsAgain, run.At(1.4));

            // Só conta como de volta depois de ficar na frente um tempo seguido.
            run.Screen = SwitchScreen.Es;
            Assert.Equal(SwitchStep.None, run.At(1.5));
            Assert.Equal(SwitchStep.None, run.At(2.5));
            Assert.Equal(SwitchStep.EsReturned, run.At(1.5 + SwitchFlow.EsSettle.TotalSeconds));
            Assert.False(run.Flow.IsBusy);
        }

        [Fact]
        public void The_steam_window_that_opens_over_the_es_after_the_big_picture_is_pushed_back()
        {
            // Visto em 17/11/2026: o ES chega na frente e, 0,4 s depois, a janela normal da Steam toma o foco.
            var run = new Run { Screen = SwitchScreen.BigPicture };
            run.At(0, pressed: true);
            run.At(1, pressed: true);

            run.Screen = SwitchScreen.Es;
            Assert.Equal(SwitchStep.None, run.At(1.1));

            run.Screen = SwitchScreen.Other; // a janela da Steam
            Assert.Equal(SwitchStep.FocusEsAgain, run.At(1.4));
            Assert.True(run.Flow.IsBusy);

            run.Screen = SwitchScreen.Es;
            Assert.Equal(SwitchStep.None, run.At(1.5));
            Assert.Equal(SwitchStep.EsReturned, run.At(1.5 + SwitchFlow.EsSettle.TotalSeconds));
        }

        [Fact]
        public void Leaving_the_big_picture_from_the_steam_menu_also_keeps_the_es_in_front()
        {
            var run = new Run { Screen = SwitchScreen.Other };
            run.Flow.ReturnAfterSteamClosed(T0);
            Assert.True(run.Flow.IsBusy);

            Assert.Equal(SwitchStep.FocusEsAgain, run.At(0.4));
            Assert.Equal(SwitchStep.None, run.At(0.5, pressed: true)); // apertos na volta não perguntam nada

            run.Screen = SwitchScreen.Es;
            run.At(0.6);
            Assert.Equal(SwitchStep.EsReturned, run.At(0.6 + SwitchFlow.EsSettle.TotalSeconds));
        }

        [Fact]
        public void The_es_that_never_comes_back_gives_up()
        {
            var run = new Run { Screen = SwitchScreen.BigPicture };
            run.At(0, pressed: true);
            run.At(1, pressed: true);

            Assert.Equal(SwitchStep.EsReturnFailed, run.At(1 + SwitchFlow.EsTimeout.TotalSeconds + 0.1));
            Assert.False(run.Flow.IsBusy);
        }

        [Fact]
        public void In_a_steam_game_the_button_brings_the_big_picture_back_without_touching_the_es()
        {
            var run = new Run { Screen = SwitchScreen.SteamGame };

            Assert.Equal(SwitchStep.AskBigPicture, run.At(0, pressed: true));
            Assert.Equal(SwitchStep.OpenSteam, run.At(1, pressed: true));

            run.Screen = SwitchScreen.BigPicture;
            Assert.Equal(SwitchStep.BigPictureOverGame, run.At(1.3)); // não SteamOpened: o ES não é minimizado de novo
            Assert.False(run.Flow.IsBusy);

            // Do Big Picture, o botão volta a perguntar pelo Retroboat, como sempre.
            Assert.Equal(SwitchStep.AskEs, run.At(5, pressed: true));
        }

        [Fact]
        public void In_a_steam_game_the_question_goes_away_if_the_game_leaves_the_front()
        {
            var run = new Run { Screen = SwitchScreen.SteamGame };
            run.At(0, pressed: true);

            run.Screen = SwitchScreen.Other;
            Assert.Equal(SwitchStep.HideAsk, run.At(0.5));
        }

        [Fact]
        public void A_steam_game_is_an_exe_in_a_steam_library()
        {
            Assert.True(SteamClient.IsLibraryGame(@"C:\Program Files (x86)\Steam\steamapps\common\Hitman Contracts\HitmanContracts.exe"));
            Assert.True(SteamClient.IsLibraryGame(@"D:\SteamLibrary\steamapps\common\SubnauticaZero\SubnauticaZero.exe"));
            Assert.False(SteamClient.IsLibraryGame(@"C:\Stuffs\Aplicacoes\retroboat\build\emulators\pcsx2\pcsx2-qt.exe"));
            Assert.False(SteamClient.IsLibraryGame(@"C:\Program Files (x86)\Steam\steamwebhelper.exe"));
            Assert.False(SteamClient.IsLibraryGame(null));
        }

        [Fact]
        public void With_a_game_or_anything_else_in_front_the_button_does_nothing()
        {
            var run = new Run { Screen = SwitchScreen.Other };

            Assert.Equal(SwitchStep.None, run.At(0, pressed: true));
            Assert.Equal(0, run.ConflictChecks);
            Assert.False(run.Flow.IsBusy);
        }

        [Fact]
        public void Without_steam_the_button_does_nothing()
        {
            var run = new Run { HasSteam = false };

            Assert.Equal(SwitchStep.None, run.At(0, pressed: true));
            Assert.Equal(0, run.ConflictChecks);
        }

        [Fact]
        public void Another_program_on_the_button_shows_how_to_turn_it_off_and_does_not_switch()
        {
            var run = new Run { Conflict = GuideButtonConflicts.GameBar };

            Assert.Equal(SwitchStep.WarnConflict, run.At(0, pressed: true));
            Assert.Same(GuideButtonConflicts.GameBar, run.Flow.Conflict);
            Assert.False(run.Flow.IsBusy);

            // Desligou a opção: o próximo aperto já pergunta.
            run.Conflict = null;
            Assert.Equal(SwitchStep.AskSteam, run.At(20, pressed: true));
        }

        [Fact]
        public void A_slow_steam_shows_that_it_is_opening_once_and_gives_up_after_the_timeout()
        {
            var run = new Run();
            run.At(0, pressed: true);
            run.At(1, pressed: true);

            Assert.Equal(SwitchStep.None, run.At(1.5));
            Assert.Equal(SwitchStep.ShowOpening, run.At(2.2));
            Assert.Equal(SwitchStep.None, run.At(3));
            Assert.Equal(SwitchStep.None, run.At(4, pressed: true)); // apertos enquanto abre não fazem nada
            Assert.Equal(SwitchStep.SteamFailed, run.At(1 + SwitchFlow.SteamTimeout.TotalSeconds + 0.1));
        }

        [Fact]
        public void Game_bar_uses_the_button_unless_turned_off_or_missing()
        {
            Assert.True(GuideButtonConflicts.GameBarUsesButton(null, installed: true)); // nunca mexeu: vem ligada
            Assert.True(GuideButtonConflicts.GameBarUsesButton(1, installed: true));
            Assert.False(GuideButtonConflicts.GameBarUsesButton(0, installed: true));
            Assert.False(GuideButtonConflicts.GameBarUsesButton(null, installed: false));
        }

        [Fact]
        public void Steam_guide_option_is_read_from_localconfig_and_defaults_to_on()
        {
            const string off = "\"UserLocalConfigStore\"\n{\n\t\"system\"\n\t{\n\t\t\"Controller_CheckGuideButton\"\t\t\"0\"\n\t}\n}";
            const string on = "\"UserLocalConfigStore\"\n{\n\t\t\"Controller_CheckGuideButton\"\t\t\"1\"\n}";
            const string untouched = "\"UserLocalConfigStore\"\n{\n\t\"Deck_GuidedTourVersionSeen\"\t\t\"1\"\n}";

            Assert.False(GuideButtonConflicts.SteamGuideFocusesSteam(off));
            Assert.True(GuideButtonConflicts.SteamGuideFocusesSteam(on));
            Assert.True(GuideButtonConflicts.SteamGuideFocusesSteam(untouched));
            Assert.False(GuideButtonConflicts.SteamGuideFocusesSteam(null)); // Steam nunca aberta com uma conta
        }

        [Fact]
        public void Localconfig_is_the_logged_in_account_or_the_last_one_used()
        {
            using (var dir = new TempDir())
            {
                var userdata = dir.Combine("userdata");
                var older = dir.WriteText(Path.Combine("userdata", "111", "config", "localconfig.vdf"), "a");
                var newer = dir.WriteText(Path.Combine("userdata", "222", "config", "localconfig.vdf"), "b");
                File.SetLastWriteTimeUtc(older, T0);
                File.SetLastWriteTimeUtc(newer, T0.AddDays(1));

                Assert.Equal(older, SteamClient.PickLocalConfig(userdata, 111));
                Assert.Equal(newer, SteamClient.PickLocalConfig(userdata, 0));
                Assert.Equal(newer, SteamClient.PickLocalConfig(userdata, 999)); // conta sem pasta
                Assert.Null(SteamClient.PickLocalConfig(dir.Combine("nada"), 111));
            }
        }
    }
}
