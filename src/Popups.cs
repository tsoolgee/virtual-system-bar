using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Enumeration;
using Panel = System.Windows.Forms.Panel;

namespace VirtualSystemBar
{
    class VolumePopup : Popup
    {
        public event Action Changed;

        public VolumePopup(AppConfig cfg) : base(cfg) { }

        protected override void Build()
        {
            if (!Audio.TryGet(out int level, out bool mute))
            {
                Header("שמע");
                Message("לא נמצא התקן שמע");
                return;
            }
            Header("עוצמת שמע");

            int h = Ui.Px(40);
            var line = new Panel { BackColor = Ui.PopupBg };
            Add(line, h);
            var muteBtn = new IconButton(G.Volume(level, mute)) { Pt = 13 };
            var pct = Ui.Label(level.ToString(), 11);
            pct.TextAlign = ContentAlignment.MiddleCenter;
            var slider = new Slider { Max = Cfg.VolumeMax, Value = Math.Min(level, Cfg.VolumeMax) };
            muteBtn.SetBounds(line.Width - Ui.Px(36), (h - Ui.Px(34)) / 2, Ui.Px(34), Ui.Px(34));
            pct.SetBounds(0, 0, Ui.Px(40), h);
            slider.SetBounds(Ui.Px(44), (h - slider.Height) / 2, line.Width - Ui.Px(44) - Ui.Px(44), slider.Height);
            line.Controls.AddRange(new Control[] { muteBtn, pct, slider });

            slider.Changed += (s, e) =>
            {
                Audio.SetLevel(slider.Value);
                if (slider.Value > 0) Audio.SetMute(false);
                pct.Text = slider.Value.ToString();
                muteBtn.Glyph = G.Volume(slider.Value, false);
                muteBtn.Invalidate();
                Changed?.Invoke();
            };
            muteBtn.Click += (s, e) =>
            {
                Audio.TryGet(out _, out bool m);
                Audio.SetMute(!m);
                muteBtn.Glyph = G.Volume(slider.Value, !m);
                muteBtn.Invalidate();
                Changed?.Invoke();
            };

            if (Cfg.AllowAudioDevice)
            {
                var devs = Audio.Devices();
                if (devs.Count > 1)
                {
                    Space(Ui.Px(4));
                    Add(Ui.Label("התקן פלט", 9, FontStyle.Regular, Ui.Sub), Ui.Px(20));
                    var list = List(devs.Count, 4);
                    FillList(list, devs.Select(d =>
                    {
                        var r = new Row
                        {
                            Glyph = d.Name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0 || d.Name.Contains("אוזני") ? G.Headphones : G.Speaker,
                            Title = d.Name, Subtitle = d.IsDefault ? "בשימוש" : null, Selected = d.IsDefault,
                        };
                        r.Click += (s, e) => { Audio.SetDefault(d.Id); Rebuild(); Changed?.Invoke(); };
                        return r;
                    }));
                }
            }
        }
    }

    class WifiPopup : Popup
    {
        WiFi.Status _st;
        List<WiFi.Net> _nets;
        string _sel;        // selected SSID
        bool _needPwd;
        string _msg;
        bool _msgErr;
        bool _busy;
        public event Action Changed;

        public WifiPopup(AppConfig cfg) : base(cfg) { Load += async (s, e) => await Reload(true); }

        async Task Reload(bool scan)
        {
            _st = await WiFi.GetStatus();
            if (IsDisposed) return;
            if (scan && _st.HasAdapter && _st.RadioOn)
            {
                if (_nets == null) Rebuild();   // show "searching" first
                var nets = await WiFi.Scan();
                if (IsDisposed) return;
                _nets = nets.Where(n => Cfg.WifiAllowed(n.Ssid) || (_st.Connected && n.Ssid == _st.Ssid)).ToList();
            }
            Rebuild();
        }

        protected override void Build()
        {
            var refresh = new IconButton(G.Refresh) { Pt = 11, Enabled = !_busy };
            refresh.Click += async (s, e) => { _msg = null; await Reload(true); };
            Toggle tg = null;
            if (_st != null && _st.HasAdapter && Cfg.WifiAllowToggle && Radios.WiFi != null)
            {
                tg = new Toggle { On = _st.RadioOn, Enabled = !_busy };
                tg.Changed += async (s, e) =>
                {
                    _busy = true;
                    string err = await Radios.Set(Radios.WiFi, tg.On);
                    _busy = false;
                    _msg = err; _msgErr = err != null;
                    _nets = null;
                    await Task.Delay(tg.On ? 2500 : 500);
                    if (!IsDisposed) await Reload(true);
                    Changed?.Invoke();
                };
            }
            Header("Wi-Fi", tg == null ? new Control[] { refresh } : new Control[] { tg, refresh });

            if (_st == null) { Message("טוען..."); return; }
            if (!_st.HasAdapter)
            {
                Message(WiFi.Access == Windows.Devices.WiFi.WiFiAccessStatus.Allowed
                    ? "לא נמצא מתאם Wi-Fi במחשב"
                    : "אין גישה ל-Wi-Fi. יש לאפשר למנהל המחשב גישת מיקום לאפליקציות שולחן עבודה (נדרש ב-Windows 11).", true);
                return;
            }
            if (!_st.RadioOn) { Message("Wi-Fi כבוי"); Message(_msg, _msgErr); return; }
            if (_nets == null) { Message("מחפש רשתות..."); return; }
            if (_nets.Count == 0) { Message(Cfg.WifiWhitelistOn ? "אף רשת מותרת לא נמצאה בטווח" : "לא נמצאו רשתות"); Message(_msg, _msgErr); return; }

            var list = List(_nets.Count, 5);
            FillList(list, _nets.Select(n =>
            {
                bool conn = _st.Connected && n.Ssid == _st.Ssid;
                var r = new Row
                {
                    Glyph = G.WifiBars(n.Bars), Title = n.Ssid, Selected = n.Ssid == _sel,
                    Subtitle = conn ? "מחובר" : n.Enterprise ? "רשת ארגונית" : n.Secured ? "מאובטחת" : "פתוחה",
                };
                r.Click += (s, e) => { if (_busy) return; _sel = n.Ssid == _sel ? null : n.Ssid; _needPwd = false; _msg = null; Rebuild(); };
                return r;
            }));

            var sel = _nets.FirstOrDefault(n => n.Ssid == _sel);
            if (sel != null)
            {
                bool conn = _st.Connected && sel.Ssid == _st.Ssid;
                if (conn)
                {
                    if (Cfg.WifiAllowDisconnect)
                    {
                        var b = Ui.Button("התנתק");
                        b.Enabled = !_busy;
                        b.Click += async (s, e) => { WiFi.Disconnect(); _msg = null; await Task.Delay(800); await Reload(false); Changed?.Invoke(); };
                        Buttons(b);
                    }
                }
                else
                {
                    TextBox pwd = null;
                    if (_needPwd)
                    {
                        Add(Ui.Label("סיסמת הרשת", 9, FontStyle.Regular, Ui.Sub), Ui.Px(20));
                        pwd = Add(new TextBox { Font = Ui.F(11), UseSystemPasswordChar = true, ShortcutsEnabled = false }, 0);
                        BeginInvoke(new Action(() => { if (!pwd.IsDisposed) pwd.Focus(); }));
                    }
                    var b = Ui.Button(_busy ? "מתחבר..." : "התחבר", true);
                    b.Enabled = !_busy;
                    b.Click += async (s, e) => await Connect(sel, pwd?.Text);
                    if (pwd != null) pwd.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Connect(sel, pwd.Text); } };
                    Buttons(b);
                }
            }
            Message(_msg, _msgErr);
        }

        async Task Connect(WiFi.Net n, string password)
        {
            if (_busy) return;
            if (password != null && password.Length == 0) { _msg = "יש להקליד סיסמה"; _msgErr = true; Rebuild(); return; }
            _busy = true; _msg = null; Rebuild();
            var (err, need) = await WiFi.Connect(n, password);
            _busy = false;
            if (IsDisposed) return;
            _needPwd = need;
            _msg = err; _msgErr = err != null;
            if (err == null && !need) { _sel = null; await Reload(false); Changed?.Invoke(); }
            else Rebuild();
        }
    }

    class BtPopup : Popup
    {
        List<Bt.Dev> _devs;
        Bt.Dev _sel;
        bool _discover;
        readonly List<DeviceInformation> _found = new List<DeviceInformation>();
        IDisposable _watch;
        string _msg;
        bool _msgErr, _busy;
        public event Action Changed;

        public BtPopup(AppConfig cfg) : base(cfg)
        {
            Load += async (s, e) => await Reload();
            FormClosed += (s, e) => _watch?.Dispose();
        }

        async Task Reload()
        {
            await Radios.Init();
            var devs = Radios.IsOn(Radios.Bt) ? await Bt.Paired() : new List<Bt.Dev>();
            if (IsDisposed) return;
            _devs = devs;
            if (_sel != null) _sel = _devs.FirstOrDefault(d => d.Info.Id == _sel.Info.Id);
            Rebuild();
        }

        protected override void Build()
        {
            if (_discover) { BuildDiscover(); return; }

            Toggle tg = null;
            if (Radios.Bt != null && Cfg.BtAllowToggle)
            {
                tg = new Toggle { On = Radios.IsOn(Radios.Bt), Enabled = !_busy };
                tg.Changed += async (s, e) =>
                {
                    _busy = true;
                    string err = await Radios.Set(Radios.Bt, tg.On);
                    _busy = false;
                    _msg = err; _msgErr = err != null;
                    await Task.Delay(1000);
                    if (!IsDisposed) await Reload();
                    Changed?.Invoke();
                };
            }
            var refresh = new IconButton(G.Refresh) { Pt = 11 };
            refresh.Click += async (s, e) => { _msg = null; await Reload(); };
            Header("Bluetooth", tg == null ? new Control[] { refresh } : new Control[] { tg, refresh });

            if (Radios.Bt == null) { Message("לא נמצא רכיב Bluetooth במחשב"); return; }
            if (!Radios.IsOn(Radios.Bt)) { Message("Bluetooth כבוי"); Message(_msg, _msgErr); return; }
            if (_devs == null) { Message("טוען..."); return; }

            if (_devs.Count == 0) Message("אין התקנים מצומדים");
            else
            {
                var list = List(_devs.Count, 5);
                FillList(list, _devs.Select(d =>
                {
                    var r = new Row
                    {
                        Glyph = G.ForBtClass(d.ClassOfDevice), Title = d.Name, Subtitle = d.Connected ? "מחובר" : "לא מחובר",
                        Selected = _sel != null && d.Info.Id == _sel.Info.Id,
                    };
                    r.Click += (s, e) => { if (_busy) return; _sel = r.Selected ? null : d; _msg = null; Rebuild(); };
                    return r;
                }));
            }

            var buttons = new List<Button>();
            if (_sel != null && Cfg.BtAllowConnect)
            {
                var b = Ui.Button(_sel.Connected ? "נתק" : "התחבר", !_sel.Connected);
                var d = _sel;
                b.Click += async (s, e) => await Run(() => Bt.SetConnected(d, !d.Connected), d.Connected ? "מנתק..." : "מתחבר...");
                buttons.Add(b);
            }
            if (_sel != null && Cfg.BtAllowUnpair)
            {
                var b = Ui.Button("הסר התקן");
                var d = _sel;
                b.Click += async (s, e) =>
                {
                    if (!Hold_(() => Msg.YesNo(this, "להסיר את " + d.Name + "?"))) return;
                    _sel = null;
                    await Run(() => Bt.Unpair(d), "מסיר...");
                };
                buttons.Add(b);
            }
            if (_sel == null && Cfg.BtAllowPair)
            {
                var b = Ui.Button("+ הוספת התקן");
                b.Click += (s, e) => StartDiscover();
                buttons.Add(b);
            }
            foreach (var b in buttons) b.Enabled = !_busy;
            if (buttons.Count > 0) Buttons(buttons.ToArray());
            Message(_msg, _msgErr);
        }

        async Task Run(Func<Task<string>> op, string progress)
        {
            _busy = true; _msg = progress; _msgErr = false; Rebuild();
            string err = await op();
            _busy = false;
            if (IsDisposed) return;
            _msg = err; _msgErr = err != null;
            await Task.Delay(err == null ? 1500 : 0);
            if (!IsDisposed) await Reload();
            Changed?.Invoke();
        }

        void StartDiscover()
        {
            _discover = true;
            _found.Clear();
            _msg = null;
            _watch = Bt.Discover(di => BeginInvoke(new Action(() =>
            {
                if (IsDisposed || _found.Any(x => x.Id == di.Id)) return;
                _found.Add(di);
                if (!_busy) Rebuild();
            })));
            Rebuild();
        }

        void StopDiscover()
        {
            _watch?.Dispose();
            _watch = null;
            _discover = false;
        }

        void BuildDiscover()
        {
            var back = new IconButton(G.Back) { Pt = 11 };
            back.Click += async (s, e) => { StopDiscover(); _msg = null; await Reload(); };
            Header("הוספת התקן", back);
            Message("הפעל את ההתקן במצב צימוד ובחר אותו מהרשימה.");
            if (_found.Count == 0) Message("מחפש התקנים בסביבה...");
            else
            {
                var list = List(_found.Count, 5);
                FillList(list, _found.Select(di =>
                {
                    var r = new Row { Glyph = G.Bluetooth, Title = di.Name, Subtitle = _busy ? null : "לחץ לצימוד", Dim = _busy };
                    r.Click += async (s, e) => await PairWith(di);
                    return r;
                }));
            }
            Message(_msg, _msgErr);
        }

        async Task PairWith(DeviceInformation di)
        {
            if (_busy) return;
            _busy = true; _msg = "מצמד את " + di.Name + "..."; _msgErr = false; Rebuild();
            string err = await Bt.Pair(di, (kind, pin) => (string)Invoke(new Func<string>(() => Hold_(() =>
            {
                switch (kind)
                {
                    case "show":
                        Msg.Info(this, "הקלד בהתקן את הקוד: " + pin);
                        return "";
                    case "confirm":
                        return Msg.YesNo(this, "האם מוצג בהתקן הקוד " + pin + "?") ? "" : null;
                    default:
                        return AskDialog.Ask(this, "צימוד", "הקלד את קוד הצימוד של ההתקן:", false);
                }
            }))));
            _busy = false;
            if (IsDisposed) return;
            if (err == null)
            {
                StopDiscover();
                _msg = di.Name + " צומד בהצלחה"; _msgErr = false;
                await Reload();
                Changed?.Invoke();
            }
            else { _msg = err; _msgErr = true; Rebuild(); }
        }
    }

    class UsbPopup : Popup
    {
        string _msg;
        bool _msgErr;
        public event Action Changed;

        public UsbPopup(AppConfig cfg) : base(cfg) { }

        protected override void Build()
        {
            Header("הוצאת התקן בבטחה");
            var drives = Usb.List();
            if (drives.Count == 0) Message("אין התקנים נשלפים מחוברים");
            else
            {
                var list = List(drives.Count, 5);
                FillList(list, drives.Select(d =>
                {
                    var r = new Row { Glyph = G.Drive, Title = d.Label, Subtitle = "לחץ להוצאה" };
                    r.Click += (s, e) =>
                    {
                        Cursor = Cursors.WaitCursor;
                        string err = Usb.Eject(d);
                        Cursor = Cursors.Default;
                        _msg = err ?? "אפשר לנתק את " + d.Label;
                        _msgErr = err != null;
                        Rebuild();
                        Changed?.Invoke();
                    };
                    return r;
                }));
            }
            Message(_msg, _msgErr);
        }
    }

    class LangPopup : Popup
    {
        readonly IntPtr _target;

        public LangPopup(AppConfig cfg, IntPtr target) : base(cfg) { _target = target; }

        protected override void Build()
        {
            Header("שפת מקלדת");
            var cur = Keyboard.Current(_target);
            var all = InputLanguage.InstalledInputLanguages.Cast<InputLanguage>().ToList();
            var list = List(all.Count, 5);
            FillList(list, all.Select(l =>
            {
                var r = new Row { Glyph = "", Title = l.Culture.NativeName, Subtitle = l.LayoutName, Selected = l.Handle == cur.Handle };
                r.Click += (s, e) => { Keyboard.Switch(_target, l); Close(); };
                return r;
            }));
        }
    }
}
