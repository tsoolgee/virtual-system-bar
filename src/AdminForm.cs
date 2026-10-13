using System;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace VirtualSystemBar
{
    /// <summary>Admin settings, reached only through the password. RightToLeftLayout mirrors the X coordinates below.</summary>
    class AdminForm : Form
    {
        public AppConfig Result { get; private set; }
        public bool ExitRequested { get; private set; }

        readonly AppConfig _c;
        readonly Panel _body;
        int _y;
        int _w;

        public AdminForm(AppConfig cfg)
        {
            var js = new JavaScriptSerializer();
            _c = js.Deserialize<AppConfig>(js.Serialize(cfg));   // edit a copy

            Text = Program.Title + " – הגדרות מנהל";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            BackColor = Ui.PopupBg;
            Font = Ui.F(10);
            AutoScaleMode = AutoScaleMode.None;

            _w = Ui.Px(520);
            var scr = Screen.PrimaryScreen.WorkingArea;
            int bodyH = Math.Min(Ui.Px(620), scr.Height - Ui.Px(140));
            _body = new Panel { AutoScroll = true, Location = new Point(0, 0), Size = new Size(_w, bodyH) };
            Controls.Add(_body);
            _y = Ui.Px(12);

            Section("רכיבים בפס");
            Check("שעה ותאריך", _c.ShowClock, v => _c.ShowClock = v);
            Check("תאריך עברי בחלונית השעה", _c.HebrewDate, v => _c.HebrewDate = v, 1);
            Check("רמקול (עוצמת שמע)", _c.ShowVolume, v => _c.ShowVolume = v);
            Check("Wi-Fi", _c.ShowWifi, v => _c.ShowWifi = v);
            Check("שפת מקלדת", _c.ShowLang, v => _c.ShowLang = v);
            Check("מקלדת מגע", _c.ShowKeyboard, v => _c.ShowKeyboard = v);
            Check("Bluetooth", _c.ShowBt, v => _c.ShowBt = v);
            Check("הוצאת USB בבטחה (מוצג רק כשמחובר התקן)", _c.ShowUsb, v => _c.ShowUsb = v);
            Check("סוללה (רק במחשב נייד)", _c.ShowBattery, v => _c.ShowBattery = v);
            Check("כפתור X לסגירת התוכנה", _c.ShowClose, v => _c.ShowClose = v);
            Check("סגירה ב-X דורשת סיסמת מנהל", _c.CloseNeedsPassword, v => _c.CloseNeedsPassword = v, 1);
            Note("אם לא מסומן, כל משתמש יכול לסגור את התוכנה (למשל כשהפס מסתיר לו משהו).");

            Section("שמע");
            var max = new NumericUpDown { Minimum = 5, Maximum = 100, Increment = 5, Value = _c.VolumeMax, Width = Ui.Px(70), Font = Ui.F(10) };
            max.ValueChanged += (s, e) => _c.VolumeMax = (int)max.Value;
            Line("עוצמה מקסימלית (%)", max);
            Check("לאפשר בחירת התקן פלט (רמקולים / אוזניות)", _c.AllowAudioDevice, v => _c.AllowAudioDevice = v);

            Section("Wi-Fi");
            Check("לאפשר הפעלה וכיבוי של Wi-Fi", _c.WifiAllowToggle, v => _c.WifiAllowToggle = v);
            Check("לאפשר התנתקות מרשת", _c.WifiAllowDisconnect, v => _c.WifiAllowDisconnect = v);
            var wl = new TextBox
            {
                Multiline = true, ScrollBars = ScrollBars.Vertical, Font = Ui.F(10), AcceptsReturn = true,
                Text = string.Join(Environment.NewLine, _c.WifiWhitelist), Enabled = _c.WifiWhitelistOn,
            };
            Check("להציג רק רשתות מותרות (רשימה לבנה)", _c.WifiWhitelistOn, v => { _c.WifiWhitelistOn = v; wl.Enabled = v; });
            Note("שם רשת אחד בכל שורה, בדיוק כפי שהוא מופיע (כולל אותיות גדולות וקטנות).");
            wl.SetBounds(Ui.Px(40), _y, _w - Ui.Px(80), Ui.Px(90));
            _body.Controls.Add(wl);
            _y += wl.Height + Ui.Px(6);
            var addCur = Ui.Button("הוסף את הרשת המחוברת כעת");
            addCur.Click += async (s, e) =>
            {
                var st = await WiFi.GetStatus();
                if (st.Connected && !wl.Lines.Contains(st.Ssid))
                    wl.Text = (wl.Text.TrimEnd() + Environment.NewLine + st.Ssid).Trim();
            };
            Place(addCur, Ui.Px(40), Ui.Px(230));

            Section("Bluetooth");
            Check("לאפשר הפעלה וכיבוי של Bluetooth", _c.BtAllowToggle, v => _c.BtAllowToggle = v);
            Check("לאפשר התחברות והתנתקות מהתקנים מצומדים", _c.BtAllowConnect, v => _c.BtAllowConnect = v);
            Check("לאפשר צימוד התקנים חדשים", _c.BtAllowPair, v => _c.BtAllowPair = v);
            Check("לאפשר הסרת התקנים מצומדים", _c.BtAllowUnpair, v => _c.BtAllowUnpair = v);

            Section("תצוגה");
            var corners = new[] { ("BottomRight", "למטה מימין"), ("BottomLeft", "למטה משמאל"), ("TopRight", "למעלה מימין"), ("TopLeft", "למעלה משמאל") };
            var corner = Combo(corners.Select(x => x.Item2).ToArray(), Array.FindIndex(corners, x => x.Item1 == _c.Corner));
            corner.SelectedIndexChanged += (s, e) => _c.Corner = corners[Math.Max(0, corner.SelectedIndex)].Item1;
            Line("מיקום הפס", corner);
            var sizes = new[] { 100, 125, 150, 175, 200 };
            var size = Combo(sizes.Select(x => x + "%").ToArray(), Array.IndexOf(sizes, _c.ScalePct));
            size.SelectedIndexChanged += (s, e) => _c.ScalePct = sizes[Math.Max(0, size.SelectedIndex)];
            Line("גודל", size);
            Check("כששורת המשימות של Windows מוצגת, הפס יושב מעליה", _c.SitAboveTaskbar, v => _c.SitAboveTaskbar = v);
            Note("אם לא מסומן (ברירת המחדל), הפס מכסה את המגש המקורי ומחליף אותו. אחרי לחיצה על מקש Windows שורת המשימות עולה מעליו כל עוד תפריט התחל פתוח, והפס חוזר מיד כשהתפריט נסגר. אם מסומן, הפס יושב מעל שורת המשימות ואף פעם לא מוסתר, גם כשהיא מוסתרת אוטומטית וקופצת.");

            Section("מסך הנעילה");
            bool lockNetWas = LockScreen.HideNetwork, lockNet = lockNetWas;
            Check("להסתיר את כפתור הרשת (Wi-Fi) במסך הנעילה והכניסה", lockNet, v => lockNet = v);
            Note("הפס עצמו לא יכול להופיע במסך הנעילה, כי Windows מציג אותו על שולחן עבודה נפרד ומוגן. ההגדרה הזו מפעילה מדיניות של Windows שמסירה משם את כפתור הרשת, כך שאי אפשר לשנות את חיבור הרשת בלי להיכנס למשתמש. היא חלה על כל המשתמשים במחשב, ובשמירה Windows יבקש הרשאות מנהל.");

            Section("מערכת");
            bool autoStart = false;
            try { autoStart = ConfigStore.AutoStart; } catch { }
            Check("הפעלה אוטומטית עם כניסת המשתמש הנוכחי", autoStart, v => { try { ConfigStore.AutoStart = v; } catch { } });
            Check("הפעלה מחדש אוטומטית אם התוכנה נסגרת (שומר)", _c.Watchdog, v => _c.Watchdog = v);
            Note("קובץ ההגדרות: " + ConfigStore.FilePath);

            var pwd = Ui.Button("החלפת סיסמת מנהל");
            pwd.Click += (s, e) => ChangePassword();
            var exit = Ui.Button("יציאה מהתוכנה");
            exit.ForeColor = Ui.Error;
            exit.Click += (s, e) =>
            {
                if (!Msg.YesNo(this, "לסגור את " + Program.Title + "?\nהפס ייעלם עד ההפעלה הבאה.")) return;
                ExitRequested = true;
                DialogResult = DialogResult.Cancel;
            };
            _y += Ui.Px(4);
            Place(pwd, Ui.Px(16), Ui.Px(180), false);
            Place(exit, Ui.Px(16) + Ui.Px(190), Ui.Px(160));
            _y += Ui.Px(8);
            var spacer = new Label { Location = new Point(0, _y), Size = new Size(1, 1) };
            _body.Controls.Add(spacer);

            // bottom bar
            var ok = Ui.Button("שמירה", true);
            var cancel = Ui.Button("ביטול");
            ok.SetBounds(Ui.Px(16), bodyH + Ui.Px(12), Ui.Px(110), Ui.Px(34));
            cancel.SetBounds(ok.Right + Ui.Px(8), ok.Top, Ui.Px(110), Ui.Px(34));
            ok.Click += (s, e) =>
            {
                _c.WifiWhitelist = wl.Lines.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
                if (_c.WifiWhitelistOn && _c.WifiWhitelist.Count == 0 &&
                    !Msg.YesNo(this, "הרשימה הלבנה ריקה, כך שאף רשת לא תוצג. להמשיך?")) return;
                if (lockNet != lockNetWas)
                {
                    string err = LockScreen.Apply(lockNet);
                    if (err != null) Msg.Info(this, err);
                }
                Result = _c;
                DialogResult = DialogResult.OK;
            };
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = null;
            CancelButton = cancel;
            ClientSize = new Size(_w, ok.Bottom + Ui.Px(12));
        }

        void ChangePassword()
        {
            string p1 = AskDialog.Ask(this, "סיסמה חדשה", "סיסמת מנהל חדשה:", true);
            if (string.IsNullOrEmpty(p1)) return;
            string p2 = AskDialog.Ask(this, "סיסמה חדשה", "הקלד שוב:", true);
            if (p1 != p2) { Msg.Info(this, "הסיסמאות לא זהות"); return; }
            Pwd.Set(_c, p1);
            Msg.Info(this, "הסיסמה תוחלף בלחיצה על שמירה.");
        }

        void Section(string title)
        {
            if (_y > Ui.Px(20)) _y += Ui.Px(10);
            var l = new Label
            {
                Text = title, Font = Ui.F(11, FontStyle.Bold), ForeColor = Ui.Accent, AutoSize = false,
                Location = new Point(Ui.Px(16), _y), Size = new Size(_w - Ui.Px(48), Ui.Px(26)), UseMnemonic = false,
            };
            _body.Controls.Add(l);
            _y += l.Height + Ui.Px(2);
        }

        void Check(string text, bool value, Action<bool> set, int indent = 0)
        {
            var cb = new CheckBox
            {
                Text = text, Checked = value, AutoSize = false, Font = Ui.F(10), UseMnemonic = false,
                // the scroll panel is not mirrored: indent by narrowing from the right
                Location = new Point(Ui.Px(24), _y), Size = new Size(_w - Ui.Px(64 + indent * 22), Ui.Px(26)),
            };
            cb.CheckedChanged += (s, e) => set(cb.Checked);
            _body.Controls.Add(cb);
            _y += cb.Height;
        }

        void Note(string text)
        {
            var l = new Label
            {
                Text = text, Font = Ui.F(8.5f), ForeColor = Ui.Sub, AutoSize = false, UseMnemonic = false,
                Location = new Point(Ui.Px(40), _y), Size = new Size(_w - Ui.Px(80), Ui.Px(20)),
            };
            var sz = TextRenderer.MeasureText(text, l.Font, new Size(l.Width, 1000), TextFormatFlags.WordBreak);
            l.Height = sz.Height + Ui.Px(2);
            _body.Controls.Add(l);
            _y += l.Height + Ui.Px(2);
        }

        void Line(string label, Control c)
        {
            var l = new Label
            {
                Text = label, Font = Ui.F(10), AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
                Location = new Point(Ui.Px(24), _y), Size = new Size(Ui.Px(170), Ui.Px(30)),
            };
            c.Location = new Point(Ui.Px(200), _y + (Ui.Px(30) - c.Height) / 2);
            _body.Controls.Add(l);
            _body.Controls.Add(c);
            _y += Ui.Px(34);
        }

        ComboBox Combo(string[] items, int index)
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.Px(150), Font = Ui.F(10) };
            cb.Items.AddRange(items);
            cb.SelectedIndex = Math.Max(0, index);
            return cb;
        }

        void Place(Button b, int x, int width, bool advance = true)
        {
            b.SetBounds(x, _y, width, Ui.Px(34));
            _body.Controls.Add(b);
            if (advance) _y += b.Height + Ui.Px(6);
        }
    }
}
