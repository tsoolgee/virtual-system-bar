using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace StatusBarWin
{
    static class Bt
    {
        public class Dev
        {
            public string Name;
            public bool Connected;
            public ulong Address;
            public uint ClassOfDevice;
            public DeviceInformation Info;
        }

        static readonly string[] Props = { "System.Devices.Aep.IsConnected", "System.Devices.Aep.DeviceAddress" };

        static string[] Selectors(bool paired) => new[]
        {
            BluetoothDevice.GetDeviceSelectorFromPairingState(paired),
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(paired),
        };

        public static async Task<List<Dev>> Paired()
        {
            await Radios.Init();
            var list = new List<Dev>();
            foreach (var sel in Selectors(true))
            {
                try
                {
                    foreach (var di in await DeviceInformation.FindAllAsync(sel, Props, DeviceInformationKind.AssociationEndpoint))
                    {
                        if (string.IsNullOrWhiteSpace(di.Name)) continue;
                        var d = new Dev { Name = di.Name, Info = di, Address = ParseAddress(di), Connected = Bool(di, "System.Devices.Aep.IsConnected") };
                        if (list.Any(x => x.Address != 0 && x.Address == d.Address)) continue;
                        FillClassic(d);
                        list.Add(d);
                    }
                }
                catch { }
            }
            return list.OrderByDescending(d => d.Connected).ThenBy(d => d.Name).ToList();
        }

        /// <summary>Watches for unpaired devices nearby. Dispose the result to stop.</summary>
        public static IDisposable Discover(Action<DeviceInformation> found)
        {
            var watchers = new List<DeviceWatcher>();
            foreach (var sel in Selectors(false))
            {
                try
                {
                    var w = DeviceInformation.CreateWatcher(sel, Props, DeviceInformationKind.AssociationEndpoint);
                    w.Added += (s, di) => { if (!string.IsNullOrWhiteSpace(di.Name)) found(di); };
                    w.Updated += (s, u) => { };
                    w.Start();
                    watchers.Add(w);
                }
                catch { }
            }
            return new Stopper(() =>
            {
                foreach (var w in watchers)
                    try { if (w.Status == DeviceWatcherStatus.Started || w.Status == DeviceWatcherStatus.EnumerationCompleted) w.Stop(); } catch { }
            });
        }

        sealed class Stopper : IDisposable
        {
            Action _a;
            public Stopper(Action a) { _a = a; }
            public void Dispose() { _a?.Invoke(); _a = null; }
        }

        /// <summary>
        /// ask(kind, pin) is called on a worker thread when the device needs the user:
        /// "confirm" (return non-null to accept), "show" (just display), "enter" (return the PIN).
        /// </summary>
        public static async Task<string> Pair(DeviceInformation di, Func<string, string, string> ask)
        {
            var custom = di.Pairing.Custom;
            void Handler(DeviceInformationCustomPairing s, DevicePairingRequestedEventArgs a)
            {
                var def = a.GetDeferral();
                try
                {
                    switch (a.PairingKind)
                    {
                        case DevicePairingKinds.ConfirmOnly:
                            a.Accept();
                            break;
                        case DevicePairingKinds.DisplayPin:
                            ask("show", a.Pin);
                            a.Accept();
                            break;
                        case DevicePairingKinds.ConfirmPinMatch:
                            if (ask("confirm", a.Pin) != null) a.Accept();
                            break;
                        case DevicePairingKinds.ProvidePin:
                            var pin = ask("enter", null);
                            if (!string.IsNullOrEmpty(pin)) a.Accept(pin);
                            break;
                    }
                }
                finally { def.Complete(); }
            }
            custom.PairingRequested += Handler;
            try
            {
                var r = await custom.PairAsync(DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin
                    | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.ProvidePin);
                switch (r.Status)
                {
                    case DevicePairingResultStatus.Paired:
                    case DevicePairingResultStatus.AlreadyPaired: return null;
                    case DevicePairingResultStatus.AuthenticationFailure:
                    case DevicePairingResultStatus.RejectedByHandler:
                    case DevicePairingResultStatus.PairingCanceled: return "הצימוד בוטל או שהקוד שגוי";
                    case DevicePairingResultStatus.AuthenticationTimeout:
                    case DevicePairingResultStatus.ConnectionRejected: return "ההתקן לא הגיב. ודא שהוא במצב צימוד";
                    default: return "הצימוד נכשל (" + r.Status + ")";
                }
            }
            catch (Exception ex) { return ex.Message; }
            finally { custom.PairingRequested -= Handler; }
        }

        public static async Task<string> Unpair(Dev d)
        {
            try
            {
                var r = await d.Info.Pairing.UnpairAsync();
                return r.Status == DeviceUnpairingResultStatus.Unpaired || r.Status == DeviceUnpairingResultStatus.AlreadyUnpaired
                    ? null : "ההסרה נכשלה (" + r.Status + ")";
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ---- connect / disconnect of a classic device: toggling its profile services, like the old Devices dialog ----

        static readonly Guid A2dpSink = new Guid("0000110B-0000-1000-8000-00805F9B34FB");
        static readonly Guid Handsfree = new Guid("0000111E-0000-1000-8000-00805F9B34FB");
        static readonly Guid Headset = new Guid("00001108-0000-1000-8000-00805F9B34FB");
        static readonly Guid Hid = new Guid("00001124-0000-1000-8000-00805F9B34FB");

        static List<Guid> ServicesFor(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info)
        {
            var known = new[] { A2dpSink, Handsfree, Headset, Hid };
            var result = new List<Guid>();
            uint n = 16;
            var buf = new Guid[16];
            if (BluetoothEnumerateInstalledServices(radio, ref info, ref n, buf) == 0)
                result.AddRange(buf.Take((int)n).Where(known.Contains));
            if (result.Count == 0)
            {
                // nothing installed right now (we disconnected it earlier) - pick by device class
                uint major = (info.ulClassofDevice >> 8) & 0x1F;
                if (major == 0x04) result.AddRange(new[] { A2dpSink, Handsfree });
                else if (major == 0x05) result.Add(Hid);
            }
            return result;
        }

        public static Task<string> SetConnected(Dev d, bool connect) => Task.Run(() =>
        {
            if (d.Address == 0) return "לא ניתן לשנות חיבור להתקן הזה";
            var fp = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
            IntPtr find = BluetoothFindFirstRadio(ref fp, out IntPtr radio);
            if (find == IntPtr.Zero) return "לא נמצא רכיב Bluetooth";
            try
            {
                var info = NewInfo(d.Address);
                BluetoothGetDeviceInfo(radio, ref info);
                var svcs = ServicesFor(radio, ref info);
                if (svcs.Count == 0) return "לא ניתן לשנות חיבור להתקן הזה";
                uint err = 0;
                foreach (var g in svcs)
                {
                    // connecting = disable + enable, which makes Windows reconnect the profile right away
                    var id = g;
                    uint e = BluetoothSetServiceState(radio, ref info, ref id, 0 /*disable*/);
                    if (connect) { id = g; e = BluetoothSetServiceState(radio, ref info, ref id, 1 /*enable*/); }
                    if (e != 0) err = e;
                }
                if (err == 5) return "נדרשות הרשאות מנהל לפעולה הזו";
                return connect && err != 0 ? "ההתחברות נכשלה. ודא שההתקן דלוק וקרוב" : null;
            }
            finally
            {
                CloseHandle(radio);
                BluetoothFindRadioClose(find);
            }
        });

        static void FillClassic(Dev d)
        {
            if (d.Address == 0) return;
            try
            {
                var fp = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
                IntPtr find = BluetoothFindFirstRadio(ref fp, out IntPtr radio);
                if (find == IntPtr.Zero) return;
                var info = NewInfo(d.Address);
                if (BluetoothGetDeviceInfo(radio, ref info) == 0)
                {
                    d.ClassOfDevice = info.ulClassofDevice;
                    if (info.fConnected != 0) d.Connected = true;
                }
                CloseHandle(radio);
                BluetoothFindRadioClose(find);
            }
            catch { }
        }

        static BLUETOOTH_DEVICE_INFO NewInfo(ulong addr) =>
            new BLUETOOTH_DEVICE_INFO { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(), Address = addr };

        static ulong ParseAddress(DeviceInformation di)
        {
            try
            {
                if (di.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out object o) && o is string s)
                    return Convert.ToUInt64(s.Replace(":", "").Replace("-", ""), 16);
            }
            catch { }
            return 0;
        }

        static bool Bool(DeviceInformation di, string key) =>
            di.Properties.TryGetValue(key, out object o) && o is bool b && b;

        [StructLayout(LayoutKind.Sequential)]
        struct BLUETOOTH_FIND_RADIO_PARAMS { public uint dwSize; }

        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEMTIME { public ushort y, mo, dow, d, h, mi, s, ms; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct BLUETOOTH_DEVICE_INFO
        {
            public uint dwSize;
            public ulong Address;
            public uint ulClassofDevice;
            public int fConnected;
            public int fRemembered;
            public int fAuthenticated;
            public SYSTEMTIME stLastSeen;
            public SYSTEMTIME stLastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
        }

        [DllImport("bthprops.cpl", SetLastError = true)] static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS p, out IntPtr radio);
        [DllImport("bthprops.cpl")] static extern bool BluetoothFindRadioClose(IntPtr find);
        [DllImport("bthprops.cpl")] static extern uint BluetoothGetDeviceInfo(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info);
        [DllImport("bthprops.cpl")] static extern uint BluetoothEnumerateInstalledServices(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info, ref uint count, [Out] Guid[] guids);
        [DllImport("bthprops.cpl")] static extern uint BluetoothSetServiceState(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info, ref Guid svc, uint flags);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    }
}
