using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;

namespace VirtualSystemBar
{
    /// <summary>The Wi-Fi and Bluetooth radios (on/off switches), shared by both popups and the bar.</summary>
    static class Radios
    {
        static Task _init;
        public static Radio WiFi { get; private set; }
        public static Radio Bt { get; private set; }

        public static Task Init() => _init ?? (_init = InitCore());

        static async Task InitCore()
        {
            try
            {
                await Radio.RequestAccessAsync();
                var all = await Radio.GetRadiosAsync();
                WiFi = all.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
                Bt = all.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            }
            catch { }
        }

        public static bool IsOn(Radio r)
        {
            try { return r != null && r.State == RadioState.On; } catch { return false; }
        }

        public static async Task<string> Set(Radio r, bool on)
        {
            if (r == null) return "לא נמצא רכיב מתאים במחשב";
            try
            {
                var res = await r.SetStateAsync(on ? RadioState.On : RadioState.Off);
                if (res == RadioAccessStatus.Allowed) return null;
                if (res == RadioAccessStatus.DeniedBySystem) return "המערכת לא מאפשרת לשנות את המצב";
                return "אין הרשאה לשנות את המצב";
            }
            catch (Exception ex) { return ex.Message; }
        }
    }

    static class WiFi
    {
        public class Status
        {
            public bool HasAdapter;
            public bool RadioOn;
            public bool Connected;
            public string Ssid;
            public int Bars;
            public bool Ethernet;   // no Wi-Fi connection, but a wired one
        }

        public class Net
        {
            public string Ssid;
            public int Bars;
            public bool Secured;
            public bool Enterprise;
            public WiFiAvailableNetwork Raw;
        }

        static Task _init;
        static WiFiAdapter _ad;
        public static WiFiAccessStatus Access { get; private set; } = WiFiAccessStatus.Unspecified;

        public static Task Init() => _init ?? (_init = InitCore());

        static async Task InitCore()
        {
            await Radios.Init();
            try
            {
                Access = await WiFiAdapter.RequestAccessAsync();
                if (Access == WiFiAccessStatus.Allowed)
                    _ad = (await WiFiAdapter.FindAllAdaptersAsync()).FirstOrDefault();
            }
            catch { }
        }

        public static async Task<Status> GetStatus()
        {
            await Init();
            var s = new Status { HasAdapter = _ad != null, RadioOn = Radios.IsOn(Radios.WiFi) };
            if (_ad != null)
            {
                try
                {
                    var p = await _ad.NetworkAdapter.GetConnectedProfileAsync();
                    if (p != null && p.IsWlanConnectionProfile)
                    {
                        s.Connected = true;
                        s.Ssid = p.WlanConnectionProfileDetails?.GetConnectedSsid() ?? p.ProfileName;
                        s.Bars = p.GetSignalBars() ?? 0;
                    }
                }
                catch { }
            }
            if (!s.Connected)
            {
                try
                {
                    var ip = NetworkInformation.GetInternetConnectionProfile();
                    s.Ethernet = ip != null && !ip.IsWlanConnectionProfile && !ip.IsWwanConnectionProfile;
                }
                catch { }
            }
            return s;
        }

        public static async Task<List<Net>> Scan()
        {
            await Init();
            var list = new List<Net>();
            if (_ad == null) return list;
            try { await _ad.ScanAsync(); } catch { }
            foreach (var n in _ad.NetworkReport.AvailableNetworks)
            {
                if (string.IsNullOrEmpty(n.Ssid)) continue;
                var auth = n.SecuritySettings.NetworkAuthenticationType;
                var net = new Net
                {
                    Ssid = n.Ssid,
                    Bars = n.SignalBars,
                    Raw = n,
                    Secured = auth != NetworkAuthenticationType.Open80211 && auth != NetworkAuthenticationType.None,
                    Enterprise = auth == NetworkAuthenticationType.Rsna || auth == NetworkAuthenticationType.Wpa
                              || auth == NetworkAuthenticationType.Wpa3,
                };
                var same = list.FirstOrDefault(x => x.Ssid == net.Ssid);
                if (same == null) list.Add(net);
                else if (net.Bars > same.Bars) list[list.IndexOf(same)] = net;
            }
            return list.OrderByDescending(x => x.Bars).ThenBy(x => x.Ssid).ToList();
        }

        /// <summary>Returns null on success, otherwise a message. NeedPassword tells the caller to ask for one.</summary>
        public static async Task<(string error, bool needPassword)> Connect(Net net, string password)
        {
            if (_ad == null) return ("אין מתאם Wi-Fi", false);
            try
            {
                WiFiConnectionResult r = password == null
                    ? await _ad.ConnectAsync(net.Raw, WiFiReconnectionKind.Automatic)
                    : await _ad.ConnectAsync(net.Raw, WiFiReconnectionKind.Automatic, new PasswordCredential { Password = password });
                switch (r.ConnectionStatus)
                {
                    case WiFiConnectionStatus.Success: return (null, false);
                    case WiFiConnectionStatus.InvalidCredential: return (password == null ? null : "הסיסמה שגויה", true);
                    case WiFiConnectionStatus.NetworkNotAvailable: return ("הרשת לא זמינה כרגע", false);
                    case WiFiConnectionStatus.Timeout: return ("תם הזמן להתחברות", false);
                    case WiFiConnectionStatus.AccessRevoked: return ("אין הרשאה להתחבר", false);
                    case WiFiConnectionStatus.UnsupportedAuthenticationProtocol: return ("סוג האבטחה של הרשת לא נתמך", false);
                    default:
                        // an unknown secured network without a saved profile fails like this - ask for a password
                        return (password == null && net.Secured ? null : "ההתחברות נכשלה", password == null && net.Secured);
                }
            }
            catch (Exception ex) { return (ex.Message, false); }
        }

        public static void Disconnect()
        {
            try { _ad?.Disconnect(); } catch { }
        }
    }
}
