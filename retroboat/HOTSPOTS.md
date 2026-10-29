# Arquivos quentes do upstream

Foto tirada em 24/10/2026 com `retroboat/tools/hotspots.sh` (janela de 12 meses, sem commits de merge).
Serve para estimar a **probabilidade de conflito** de qualquer mudança nossa: editar um arquivo ALTA
significa resolver conflito com frequência; editar um arquivo baixa quase nunca dá trabalho.

Regenere a cada poucos meses ou antes de decidir onde implementar uma mudança grande:

```bash
bash retroboat/tools/hotspots.sh                                  # este repo (retrobat)
bash retroboat/tools/hotspots.sh -C ../emulatorlauncher -r upstream/master
bash retroboat/tools/hotspots.sh -C ../emulationstation -r upstream/master -x '^locale/'
```

## retrobat (este repositório) — configs, templates, mapeamentos

_324 commits (sem merges) em `upstream/main` desde "12 months ago". Calor: ALTA >= 15 alterações, média 4-14, baixa <= 3._

| Alterações | % dos commits | Calor | Arquivo |
|---:|---:|---|---|
| 85 | 26% | ALTA | `system/templates/emulationstation/es_systems.cfg` |
| 46 | 14% | ALTA | `system/templates/emulationstation/es_padtokey.cfg` |
| 40 | 12% | ALTA | `system/configgen/templates_files.lst` |
| 37 | 11% | ALTA | `CHANGELOG.md` |
| 31 | 10% | ALTA | `build.ini` |
| 27 | 8% | ALTA | `system/configgen/retrobat_tree.lst` |
| 23 | 7% | ALTA | `system/resources/inputmapping/teknoparrot.yml` |
| 23 | 7% | ALTA | `system/configgen/systems_names.lst` |
| 22 | 7% | ALTA | `system/configgen/emulators_names.lst` |
| 19 | 6% | ALTA | `bios` |
| 18 | 6% | ALTA | `system/templates/retroarch/retroarch-core-options.cfg` |
| 18 | 6% | ALTA | `system/templates/emulationstation/es_features.locale/fr/es-features.po` |
| 15 | 5% | ALTA | `system/upgrade.xml` |
| 15 | 5% | ALTA | `system/es_menu/gamelist.xml` |
| 15 | 5% | ALTA | `system/configgen/lrcores_names.lst` |
| 15 | 5% | ALTA | `InstallerHost.exe` |
| 7 | 2% | média | `system/templates/rpcs3/config/config.yml` |
| 7 | 2% | média | `system/templates/citron/user/config/qt-config.ini` |
| 7 | 2% | média | `system/tattoos` |
| 7 | 2% | média | `RetroBuild.exe` |
| 6 | 2% | média | `system/templates/emulationstation/kbhotkeysdics.json` |
| 5 | 2% | média | `system/tools/triforce_patches.json` |
| 5 | 2% | média | `system/templates/xenia-edge/xenia-edge.config.toml` |
| 5 | 2% | média | `system/templates/azahar/user/config/qt-config.ini` |
| 5 | 2% | média | `system/resources/inputmapping/teknoparrot_wheel_mapping.yml` |
| 5 | 2% | média | `system/resources/inputmapping/mdControllers.json` |
| 5 | 2% | média | `system/resources/inputmapping/libretro_mame.yml` |
| 5 | 2% | média | `README.md` |
| 4 | 1% | média | `system/templates/shadps4/user/config.toml` |
| 4 | 1% | média | `system/templates/eden/user/config/qt-config.ini` |

Leitura: `es_systems.cfg` muda em 1 de cada 4 commits do upstream. Qualquer edição direta nele vai
conflitar quase toda sincronização; por isso mudanças de sistemas vão para um overlay
`es_systems_retroboat.cfg` (ver FORK.md, regra 1). `build.ini` é quente, mas só nas chaves
`retrobat_version` (57 alterações) e `branch` (17); as linhas de URL mudaram 3 vezes no ano.

## emulatorlauncher — geração de config dos emuladores, controles, es_features, atualizador

_657 commits (sem merges) em `origin/master` desde "12 months ago". Calor: ALTA >= 15 alterações, média 4-14, baixa <= 3._

| Alterações | % dos commits | Calor | Arquivo |
|---:|---:|---|---|
| 238 | 36% | ALTA | `.emulationstation/es_features.cfg` |
| 72 | 11% | ALTA | `emulatorLauncher/Generators/LibRetro.CoreOptions.cs` |
| 53 | 8% | ALTA | `emulatorLauncher/Generators/LibRetro.Generator.cs` |
| 50 | 8% | ALTA | `emulatorLauncher/Program.cs` |
| 44 | 7% | ALTA | `emulatorLauncher/Installer.cs` |
| 40 | 6% | ALTA | `emulatorLauncher/Generators/Dolphin.Generator.cs` |
| 30 | 5% | ALTA | `emulatorLauncher/emulatorLauncher.csproj` |
| 30 | 5% | ALTA | `emulatorLauncher/Generators/PortsLauncher.Configuration.cs` |
| 27 | 4% | ALTA | `emulatorLauncher/Generators/Pcsx2.Generator.cs` |
| 26 | 4% | ALTA | `emulatorLauncher/Generators/Pcsx2.Controllers.cs` |
| 25 | 4% | ALTA | `emulatorLauncher/Generators/Mame64.Controllers.cs` |
| 24 | 4% | ALTA | `emulatorLauncher/Generators/Rpcs3.Generator.cs` |
| 23 | 4% | ALTA | `emulatorLauncher/Generators/ExeLauncher.Generator.cs` |
| 23 | 4% | ALTA | `emulatorLauncher/Common/Generator.cs` |
| 22 | 3% | ALTA | `emulatorLauncher/Generators/Libretro.Controllers.cs` |
| 21 | 3% | ALTA | `emulatorLauncher/Generators/Libretro.InputRemap.cs` |
| 19 | 3% | ALTA | `emulatorLauncher/Generators/Mame64.Generator.cs` |
| 19 | 3% | ALTA | `emulatorLauncher/Generators/Eden.Generator.cs` |
| 19 | 3% | ALTA | `emulatorLauncher/Bezels/BezelFiles.Tattoos.cs` |
| 18 | 3% | ALTA | `emulatorLauncher/Generators/Xenia.Generator.cs` |
| 18 | 3% | ALTA | `emulatorLauncher/Generators/Ryujinx.Generator.cs` |
| 18 | 3% | ALTA | `batocera-systems/Resources/batocera-systems.json` |
| 17 | 3% | ALTA | `emulatorLauncher/Generators/TeknoParrot.generator.cs` |
| 17 | 3% | ALTA | `emulatorLauncher/Generators/Ares.Generator.cs` |
| 16 | 2% | ALTA | `emulatorLauncher/Generators/Citron.Generator.cs` |
| 15 | 2% | ALTA | `emulatorLauncher/Generators/PortsLauncher.Generator.cs` |
| 14 | 2% | média | `emulatorLauncher/Generators/Ryujinx.Controllers.cs` |
| 14 | 2% | média | `emulatorLauncher/ControlCenter/ControlCenterFrm.cs` |
| 14 | 2% | média | `emulatorLauncher/Common/HotkeysDictionaries.cs` |
| 14 | 2% | média | `EmulatorLauncher.Common/EmulatorLauncher.Common.csproj` |

Leitura: é o repositório mais ativo (cerca de 13 commits por semana, quase todos de um único
mantenedor). `es_features.cfg` muda em mais de um terço dos commits: mudanças de opções de menu vão
para o overlay `es_features_retroboat.cfg`. Nos geradores C#, prefira classes novas com um gancho
de uma linha a editar o meio de `LibRetro.Generator.cs` / `Program.cs`.

## EmulationStation (batocera-linux/batocera-emulationstation, sem `locale/`)

_398 commits (sem merges) em `batocera/master` desde "12 months ago". Calor: ALTA >= 15 alterações, média 4-14, baixa <= 3._

| Alterações | % dos commits | Calor | Arquivo |
|---:|---:|---|---|
| 43 | 11% | ALTA | `es-app/src/guis/GuiMenu.cpp` |
| 41 | 10% | ALTA | `es-app/src/ApiSystem.cpp` |
| 18 | 5% | ALTA | `es-app/src/ApiSystem.h` |
| 17 | 4% | ALTA | `es-core/src/Window.cpp` |
| 17 | 4% | ALTA | `es-app/src/SystemData.cpp` |
| 16 | 4% | ALTA | `es-app/src/main.cpp` |
| 13 | 3% | média | `es-core/src/ThemeData.cpp` |
| 13 | 3% | média | `es-core/src/Settings.cpp` |
| 12 | 3% | média | `resources/gamesdb.xml` |
| 12 | 3% | média | `es-core/src/utils/FileSystemUtil.cpp` |
| 11 | 3% | média | `es-app/src/guis/GuiControllersSettings.cpp` |
| 11 | 3% | média | `es-app/src/FileData.cpp` |
| 10 | 3% | média | `es-core/src/renderers/Renderer.cpp` |
| 10 | 3% | média | `es-core/src/InputManager.cpp` |
| 10 | 3% | média | `es-app/src/components/ScraperSearchComponent.cpp` |
| 10 | 3% | média | `es-app/src/MetaData.cpp` |
| 9 | 2% | média | `es-app/src/views/ViewController.cpp` |
| 9 | 2% | média | `es-app/src/services/HttpServerThread.cpp` |
| 8 | 2% | média | `es-core/src/utils/Platform.cpp` |
| 8 | 2% | média | `es-core/src/resources/Font.cpp` |
| 8 | 2% | média | `CMakeLists.txt` |
| 7 | 2% | média | `es-core/src/resources/TextureDataManager.cpp` |
| 7 | 2% | média | `es-app/src/Win32ApiSystem.cpp` |
| 7 | 2% | média | `es-app/src/PlatformId.cpp` |
| 6 | 2% | média | `es-core/src/Settings.h` |
| 6 | 2% | média | `es-core/src/GunManager.cpp` |
| 6 | 2% | média | `es-core/src/AudioManager.cpp` |
| 6 | 2% | média | `es-app/src/views/SystemView.cpp` |
| 6 | 2% | média | `es-app/src/scrapers/ScreenScraper.cpp` |
| 6 | 2% | média | `es-app/src/guis/GuiKeyboardtopads.cpp` |

Leitura: `GuiMenu.cpp` e `ApiSystem.cpp` são os pontos mais disputados. Mudanças específicas de
Windows costumam cair em `Win32ApiSystem.cpp` (7 alterações no ano), que é bem mais calmo.

## retrobat-executable — retrobat.exe (inicialização, splash, foco)

_34 commits (sem merges) em `origin/main` desde "12 months ago". Calor: ALTA >= 15 alterações, média 4-14, baixa <= 3._

| Alterações | % dos commits | Calor | Arquivo |
|---:|---:|---|---|
| 20 | 59% | ALTA | `RetroBat/RetroBat/Program.cs` |
| 8 | 24% | média | `RetroBat/RetroBat/Properties/AssemblyInfo.cs` |
| 8 | 24% | média | `RetroBat/RetroBat/IniReader.cs` |
| 5 | 15% | média | `RetroBat/RetroBat/VideoPlayerForm.cs` |
| 5 | 15% | média | `RetroBat/RetroBat/SplashVideo.cs` |
| 5 | 15% | média | `RetroBat/RetroBat/RetroBat.csproj` |
| 5 | 15% | média | `RetroBat/RetroBat/EmulationStationLauncher.cs` |
| 4 | 12% | média | `RetroBat/RetroBat/resources/launcher.ico` |
| 4 | 12% | média | `RetroBat/RetroBat/RetroBatConfigLoader.cs` |
| 2 | 6% | baixa | `RetroBat/RetroBat/app.manifest` |
| 2 | 6% | baixa | `RetroBat/RetroBat/FocusHelper.cs` |
| 2 | 6% | baixa | `README.md` |
| 1 | 3% | baixa | `RetroBat/RetroBat/packages.config` |
| 1 | 3% | baixa | `RetroBat/RetroBat/WindowsInterfaceManager.cs` |
| 1 | 3% | baixa | `RetroBat/RetroBat/StartupFileChecker.cs` |

Leitura: repositório pequeno, mas quase tudo passa por `Program.cs`.
