using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Retroboat.Settings
{
    /// <summary>
    /// RB-030: as saídas de som do Windows. O ES e os emuladores tocam na saída padrão do Windows, então trocar a padrão leva
    /// os dois juntos. A escolha fica em [Audio] Output no retroboat.ini, e o Retroboat.exe volta para ela ao abrir: o Windows
    /// troca sozinho para o som de um monitor ou TV que acabou de ser ligado.
    /// </summary>
    public static class AudioOutputs
    {
        public sealed class Output
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public bool IsDefault { get; set; }
        }

        /// <summary>As saídas ligadas agora, na ordem do Windows.</summary>
        public static List<Output> List()
        {
            var outputs = new List<Output>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            string defaultId = DefaultId(enumerator);
            if (enumerator.EnumAudioEndpoints(RenderFlow, ActiveState, out var devices) != 0)
                return outputs;

            devices.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                if (devices.Item(i, out var device) != 0 || device.GetId(out var id) != 0)
                    continue;

                outputs.Add(new Output { Id = id, Name = FriendlyName(device) ?? id, IsDefault = id == defaultId });
            }

            return outputs;
        }

        /// <summary>
        /// Faz de <paramref name="id"/> a saída padrão do Windows (a de som em geral; a de chamadas fica como está).
        /// </summary>
        /// <returns>false se essa saída não está ligada agora.</returns>
        public static bool SetDefault(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !List().Exists(o => o.Id == id))
                return false;

            var policy = (IPolicyConfig)new PolicyConfigClient();
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, ConsoleRole));
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, MultimediaRole));
            return true;
        }

        /// <summary>
        /// Ao abrir o Retroboat: volta para a saída escolhida no menu do ES, se ela estiver ligada e não for a padrão.
        /// </summary>
        /// <returns>A linha para o log; null se não havia nada a fazer.</returns>
        public static string ApplyChosen(RetroboatSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.AudioOutput))
                return null;

            var outputs = List();
            var chosen = outputs.Find(o => o.Id == settings.AudioOutput);
            if (chosen == null)
                return "Saída de som escolhida não está ligada; fica a do Windows.";

            if (chosen.IsDefault)
                return null;

            var current = outputs.Find(o => o.IsDefault);
            SetDefault(chosen.Id);
            return "Saída de som de volta para " + chosen.Name + " (o Windows estava em " + (current?.Name ?? "nenhuma") + ").";
        }

        static string DefaultId(IMMDeviceEnumerator enumerator)
        {
            if (enumerator.GetDefaultAudioEndpoint(RenderFlow, ConsoleRole, out var device) != 0 || device == null)
                return null;

            return device.GetId(out var id) == 0 ? id : null;
        }

        static string FriendlyName(IMMDevice device)
        {
            if (device.OpenPropertyStore(0, out var store) != 0)
                return null;

            var key = new PropertyKey { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), PropertyId = 14 };
            IntPtr value = Marshal.AllocCoTaskMem(32);
            try
            {
                for (int i = 0; i < 32; i++)
                    Marshal.WriteByte(value, i, 0);

                if (store.GetValue(ref key, value) != 0 || Marshal.ReadInt16(value) != 31) // VT_LPWSTR
                    return null;

                return Marshal.PtrToStringUni(Marshal.ReadIntPtr(value, 8));
            }
            finally
            {
                PropVariantClear(value);
                Marshal.FreeCoTaskMem(value);
            }
        }

        const int RenderFlow = 0;     // eRender
        const int ActiveState = 1;    // DEVICE_STATE_ACTIVE
        const int ConsoleRole = 0;    // eConsole
        const int MultimediaRole = 1; // eMultimedia

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(IntPtr value);

        [StructLayout(LayoutKind.Sequential)]
        struct PropertyKey
        {
            public Guid FormatId;
            public int PropertyId;
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, IntPtr value);
        }

        // Interface não documentada do Windows (Vista ao 11) que o painel de som usa para trocar a padrão; a mesma de
        // ferramentas como o SoundSwitch e o AudioDeviceCmdlets. A ordem dos métodos é a da vtable: só SetDefaultEndpoint
        // é chamado.
        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        class PolicyConfigClient { }

        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr format);
            [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device, int useDefault, IntPtr format);
            [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device);
            [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr endpointFormat, IntPtr mixFormat);
            [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string device, int useDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
            [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr period);
            [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr mode);
            [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr mode);
            [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr key, IntPtr value);
            [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr key, IntPtr value);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string device, int role);
            [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string device, int visible);
        }
    }
}
