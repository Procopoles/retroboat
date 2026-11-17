using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Retroboat.SteamSwitch;

namespace Retroboat.Launcher.SteamSwitch
{
    /// <summary>As janelas da troca: qual está na frente, minimizar o ES sem tirar o foco de ninguém e trazê-lo de volta.</summary>
    static class SwitchWindows
    {
        const string EsProcess = "emulationstation";

        /// <summary>O que está na frente agora; <paramref name="window"/> é a janela.</summary>
        public static SwitchScreen Front(out IntPtr window)
        {
            window = GetForegroundWindow();
            if (window == IntPtr.Zero)
                return SwitchScreen.Other;

            GetWindowThreadProcessId(window, out uint pid);
            var path = ProcessPath(pid);
            var name = path != null ? Path.GetFileNameWithoutExtension(path) : null;

            if (string.Equals(name, EsProcess, StringComparison.OrdinalIgnoreCase))
                return SwitchScreen.Es;

            if (string.Equals(name, "steamwebhelper", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "steam", StringComparison.OrdinalIgnoreCase))
                return IsBigPicture(window) ? SwitchScreen.BigPicture : SwitchScreen.Other;

            return SteamClient.IsSteamGame(path) ? SwitchScreen.SteamGame : SwitchScreen.Other;
        }

        /// <summary>
        /// "Steam Big Picture Mode", "Steam — Modo Big Picture": o nome fica em quase todas as línguas. Numa língua que
        /// traduz o nome, a janela da Steam ocupando a tela inteira também é o Big Picture (o cliente normal não fica assim).
        /// </summary>
        static bool IsBigPicture(IntPtr window)
        {
            var title = new StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            if (title.ToString().IndexOf("Big Picture", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!GetWindowRect(window, out var rect))
                return false;

            var screen = Screen.FromHandle(window).Bounds;
            return rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
        }

        /// <summary>O processo do ES, ou nulo; quem chama descarta.</summary>
        public static Process FindEsProcess()
        {
            var processes = Process.GetProcessesByName(EsProcess);
            for (int i = 1; i < processes.Length; i++)
                processes[i].Dispose();
            return processes.Length > 0 ? processes[0] : null;
        }

        /// <summary>A janela do ES, mesmo minimizada; zero se o ES não está aberto.</summary>
        public static IntPtr FindEs()
        {
            var processes = Process.GetProcessesByName(EsProcess);
            try
            {
                return processes.Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
            }
            finally
            {
                foreach (var p in processes)
                    p.Dispose();
            }
        }

        /// <summary>Uma janela da Steam que não é o Big Picture (a janela normal do cliente).</summary>
        public static bool IsSteamWindow(IntPtr window)
        {
            if (window == IntPtr.Zero)
                return false;

            GetWindowThreadProcessId(window, out uint pid);
            var name = ProcessName(pid);
            bool steam = string.Equals(name, "steamwebhelper", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "steam", StringComparison.OrdinalIgnoreCase);
            return steam && !IsBigPicture(window);
        }

        /// <summary>A janela foi destruída. O Big Picture é destruído ao fechar; com um jogo da Steam aberto, continua lá.</summary>
        public static bool IsGone(IntPtr window) => window == IntPtr.Zero || !IsWindow(window);

        public static bool IsMinimized(IntPtr window) => window != IntPtr.Zero && IsIconic(window);

        /// <summary>Minimiza sem ativar outra janela: o Big Picture continua com o foco. Minimizado, o ES dorme.</summary>
        public static void MinimizeQuietly(IntPtr window)
        {
            if (window != IntPtr.Zero && IsWindow(window))
                ShowWindow(window, SW_SHOWMINNOACTIVE);
        }

        /// <summary>
        /// Restaura e põe na frente. O Windows deixa um programa em segundo plano fazer isso só em alguns casos; quando
        /// recusa, um Alt de mentira antes conta como "o usuário mexeu", e aí deixa (o mesmo que o ES faz no activateWindow).
        /// </summary>
        public static bool BringBack(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window))
                return false;

            if (IsIconic(window))
                ShowWindow(window, SW_RESTORE);

            SetForegroundWindow(window);
            if (GetForegroundWindow() == window)
                return true;

            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            SetForegroundWindow(window);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            return GetForegroundWindow() == window;
        }

        static string ProcessName(uint pid)
        {
            var path = ProcessPath(pid);
            return path != null ? Path.GetFileNameWithoutExtension(path) : null;
        }

        /// <summary>O executável do processo, ou nulo (processo de outro usuário ou já fechado).</summary>
        static string ProcessPath(uint pid)
        {
            var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero)
                return null;

            try
            {
                var path = new StringBuilder(1024);
                int size = path.Capacity;
                return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : null;
            }
            finally
            {
                CloseHandle(process);
            }
        }

        const int SW_SHOWMINNOACTIVE = 7;
        const int SW_RESTORE = 9;
        const byte VK_MENU = 0x12;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
    }
}
