using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VirtualSystemBar
{
    /// <summary>External drives and "safely remove" without the Windows dialog.</summary>
    static class Usb
    {
        public class Drive
        {
            public string Root;     // "E:\"
            public string Label;
        }

        public static List<Drive> List()
        {
            var list = new List<Drive>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Removable && d.DriveType != DriveType.Fixed) continue;
                    if (d.DriveType == DriveType.Fixed && !IsExternalBus(d.Name)) continue;
                    string letter = d.Name.Substring(0, 2);
                    string label = d.IsReady && !string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.VolumeLabel : "כונן נשלף";
                    list.Add(new Drive { Root = d.Name, Label = label + " (" + letter + ")" });
                }
                catch { }
            }
            return list;
        }

        static bool IsExternalBus(string root)
        {
            using (var h = OpenVolume(root))
            {
                if (h.IsInvalid) return false;
                var q = new byte[12];   // STORAGE_PROPERTY_QUERY { StorageDeviceProperty, PropertyStandardQuery }
                var buf = new byte[1024];
                if (!DeviceIoControl(h, 0x2D1400, q, q.Length, buf, buf.Length, out _, IntPtr.Zero)) return false;
                uint bus = BitConverter.ToUInt32(buf, 28);
                return bus == 7 /*USB*/ || bus == 0x0C /*SD*/ || bus == 0x0D /*MMC*/;
            }
        }

        /// <summary>Null on success, else a message (e.g. the drive is in use).</summary>
        public static string Eject(Drive d)
        {
            int number;
            using (var h = OpenVolume(d.Root))
            {
                if (h.IsInvalid) return "הכונן כבר לא מחובר";
                var buf = new byte[12];
                if (!DeviceIoControl(h, 0x2D1080 /*IOCTL_STORAGE_GET_DEVICE_NUMBER*/, null, 0, buf, buf.Length, out _, IntPtr.Zero))
                    return "לא ניתן לזהות את הכונן";
                number = BitConverter.ToInt32(buf, 4);
            }
            uint devInst = DiskDevInst(number);
            if (devInst == 0) return "לא ניתן לזהות את הכונן";
            if (CM_Get_Parent(out uint parent, devInst, 0) != 0) parent = devInst;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                // a non-null veto buffer means Windows shows no UI of its own
                var veto = new StringBuilder(260);
                int r = CM_Request_Device_EjectW(parent, out int vetoType, veto, veto.Capacity, 0);
                if (r == 0 && vetoType == 0) return null;
                Thread.Sleep(400);
            }
            return "הכונן בשימוש. סגור קבצים פתוחים ונסה שוב";
        }

        static Microsoft.Win32.SafeHandles.SafeFileHandle OpenVolume(string root) =>
            CreateFile(@"\\.\" + root.Substring(0, 2), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);

        static uint DiskDevInst(int number)
        {
            var disk = new Guid("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
            IntPtr set = SetupDiGetClassDevs(ref disk, IntPtr.Zero, IntPtr.Zero, 0x12 /*PRESENT|DEVICEINTERFACE*/);
            if (set == new IntPtr(-1)) return 0;
            try
            {
                for (uint i = 0; ; i++)
                {
                    var ifd = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref disk, i, ref ifd)) return 0;
                    var dev = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    IntPtr detail = Marshal.AllocHGlobal(2048);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref ifd, detail, 2048, out _, ref dev)) continue;
                        string path = Marshal.PtrToStringUni(detail + 4);
                        using (var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                        {
                            if (h.IsInvalid) continue;
                            var buf = new byte[12];
                            if (DeviceIoControl(h, 0x2D1080, null, 0, buf, buf.Length, out _, IntPtr.Zero)
                                && BitConverter.ToInt32(buf, 4) == number)
                                return dev.DevInst;
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public uint cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint size, out uint required, ref SP_DEVINFO_DATA dev);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll")] static extern int CM_Get_Parent(out uint parent, uint devInst, int flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        static extern int CM_Request_Device_EjectW(uint devInst, out int vetoType, StringBuilder vetoName, int nameLength, int flags);
    }

    /// <summary>Input language of the kiosk app's window, and the Windows touch keyboard.</summary>
    static class Keyboard
    {
        public static string Abbrev(CultureInfo c)
        {
            if (c == null) return "?";
            if (c.TwoLetterISOLanguageName == "he") return "עבר";
            return c.ThreeLetterISOLanguageName.ToUpperInvariant();
        }

        public static InputLanguage Current(IntPtr target)
        {
            IntPtr hkl = GetKeyboardLayout(GetWindowThreadProcessId(target, out _));
            foreach (InputLanguage l in InputLanguage.InstalledInputLanguages)
                if (l.Handle == hkl) return l;
            foreach (InputLanguage l in InputLanguage.InstalledInputLanguages)
                if (((long)l.Handle & 0xFFFF) == ((long)hkl & 0xFFFF)) return l;
            return InputLanguage.CurrentInputLanguage;
        }

        public static void Switch(IntPtr target, InputLanguage lang)
        {
            if (target == IntPtr.Zero || lang == null) return;
            PostMessage(target, 0x0050 /*WM_INPUTLANGCHANGEREQUEST*/, IntPtr.Zero, lang.Handle);
        }

        public static void Next(IntPtr target)
        {
            var all = InputLanguage.InstalledInputLanguages;
            if (all.Count < 2) return;
            var cur = Current(target);
            int i = 0;
            for (; i < all.Count; i++) if (all[i].Handle == cur.Handle) break;
            Switch(target, all[(i + 1) % all.Count]);
        }

        /// <summary>Shows/hides the touch keyboard. Only TabTip is used - never osk.exe, whose Options link opens Control Panel.</summary>
        public static string ToggleTouch()
        {
            if (TryToggle()) return null;
            string tabtip = Path.Combine(Environment.GetEnvironmentVariable("CommonProgramW6432")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), @"microsoft shared\ink\TabTip.exe");
            if (!File.Exists(tabtip)) return "מקלדת המגע לא מותקנת במחשב הזה";
            try
            {
                // starting TabTip brings the keyboard up by itself
                Process.Start(new ProcessStartInfo(tabtip) { UseShellExecute = true });
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        static bool TryToggle()
        {
            try
            {
                var t = Type.GetTypeFromCLSID(new Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376"));
                var tip = (ITipInvocation)Activator.CreateInstance(t);
                tip.Toggle(GetDesktopWindow());
                Marshal.ReleaseComObject(tip);
                return true;
            }
            catch { return false; }
        }

        [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITipInvocation { void Toggle(IntPtr hwnd); }

        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    }

    /// <summary>
    /// The bar can't appear on the lock screen: Windows draws it on its own protected desktop. What it can do is
    /// switch on the Windows policy that removes the network button there, so nobody changes Wi-Fi without signing in.
    /// Writing the policy needs Windows admin rights, so a second copy of the EXE does it elevated (UAC).
    /// </summary>
    static class LockScreen
    {
        const string Key = @"SOFTWARE\Policies\Microsoft\Windows\System";
        const string Value = "DontDisplayNetworkSelectionUI";
        public const string Arg = "--lock-net";

        public static bool HideNetwork
        {
            get
            {
                try
                {
                    using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (var k = hk.OpenSubKey(Key))
                        return k?.GetValue(Value) is int v && v == 1;
                }
                catch { return false; }
            }
        }

        /// <summary>Runs inside the elevated copy. Exit code 0 = written.</summary>
        public static int Write(bool hide)
        {
            try
            {
                using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = hk.CreateSubKey(Key))
                {
                    if (hide) k.SetValue(Value, 1, RegistryValueKind.DWord);
                    else k.DeleteValue(Value, false);
                }
                return 0;
            }
            catch { return 1; }
        }

        /// <summary>Asks Windows for admin rights and applies the setting. Returns an error to show, or null.</summary>
        public static string Apply(bool hide)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(Program.ExePath, Arg + " " + (hide ? "1" : "0"))
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                p.WaitForExit();
                if (p.ExitCode != 0 || HideNetwork != hide) return "שינוי ההגדרה של מסך הנעילה נכשל.";
                return null;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return "ההגדרה של מסך הנעילה לא שונתה: לא אושרו הרשאות מנהל של Windows.";
            }
            catch (Exception ex) { return "שינוי ההגדרה של מסך הנעילה נכשל:\n" + ex.Message; }
        }
    }
}
