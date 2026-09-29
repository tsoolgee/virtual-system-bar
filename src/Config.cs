using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace StatusBarWin
{
    public class AppConfig
    {
        // what the bar shows
        public bool ShowClock { get; set; } = true;
        public bool ShowVolume { get; set; } = true;
        public bool ShowWifi { get; set; } = true;
        public bool ShowLang { get; set; } = true;
        public bool ShowKeyboard { get; set; } = true;
        public bool ShowBt { get; set; } = true;
        public bool ShowUsb { get; set; } = true;
        public bool ShowBattery { get; set; } = true;
        public bool HebrewDate { get; set; } = true;

        // restrictions inside the components
        public int VolumeMax { get; set; } = 100;
        public bool AllowAudioDevice { get; set; } = true;
        public bool WifiAllowToggle { get; set; } = true;
        public bool WifiAllowDisconnect { get; set; } = true;
        public bool WifiWhitelistOn { get; set; }
        public List<string> WifiWhitelist { get; set; } = new List<string>();
        public bool BtAllowToggle { get; set; } = true;
        public bool BtAllowConnect { get; set; } = true;
        public bool BtAllowPair { get; set; } = true;
        public bool BtAllowUnpair { get; set; }

        // look & system
        public string Corner { get; set; } = "BottomRight";
        public int ScalePct { get; set; } = 100;
        public bool Watchdog { get; set; } = true;

        public string PwdHash { get; set; }
        public string PwdSalt { get; set; }
        public int PwdIter { get; set; }

        public bool WifiAllowed(string ssid)
        {
            if (!WifiWhitelistOn) return true;
            foreach (var s in WifiWhitelist)
                if (string.Equals(s.Trim(), ssid, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    static class ConfigStore
    {
        public static string FilePath
        {
            get
            {
                string beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                if (File.Exists(beside)) return beside;   // portable mode
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StatusBarWin", "config.json");
            }
        }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var c = new JavaScriptSerializer().Deserialize<AppConfig>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (c != null)
                    {
                        c.WifiWhitelist = c.WifiWhitelist ?? new List<string>();
                        c.VolumeMax = Math.Max(5, Math.Min(100, c.VolumeMax));
                        c.ScalePct = Math.Max(100, Math.Min(200, c.ScalePct));
                        return c;
                    }
                }
            }
            catch { }
            return new AppConfig();
        }

        public static void Save(AppConfig c)
        {
            string p = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(c), new UTF8Encoding(false));
            if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "StatusBarWin";

        public static bool AutoStart
        {
            get { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue(RunName) != null; }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunName, "\"" + Program.ExePath + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
        }
    }

    static class Pwd
    {
        public static bool IsSet(AppConfig c) => !string.IsNullOrEmpty(c.PwdHash);

        public static void Set(AppConfig c, string pwd)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            c.PwdIter = 200000;
            c.PwdSalt = Convert.ToBase64String(salt);
            c.PwdHash = Convert.ToBase64String(Derive(pwd, salt, c.PwdIter));
        }

        public static bool Verify(AppConfig c, string pwd)
        {
            if (!IsSet(c) || pwd == null) return false;
            var want = Convert.FromBase64String(c.PwdHash);
            var got = Derive(pwd, Convert.FromBase64String(c.PwdSalt), c.PwdIter);
            int diff = want.Length ^ got.Length;
            for (int i = 0; i < Math.Min(want.Length, got.Length); i++) diff |= want[i] ^ got[i];
            return diff == 0;
        }

        static byte[] Derive(string pwd, byte[] salt, int iter)
        {
            using (var k = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(pwd), salt, iter, HashAlgorithmName.SHA256))
                return k.GetBytes(32);
        }

        // 5 wrong attempts => locked for a minute
        static int _fails;
        static DateTime _lockedUntil;
        public static int LockSecondsLeft => Math.Max(0, (int)Math.Ceiling((_lockedUntil - DateTime.UtcNow).TotalSeconds));

        public static bool Check(AppConfig c, string pwd)
        {
            if (LockSecondsLeft > 0) return false;
            if (Verify(c, pwd)) { _fails = 0; return true; }
            if (++_fails >= 5) { _fails = 0; _lockedUntil = DateTime.UtcNow.AddMinutes(1); }
            return false;
        }
    }
}
