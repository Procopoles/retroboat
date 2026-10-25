using System;
using System.Runtime.InteropServices;

namespace Retroboat.Launch
{
    /// <summary>Lê os botões dos controles XInput (Xbox e compatíveis), para usar as telas só com o controle.</summary>
    static class GamepadButtons
    {
        public const ushort DpadUp = 0x0001;
        public const ushort DpadDown = 0x0002;
        public const ushort A = 0x1000;
        public const ushort B = 0x2000;

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

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        static extern uint GetState14(uint userIndex, out XInputState state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        static extern uint GetState910(uint userIndex, out XInputState state);

        static bool _use910;
        static bool _unavailable;

        /// <summary>Algum dos 4 controles está com o botão pressionado?</summary>
        public static bool IsPressed(ushort button)
        {
            if (_unavailable)
                return false;

            for (uint index = 0; index < 4; index++)
            {
                if (TryGetState(index, out var state) && (state.Gamepad.Buttons & button) != 0)
                    return true;
            }

            return false;
        }

        /// <summary>Botões pressionados agora, somando os 4 controles.</summary>
        public static ushort Current()
        {
            ushort buttons = 0;
            if (_unavailable)
                return buttons;

            for (uint index = 0; index < 4; index++)
            {
                if (TryGetState(index, out var state))
                    buttons |= state.Gamepad.Buttons;
            }

            return buttons;
        }

        static bool TryGetState(uint index, out XInputState state)
        {
            state = default;
            try
            {
                return (_use910 ? GetState910(index, out state) : GetState14(index, out state)) == 0;
            }
            catch (DllNotFoundException)
            {
                if (_use910)
                    _unavailable = true;
                else
                    _use910 = true;

                return false;
            }
            catch (EntryPointNotFoundException)
            {
                _unavailable = true;
                return false;
            }
        }
    }
}
