using System;
using System.Runtime.InteropServices;

namespace Retroboat.Launcher.SteamSwitch
{
    /// <summary>
    /// O botão Xbox (Guia) dos controles XInput. O <c>XInputGetState</c> público não mostra esse botão; a função 100 da
    /// mesma DLL (a "Ex", sem nome) mostra, e é a que o SDL e a Steam usam. Funciona com qualquer janela na frente.
    ///
    /// Para não pesar em nada: lê só os controles ligados, 20 vezes por segundo; os encaixes vazios são olhados de novo a
    /// cada 2 s (no XInput, consultar um encaixe vazio custa bem mais que um ligado).
    /// </summary>
    sealed class GuideButton
    {
        const ushort GuideMask = 0x0400;
        const uint ErrorSuccess = 0;
        static readonly TimeSpan RescanEvery = TimeSpan.FromSeconds(2);

        readonly bool[] _connected = new bool[4];
        DateTime _nextScan = DateTime.MinValue;
        bool _wasDown;
        bool _use13;

        /// <summary>Sem XInput com a função 100 neste Windows: o botão nunca é lido.</summary>
        public bool Unavailable { get; private set; }

        /// <summary>true só na volta em que o botão desceu (soltar e apertar de novo conta outra vez).</summary>
        public bool Pressed(DateTime now)
        {
            bool down = IsDown(now);
            bool pressed = down && !_wasDown;
            _wasDown = down;
            return pressed;
        }

        bool IsDown(DateTime now)
        {
            if (Unavailable)
                return false;

            bool scan = now >= _nextScan;
            if (scan)
                _nextScan = now + RescanEvery;

            bool down = false;
            for (uint index = 0; index < 4; index++)
            {
                if (!_connected[index] && !scan)
                    continue;

                _connected[index] = TryGetState(index, out var state);
                if (_connected[index] && (state.Gamepad.Buttons & GuideMask) != 0)
                    down = true;
            }

            return down;
        }

        bool TryGetState(uint index, out XInputState state)
        {
            state = default;
            try
            {
                return (_use13 ? GetStateEx13(index, out state) : GetStateEx14(index, out state)) == ErrorSuccess;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                // O xinput1_4 vem no Windows 8 em diante; o 1_3 vem com o DirectX. Sem nenhum dos dois, fica sem o botão.
                if (_use13)
                    Unavailable = true;
                else
                    _use13 = true;

                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XInputGamepad
        {
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX;
            public short ThumbLY;
            public short ThumbRX;
            public short ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XInputState
        {
            public uint PacketNumber;
            public XInputGamepad Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "#100")]
        static extern uint GetStateEx14(uint userIndex, out XInputState state);

        [DllImport("xinput1_3.dll", EntryPoint = "#100")]
        static extern uint GetStateEx13(uint userIndex, out XInputState state);
    }
}
