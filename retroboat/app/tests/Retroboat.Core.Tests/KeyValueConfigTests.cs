using Retroboat.Library;
using Xunit;

namespace Retroboat.Tests
{
    public class KeyValueConfigTests
    {
        const string Marker = "# Retroboat: pasta de jogos (RB-003)";
        const string Template = "# EmulationStation config files path\r\nhome=.\\.emulationstation\r\n\r\n# RetroArch Folders\r\nbios=.\\..\\bios\r\nsaves=.\\..\\saves\r\n";

        [Fact]
        public void Adds_managed_key_at_the_end()
        {
            var result = KeyValueConfig.SetManagedValue(Template, "roms", @"D:\Jogos", Marker);

            Assert.Equal(Template + Marker + "\r\nroms=D:\\Jogos\r\n", result);
        }

        [Fact]
        public void Replaces_existing_value_in_place_and_marks_it()
        {
            var content = "home=.\\.emulationstation\r\nroms=E:\\Velho\r\nsaves=.\\..\\saves\r\n";

            var result = KeyValueConfig.SetManagedValue(content, "roms", @"D:\Jogos", Marker);

            Assert.Equal("home=.\\.emulationstation\r\n" + Marker + "\r\nroms=D:\\Jogos\r\nsaves=.\\..\\saves\r\n", result);
        }

        [Fact]
        public void Removes_only_a_managed_key()
        {
            var managed = KeyValueConfig.SetManagedValue(Template, "roms", @"D:\Jogos", Marker);
            Assert.Equal(Template, KeyValueConfig.SetManagedValue(managed, "roms", null, Marker));

            var manual = Template + "roms=E:\\MeusJogos\r\n";
            Assert.Same(manual, KeyValueConfig.SetManagedValue(manual, "roms", null, Marker));
        }

        [Fact]
        public void Unchanged_value_returns_the_same_content()
        {
            var managed = KeyValueConfig.SetManagedValue(Template, "roms", @"D:\Jogos", Marker);

            Assert.Same(managed, KeyValueConfig.SetManagedValue(managed, "roms", @"D:\Jogos", Marker));
        }

        [Fact]
        public void Ignores_commented_keys_and_keeps_lf_endings()
        {
            var content = "#roms=X:\\comentado\nbios=.\\..\\bios\n";

            var result = KeyValueConfig.SetManagedValue(content, "roms", @"D:\Jogos", Marker);

            Assert.Equal(content + Marker + "\nroms=D:\\Jogos\n", result);
            Assert.Equal(@"D:\Jogos", KeyValueConfig.GetValue(result, "roms"));
        }
    }
}
