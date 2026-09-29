using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StatusBarWin
{
    /// <summary>
    /// The always-on-top strip that replaces the Windows tray. It never takes focus, so the kiosk
    /// app keeps the keyboard and the language button acts on the kiosk app's window.
    /// </summary>
    class BarForm : Form
    {
        enum Kind { Clock, Volume, Wifi, Lang, Keyboard, Bt, Usb, Battery, Gear }

        class Item
        {
            public Kind Kind;
            public Rectangle Rect;
        }

        AppConfig _cfg;
        readonly List<Item> _items = new List<Item>();
        readonly ToolTip _tip = new ToolTip { ShowAlways = true };
        readonly Timer _fast = new Timer { Interval = 1000 };
        readonly Timer _slow = new Timer { Interval = 3000 };
        readonly uint _myPid = (uint)Process.GetCurrentProcess().Id;
        Item _hover;
        Popup _popup;
        Kind? _popupKind;
        bool _exiting, _polling;

        // state shown on the bar
        int _vol; bool _mute, _hasAudio;
        WiFi.Status _wifi;
        bool _btOn, _hasBt;
        int _usbCount;
        IntPtr _target;   // last foreground window that isn't ours = the kiosk app

        public BarForm(AppConfig cfg)
        {
            _cfg = cfg;
            Ui.SetScale(cfg.ScalePct);
            Text = Program.Title;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Ui.Bg;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;

            _fast.Tick += (s, e) => FastTick();
            _slow.Tick += async (s, e) => await SlowTick();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            Guard.Ensure(_cfg.Watchdog);
            RefreshLocal();
            Relayout();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08 | 0x08000000;   // TOOLWINDOW | TOPMOST | NOACTIVATE
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21 /*WM_MOUSEACTIVATE*/) { m.Result = (IntPtr)3 /*MA_NOACTIVATE*/; return; }
            base.WndProc(ref m);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _fast.Start();
            _slow.Start();
            await SlowTick();
        }

        void OnDisplayChanged(object s, EventArgs e) => BeginInvoke(new Action(Relayout));

        // ---------- layout ----------

        bool Visible_(Kind k)
        {
            switch (k)
            {
                case Kind.Clock: return _cfg.ShowClock;
                case Kind.Volume: return _cfg.ShowVolume;
                case Kind.Wifi: return _cfg.ShowWifi;
                case Kind.Lang: return _cfg.ShowLang;
                case Kind.Keyboard: return _cfg.ShowKeyboard;
                case Kind.Bt: return _cfg.ShowBt && _hasBt;
                case Kind.Usb: return _cfg.ShowUsb && _usbCount > 0;
                case Kind.Battery: return _cfg.ShowBattery && HasBattery;
                default: return true;
            }
        }

        static bool HasBattery => (SystemInformation.PowerStatus.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0
                                  && SystemInformation.PowerStatus.BatteryChargeStatus != BatteryChargeStatus.Unknown;

        int WidthOf(Kind k)
        {
            switch (k)
            {
                case Kind.Clock: return Ui.Px(84);
                case Kind.Lang: return Ui.Px(42);
                case Kind.Battery: return Ui.Px(64);
                case Kind.Gear: return Ui.Px(34);
                default: return Ui.Px(38);
            }
        }

        void Relayout()
        {
            _items.Clear();
            int x = Ui.Px(4), h = Ui.Px(40);
            // same order as the Windows tray in the request's screenshot, left to right; settings at the far right
            foreach (Kind k in new[] { Kind.Clock, Kind.Volume, Kind.Wifi, Kind.Lang, Kind.Keyboard, Kind.Bt, Kind.Usb, Kind.Battery, Kind.Gear })
            {
                if (!Visible_(k)) continue;
                int w = WidthOf(k);
                _items.Add(new Item { Kind = k, Rect = new Rectangle(x, Ui.Px(3), w, h - Ui.Px(6)) });
                x += w;
            }
            var size = new Size(x + Ui.Px(4), h);
            var scr = Screen.PrimaryScreen.Bounds;   // kiosk: no taskbar, so the whole screen
            int left = _cfg.Corner.EndsWith("Left") ? scr.Left : scr.Right - size.Width;
            int top = _cfg.Corner.StartsWith("Top") ? scr.Top : scr.Bottom - size.Height;
            Bounds = new Rectangle(left, top, size.Width, size.Height);
            Invalidate();
        }

        bool AtTop => _cfg.Corner.StartsWith("Top");

        // ---------- state ----------

        void FastTick()
        {
            // keep above full-screen kiosk windows
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /*NOSIZE|NOMOVE|NOACTIVATE*/);
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                Native.GetWindowThreadProcessId(fg, out uint pid);
                if (pid != _myPid) _target = fg;
            }
            RefreshLocal();
            Invalidate();
        }

        void RefreshLocal()
        {
            _hasAudio = Audio.TryGet(out _vol, out _mute);
            if (_hasAudio && _vol > _cfg.VolumeMax) { Audio.SetLevel(_cfg.VolumeMax); _vol = _cfg.VolumeMax; }
        }

        async System.Threading.Tasks.Task SlowTick()
        {
            if (_polling) return;
            _polling = true;
            try
            {
                bool before = Visible_(Kind.Bt) , usbBefore = Visible_(Kind.Usb);
                if (_cfg.ShowWifi) _wifi = await WiFi.GetStatus();
                await Radios.Init();
                _hasBt = Radios.Bt != null;
                _btOn = Radios.IsOn(Radios.Bt);
                _usbCount = _cfg.ShowUsb ? Usb.List().Count : 0;
                if (before != Visible_(Kind.Bt) || usbBefore != Visible_(Kind.Usb)) Relayout();
                Invalidate();
            }
            catch { }
            finally { _polling = false; }
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Bg);
            using (var pen = new Pen(Ui.Border))
            {
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
            using (var icons = Ui.Icons(12))
            using (var small = Ui.F(8.5f))
            using (var lang = Ui.F(9.5f))
            {
                foreach (var it in _items)
                {
                    bool active = _popupKind == it.Kind && _popup != null && !_popup.IsDisposed;
                    if (it == _hover || active)
                        using (var path = Ui.Round(it.Rect, Ui.Px(4)))
                        using (var b = new SolidBrush(active ? Ui.Selected : Ui.Hover)) g.FillPath(b, path);
                    var r = it.Rect;
                    switch (it.Kind)
                    {
                        case Kind.Clock:
                            var now = DateTime.Now;
                            var top = new Rectangle(r.X, r.Y + Ui.Px(1), r.Width, r.Height / 2);
                            var bot = new Rectangle(r.X, r.Y + r.Height / 2 - Ui.Px(1), r.Width, r.Height / 2);
                            TextRenderer.DrawText(g, now.ToString("HH:mm"), small, top, Ui.Text, Ui.Center);
                            TextRenderer.DrawText(g, now.ToString("dd/MM/yyyy"), small, bot, Ui.Text, Ui.Center);
                            break;
                        case Kind.Volume:
                            Glyph(g, icons, r, _hasAudio ? G.Volume(_vol, _mute) : G.Mute, _hasAudio);
                            break;
                        case Kind.Wifi:
                            string wg = _wifi == null ? G.Wifi
                                : _wifi.Connected ? G.WifiBars(_wifi.Bars)
                                : _wifi.Ethernet ? G.Ethernet : G.Globe;
                            Glyph(g, icons, r, wg, _wifi == null || _wifi.Connected || _wifi.Ethernet);
                            break;
                        case Kind.Lang:
                            TextRenderer.DrawText(g, Keyboard.Abbrev(Keyboard.Current(_target).Culture), lang, r, Ui.Text, Ui.Center);
                            break;
                        case Kind.Keyboard:
                            Glyph(g, icons, r, G.Keyboard, true);
                            break;
                        case Kind.Bt:
                            Glyph(g, icons, r, G.Bluetooth, _btOn);
                            break;
                        case Kind.Usb:
                            Glyph(g, icons, r, G.Usb, true);
                            break;
                        case Kind.Battery:
                            var ps = SystemInformation.PowerStatus;
                            int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
                            var gr = new Rectangle(r.Right - Ui.Px(26), r.Y, Ui.Px(24), r.Height);
                            Glyph(g, icons, gr, G.Battery(pct), true);
                            var tr = new Rectangle(r.X, r.Y, r.Width - Ui.Px(26), r.Height);
                            TextRenderer.DrawText(g, pct + "%" + (ps.PowerLineStatus == PowerLineStatus.Online ? "⚡" : ""), small, tr, Ui.Text, Ui.Center);
                            break;
                        case Kind.Gear:
                            using (var gi = Ui.Icons(10)) Glyph(g, gi, r, G.Settings, true, Ui.Sub);
                            break;
                    }
                }
            }
        }

        static void Glyph(Graphics g, Font f, Rectangle r, string glyph, bool on, Color? color = null) =>
            TextRenderer.DrawText(g, glyph, f, r, color ?? (on ? Ui.Text : Ui.Faint), Ui.Center);

        // ---------- mouse ----------

        Item Hit(Point p) => _items.FirstOrDefault(i => i.Rect.Contains(p));

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var it = Hit(e.Location);
            if (it == _hover) return;
            _hover = it;
            Invalidate();
            if (it == null) { _tip.Hide(this); return; }
            string text = TipFor(it.Kind);
            if (text != null) _tip.Show(text, this, it.Rect.X, AtTop ? Height + 2 : -Ui.Px(30), 4000);
            else _tip.Hide(this);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = null;
            _tip.Hide(this);
            Invalidate();
        }

        string TipFor(Kind k)
        {
            switch (k)
            {
                case Kind.Clock:
                    var now = DateTime.Now;
                    string s = now.ToString("dddd, d MMMM yyyy", new CultureInfo("he-IL"));
                    if (_cfg.HebrewDate)
                    {
                        var he = new CultureInfo("he-IL");
                        he.DateTimeFormat.Calendar = new HebrewCalendar();
                        s += "\n" + now.ToString("d MMMM yyyy", he);
                    }
                    return s;
                case Kind.Volume: return _hasAudio ? "רמקולים: " + (_mute ? "מושתק" : _vol + "%") : "אין התקן שמע";
                case Kind.Wifi:
                    if (_wifi == null) return "רשת";
                    if (_wifi.Connected) return "מחובר ל-" + _wifi.Ssid;
                    if (_wifi.Ethernet) return "מחובר ברשת קווית";
                    return _wifi.RadioOn ? "לא מחובר" : "Wi-Fi כבוי";
                case Kind.Lang: return "שפת קלט. לחיצה ימנית: רשימת השפות";
                case Kind.Keyboard: return "מקלדת מגע";
                case Kind.Bt: return _btOn ? "Bluetooth פועל" : "Bluetooth כבוי";
                case Kind.Usb: return "הוצאת התקן בבטחה";
                case Kind.Battery: return "סוללה";
                case Kind.Gear: return "הגדרות (מנהל)";
            }
            return null;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            var it = Hit(e.Location);
            if (it == null) return;
            _tip.Hide(this);

            // a second click on the same item closes its popup
            bool wasOpen = _popup != null && !_popup.IsDisposed && _popupKind == it.Kind;
            ClosePopup();
            if (wasOpen) return;

            switch (it.Kind)
            {
                case Kind.Volume:
                    var vp = new VolumePopup(_cfg);
                    vp.Changed += () => { RefreshLocal(); Invalidate(); };
                    Open(vp, it);
                    break;
                case Kind.Wifi:
                    var wp = new WifiPopup(_cfg);
                    wp.Changed += async () => await SlowTick();
                    Open(wp, it);
                    break;
                case Kind.Bt:
                    var bp = new BtPopup(_cfg);
                    bp.Changed += async () => await SlowTick();
                    Open(bp, it);
                    break;
                case Kind.Usb:
                    var up = new UsbPopup(_cfg);
                    up.Changed += async () => await SlowTick();
                    Open(up, it);
                    break;
                case Kind.Lang:
                    if (e.Button == MouseButtons.Right) Open(new LangPopup(_cfg, _target), it);
                    else { Keyboard.Next(_target); BeginInvoke(new Action(Invalidate)); }
                    break;
                case Kind.Keyboard:
                    string err = Keyboard.ToggleTouch();
                    if (err != null) Msg.Info(this, err);
                    break;
                case Kind.Gear:
                    OpenAdmin();
                    break;
            }
        }

        void Open(Popup p, Item it)
        {
            _popup = p;
            _popupKind = it.Kind;
            p.FormClosed += (s, e) => { if (_popup == p) { _popup = null; _popupKind = null; } Invalidate(); };
            var r = RectangleToScreen(it.Rect);
            p.ShowAt(new Rectangle(r.X, Top, r.Width, Height), AtTop);
            Invalidate();
        }

        void ClosePopup()
        {
            if (_popup != null && !_popup.IsDisposed) _popup.Close();
            _popup = null;
            _popupKind = null;
        }

        // ---------- admin ----------

        void OpenAdmin()
        {
            if (!Pwd.IsSet(_cfg))
            {
                // first run: the admin chooses the password before anything else
                string p1 = AskDialog.Ask(this, "הגדרת סיסמת מנהל", "זו הפעלה ראשונה. בחר סיסמת מנהל להגדרות:", true);
                if (string.IsNullOrEmpty(p1)) return;
                string p2 = AskDialog.Ask(this, "הגדרת סיסמת מנהל", "הקלד שוב את הסיסמה:", true);
                if (p1 != p2) { Msg.Info(this, "הסיסמאות לא זהות"); return; }
                Pwd.Set(_cfg, p1);
                if (!TrySave(_cfg)) return;
            }
            else
            {
                if (Pwd.LockSecondsLeft > 0) { Msg.Info(this, $"יותר מדי ניסיונות שגויים. נסה שוב בעוד {Pwd.LockSecondsLeft} שניות."); return; }
                string p = AskDialog.Ask(this, "הגדרות", "סיסמת מנהל:", true);
                if (p == null) return;
                if (!Pwd.Check(_cfg, p))
                {
                    Msg.Info(this, Pwd.LockSecondsLeft > 0 ? "יותר מדי ניסיונות שגויים. ההגדרות נעולות לדקה." : "סיסמה שגויה");
                    return;
                }
            }

            using (var f = new AdminForm(_cfg))
            {
                var res = f.ShowDialog(this);
                if (f.ExitRequested) { ExitApp(); return; }
                if (res != DialogResult.OK) return;
                if (!TrySave(f.Result)) return;
                _cfg = f.Result;
                Ui.SetScale(_cfg.ScalePct);
                Guard.Ensure(_cfg.Watchdog);
                RefreshLocal();
                Relayout();
                _ = SlowTick();
            }
        }

        bool TrySave(AppConfig c)
        {
            try { ConfigStore.Save(c); return true; }
            catch (Exception ex) { Msg.Info(this, "שמירת ההגדרות נכשלה:\n" + ex.Message); return false; }
        }

        void ExitApp()
        {
            _exiting = true;
            Guard.Ensure(false);
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Alt+F4 and the like: only the admin's "exit" closes the bar; Windows shutdown/logoff always may
            if (!_exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            base.OnFormClosing(e);
        }
    }
}
