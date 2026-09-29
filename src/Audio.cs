using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace StatusBarWin
{
    /// <summary>Master volume, mute and output device through Core Audio - no Windows UI involved.</summary>
    static class Audio
    {
        public class Device
        {
            public string Id;
            public string Name;
            public bool IsDefault;
        }

        static readonly Guid Ctx = Guid.Empty;

        static IMMDeviceEnumerator Enumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();

        static IAudioEndpointVolume Volume()
        {
            Enumerator().GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out var dev);
            var iid = typeof(IAudioEndpointVolume).GUID;
            dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out object o);
            return (IAudioEndpointVolume)o;
        }

        /// <summary>False when there is no output device at all.</summary>
        public static bool TryGet(out int level, out bool mute)
        {
            level = 0; mute = false;
            try
            {
                var v = Volume();
                v.GetMasterVolumeLevelScalar(out float f);
                v.GetMute(out mute);
                level = (int)Math.Round(f * 100);
                return true;
            }
            catch { return false; }
        }

        public static void SetLevel(int pct)
        {
            try
            {
                var v = Volume();
                var g = Ctx;
                v.SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(100, pct)) / 100f, ref g);
            }
            catch { }
        }

        public static void SetMute(bool mute)
        {
            try { var g = Ctx; Volume().SetMute(mute, ref g); } catch { }
        }

        public static List<Device> Devices()
        {
            var list = new List<Device>();
            try
            {
                var en = Enumerator();
                string defId = null;
                try { en.GetDefaultAudioEndpoint(0, 1, out var d); d.GetId(out defId); } catch { }
                en.EnumAudioEndpoints(0, 1 /*DEVICE_STATE_ACTIVE*/, out var col);
                col.GetCount(out uint n);
                for (uint i = 0; i < n; i++)
                {
                    col.Item(i, out var dev);
                    dev.GetId(out string id);
                    list.Add(new Device { Id = id, Name = FriendlyName(dev) ?? id, IsDefault = id == defId });
                }
            }
            catch { }
            return list;
        }

        static string FriendlyName(IMMDevice dev)
        {
            try
            {
                dev.OpenPropertyStore(0 /*STGM_READ*/, out var ps);
                var key = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                ps.GetValue(ref key, out var pv);
                string s = pv.vt == 31 /*VT_LPWSTR*/ ? Marshal.PtrToStringUni(pv.p) : null;
                PropVariantClear(ref pv);
                return s;
            }
            catch { return null; }
        }

        public static bool SetDefault(string id)
        {
            try
            {
                var pc = (IPolicyConfig)new PolicyConfigCo();
                for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role);
                return true;
            }
            catch { return false; }
        }

        // ---- interop ----

        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pv);

        [StructLayout(LayoutKind.Sequential)]
        struct PROPERTYKEY { public Guid fmtid; public int pid; }

        [StructLayout(LayoutKind.Sequential)]
        struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p; public IntPtr p2; }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }
        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigCo { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            void EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            void GetCount(out uint count);
            void Item(uint index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            void OpenPropertyStore(int access, out IPropertyStore store);
            void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            void GetState(out int state);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            void GetCount(out int count);
            void GetAt(int index, out PROPERTYKEY key);
            void GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            void RegisterControlChangeNotify(IntPtr notify);
            void UnregisterControlChangeNotify(IntPtr notify);
            void GetChannelCount(out uint count);
            void SetMasterVolumeLevel(float db, ref Guid ctx);
            void SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            void GetMasterVolumeLevel(out float db);
            void GetMasterVolumeLevelScalar(out float level);
            void SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
            void SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
            void GetChannelVolumeLevel(uint ch, out float db);
            void GetChannelVolumeLevelScalar(uint ch, out float level);
            void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        // undocumented but stable since Windows 7; the same call the sound settings make
        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            void GetMixFormat(IntPtr a, IntPtr b);
            void GetDeviceFormat(IntPtr a, int b, IntPtr c);
            void ResetDeviceFormat(IntPtr a);
            void SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
            void GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
            void SetProcessingPeriod(IntPtr a, IntPtr b);
            void GetShareMode(IntPtr a, IntPtr b);
            void SetShareMode(IntPtr a, IntPtr b);
            void GetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
            void SetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
            void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
            void SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
        }
    }
}
