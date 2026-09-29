using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VirtualSystemBar
{
    static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(243, 243, 243);
        public static readonly Color PopupBg = Color.FromArgb(249, 249, 249);
        public static readonly Color Hover = Color.FromArgb(226, 226, 226);
        public static readonly Color Selected = Color.FromArgb(232, 240, 250);
        public static readonly Color Border = Color.FromArgb(200, 200, 200);
        public static readonly Color Text = Color.FromArgb(26, 26, 26);
        public static readonly Color Sub = Color.FromArgb(96, 96, 96);
        public static readonly Color Faint = Color.FromArgb(160, 160, 160);
        public static readonly Color Accent = Color.FromArgb(0, 103, 192);
        public static readonly Color Error = Color.FromArgb(196, 43, 28);

        static readonly string IconFamily = new InstalledFontCollection().Families.Any(f => f.Name == "Segoe Fluent Icons")
            ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

        /// <summary>Pixel scale: screen DPI times the admin's size setting.</summary>
        public static float S { get; private set; } = 1;
        static float _user = 1;

        public static void SetScale(int pct)
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f * pct / 100f;
            _user = pct / 100f;
        }

        public static int Px(float v) => (int)Math.Round(v * S);
        public static Font F(float pt, FontStyle st = FontStyle.Regular) => new Font("Segoe UI", pt * _user, st);
        public static Font Icons(float pt) => new Font(IconFamily, pt * _user);

        public const TextFormatFlags RtlText = TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Label Label(string text, float pt = 10, FontStyle st = FontStyle.Regular, Color? color = null)
        {
            return new Label
            {
                Text = text, Font = F(pt, st), ForeColor = color ?? Text, AutoSize = false,
                // with RightToLeft=Yes WinForms mirrors the alignment, so "Left" lands on the right
                RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
            };
        }

        public static Button Button(string text, bool primary = false)
        {
            var b = new Button
            {
                Text = text, Font = F(10), FlatStyle = FlatStyle.Flat, RightToLeft = RightToLeft.Yes, UseMnemonic = false,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Text, Cursor = Cursors.Hand,
                Height = Px(34), UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0, 90, 170) : Hover;
            return b;
        }
    }

    /// <summary>Glyph codes shared by the MDL2 and Fluent icon fonts.</summary>
    static class G
    {
        public const string Mute = "", Vol0 = "", Vol1 = "", Vol2 = "", Vol3 = "";
        public const string Wifi = "", Wifi1 = "", Wifi2 = "", Wifi3 = "", Globe = "", Ethernet = "";
        public const string Bluetooth = "", Keyboard = "", Usb = "", Settings = "", Lock = "";
        public const string Refresh = "", Back = "", Speaker = "", Headphones = "", Device = "";
        public const string Mouse = "", Phone = "", Drive = "", Check = "", Add = "", Close = "";

        public static string Volume(int level, bool mute) =>
            mute || level == 0 ? Mute : level < 34 ? Vol1 : level < 67 ? Vol2 : Vol3;

        public static string WifiBars(int bars) => bars <= 1 ? Wifi1 : bars == 2 ? Wifi2 : bars == 3 ? Wifi3 : Wifi;

        public static string Battery(int pct) => pct >= 100 ? "" : ((char)(0xE850 + Math.Max(0, Math.Min(9, pct / 10)))).ToString();

        public static string ForBtClass(uint cod)
        {
            uint major = (cod >> 8) & 0x1F;
            if (major == 0x04) return Headphones;
            if (major == 0x05) return Keyboard;
            if (major == 0x02) return Phone;
            return Bluetooth;
        }
    }

    /// <summary>On/off switch; mirrored for Hebrew (knob on the left when on).</summary>
    class Toggle : Control
    {
        bool _on;
        public event EventHandler Changed;

        public Toggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(Ui.Px(44), Ui.Px(22));
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        public bool On
        {
            get => _on;
            set { if (_on != value) { _on = value; Invalidate(); } }
        }

        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            On = !On;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var path = Ui.Round(r, r.Height / 2))
            {
                if (_on) using (var b = new SolidBrush(Enabled ? Ui.Accent : Ui.Faint)) g.FillPath(b, path);
                else using (var pen = new Pen(Ui.Sub, Math.Max(1, Ui.S))) g.DrawPath(pen, path);
            }
            int k = r.Height - Ui.Px(8);
            int x = _on ? r.X + Ui.Px(4) : r.Right - Ui.Px(4) - k;
            using (var b = new SolidBrush(_on ? Color.White : Ui.Sub))
                g.FillEllipse(b, x, r.Y + (r.Height - k) / 2, k, k);
        }
    }

    /// <summary>Horizontal slider that fills from the right, like Hebrew Windows.</summary>
    class Slider : Control
    {
        int _value;
        public int Max { get; set; } = 100;
        public event EventHandler Changed;

        public Slider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Height = Ui.Px(28);
            Cursor = Cursors.Hand;
        }

        public int Value
        {
            get => _value;
            set { value = Math.Max(0, Math.Min(Max, value)); if (_value != value) { _value = value; Invalidate(); } }
        }

        int Knob => Ui.Px(18);

        void SetFromMouse(int x)
        {
            int span = Width - Knob;
            int v = (int)Math.Round((span - (x - Knob / 2)) * 100.0 / span);
            v = Math.Max(0, Math.Min(Max, v));
            if (v != _value) { Value = v; Changed?.Invoke(this, EventArgs.Empty); }
        }

        protected override void OnMouseDown(MouseEventArgs e) { Capture = true; SetFromMouse(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { if (Capture) SetFromMouse(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { Capture = false; }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int v = Math.Max(0, Math.Min(Max, _value + Math.Sign(e.Delta) * 2));
            if (v != _value) { Value = v; Changed?.Invoke(this, EventArgs.Empty); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Ui.PopupBg);
            int span = Width - Knob;
            int cy = Height / 2, th = Math.Max(2, Ui.Px(4));
            int kx = Knob / 2 + span - span * _value / 100;   // knob centre
            using (var b = new SolidBrush(Ui.Faint)) g.FillRectangle(b, Knob / 2, cy - th / 2, span, th);
            using (var b = new SolidBrush(Ui.Accent)) g.FillRectangle(b, kx, cy - th / 2, Width - Knob / 2 - kx, th);
            if (Max < 100)
            {
                // the admin's cap: the rest of the track is out of reach
                int cx = Knob / 2 + span - span * Max / 100;
                using (var b = new SolidBrush(Color.FromArgb(215, 215, 215))) g.FillRectangle(b, Knob / 2, cy - th / 2, cx - Knob / 2, th);
            }
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - Knob / 2, cy - Knob / 2, Knob, Knob);
            using (var pen = new Pen(Ui.Border)) g.DrawEllipse(pen, kx - Knob / 2, cy - Knob / 2, Knob, Knob);
            using (var b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, kx - Knob / 4, cy - Knob / 4, Knob / 2, Knob / 2);
        }
    }

    /// <summary>A clickable list row: icon on the right, title and a grey second line.</summary>
    class Row : Control
    {
        public string Glyph;
        public string Title;
        public string Subtitle;
        public bool Selected;
        public bool Dim;
        bool _hover;

        public Row()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Height = Ui.Px(48);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Ui.PopupBg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (Selected || _hover)
                using (var path = Ui.Round(r, Ui.Px(4)))
                using (var b = new SolidBrush(Selected ? Ui.Selected : Ui.Hover)) g.FillPath(b, path);
            int icon = Ui.Px(40);
            var fg = Dim ? Ui.Faint : Ui.Text;
            using (var f = Ui.Icons(13))
                TextRenderer.DrawText(g, Glyph ?? "", f, new Rectangle(Width - icon, 0, icon, Height), fg, Ui.Center);
            var textR = new Rectangle(Ui.Px(8), 0, Width - icon - Ui.Px(12), Height);
            if (string.IsNullOrEmpty(Subtitle))
                using (var f = Ui.F(10)) TextRenderer.DrawText(g, Title, f, textR, fg, Ui.RtlText);
            else
            {
                var top = new Rectangle(textR.X, Ui.Px(5), textR.Width, Height / 2 - Ui.Px(3));
                var bot = new Rectangle(textR.X, Height / 2, textR.Width, Height / 2 - Ui.Px(5));
                using (var f = Ui.F(10)) TextRenderer.DrawText(g, Title, f, top, fg, Ui.RtlText);
                using (var f = Ui.F(8.5f)) TextRenderer.DrawText(g, Subtitle, f, bot, Ui.Sub, Ui.RtlText);
            }
        }
    }

    /// <summary>Small borderless icon button (refresh, back, mute).</summary>
    class IconButton : Control
    {
        public string Glyph;
        public float Pt = 12;
        bool _hover;

        public IconButton(string glyph)
        {
            Glyph = glyph;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Size = new Size(Ui.Px(34), Ui.Px(34));
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Ui.PopupBg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_hover && Enabled)
                using (var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Ui.Px(4)))
                using (var b = new SolidBrush(Ui.Hover)) g.FillPath(b, path);
            using (var f = Ui.Icons(Pt))
                TextRenderer.DrawText(g, Glyph, f, ClientRectangle, Enabled ? Ui.Text : Ui.Faint, Ui.Center);
        }
    }

    /// <summary>
    /// Flyout that opens above (or below) a bar item and closes when it loses focus.
    /// Content is laid out top to bottom with Add(); call Rebuild() to redraw it all.
    /// </summary>
    class Popup : Form
    {
        protected readonly AppConfig Cfg;
        protected int W => Ui.Px(340);
        protected int Pad => Ui.Px(14);
        int _y;
        Rectangle _anchor;
        bool _below;
        public bool Hold;   // true while a dialog of ours is open, so losing focus doesn't close us

        public Popup(AppConfig cfg)
        {
            Cfg = cfg;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Ui.PopupBg;
            Font = Ui.F(10);
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW: not in Alt+Tab
                cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW
                return cp;
            }
        }

        public void ShowAt(Rectangle anchor, bool below)
        {
            _anchor = anchor;
            _below = below;
            Rebuild();
            Show();
            Activate();
        }

        void Place()
        {
            var scr = Screen.FromRectangle(_anchor).Bounds;
            int gap = Ui.Px(8);
            int x = Math.Max(scr.Left + gap, Math.Min(_anchor.Right - Width, scr.Right - Width - gap));
            int y = _below ? _anchor.Bottom + gap : _anchor.Top - Height - gap;
            y = Math.Max(scr.Top + gap, Math.Min(y, scr.Bottom - Height - gap));
            Location = new Point(x, y);
        }

        public void Rebuild()
        {
            if (IsDisposed) return;
            SuspendLayout();
            foreach (Control c in Controls.Cast<Control>().ToList()) c.Dispose();
            Controls.Clear();
            _y = Pad;
            Build();
            ClientSize = new Size(W, _y + Pad - Ui.Px(6));
            ResumeLayout();
            Place();
            Invalidate();
        }

        protected virtual void Build() { }

        /// <summary>Adds a control across the full width (or at its own width, right-aligned) on the next line.</summary>
        protected T Add<T>(T c, int height = 0, bool fullWidth = true) where T : Control
        {
            if (height > 0) c.Height = height;
            if (fullWidth) c.Width = W - Pad * 2;
            c.Location = new Point(W - Pad - c.Width, _y);
            Controls.Add(c);
            _y += c.Height + Ui.Px(6);
            return c;
        }

        /// <summary>Header: title on the right, optional controls on the left.</summary>
        protected void Header(string title, params Control[] left)
        {
            int h = Ui.Px(34);
            int x = Pad;
            foreach (var c in left)
            {
                c.Location = new Point(x, _y + (h - c.Height) / 2);
                Controls.Add(c);
                x += c.Width + Ui.Px(8);
            }
            var lbl = Ui.Label(title, 11.5f, FontStyle.Bold);
            lbl.SetBounds(x, _y, W - Pad - x, h);
            Controls.Add(lbl);
            _y += h + Ui.Px(6);
        }

        protected void Space(int px) => _y += px;

        /// <summary>A scrolling list of rows, at most maxRows tall.</summary>
        protected Panel List(int count, int maxRows = 5)
        {
            int rowH = Ui.Px(48);
            var p = new Panel { AutoScroll = true, BackColor = Ui.PopupBg };
            Add(p, Math.Max(1, Math.Min(count, maxRows)) * rowH + 2);
            return p;
        }

        protected static void FillList(Panel p, System.Collections.Generic.IEnumerable<Row> rows)
        {
            int y = 0;
            bool scroll = rows.Count() * Ui.Px(48) > p.Height;
            int w = p.Width - (scroll ? SystemInformation.VerticalScrollBarWidth : 0);
            foreach (var r in rows)
            {
                r.SetBounds(scroll ? SystemInformation.VerticalScrollBarWidth : 0, y, w, Ui.Px(48));
                p.Controls.Add(r);
                y += r.Height;
            }
        }

        protected Label Message(string text, bool error = false)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var l = Ui.Label(text, 9, FontStyle.Regular, error ? Ui.Error : Ui.Sub);
            l.TextAlign = ContentAlignment.TopLeft;
            using (var g = CreateGraphics())
            {
                var sz = TextRenderer.MeasureText(g, text, l.Font, new Size(W - Pad * 2, 1000), TextFormatFlags.WordBreak | TextFormatFlags.RightToLeft);
                return Add(l, sz.Height + Ui.Px(4));
            }
        }

        /// <summary>Row of buttons, right to left.</summary>
        protected void Buttons(params Button[] buttons)
        {
            int x = W - Pad;
            int h = Ui.Px(34);
            foreach (var b in buttons)
            {
                b.Width = Math.Max(Ui.Px(90), TextRenderer.MeasureText(b.Text, b.Font).Width + Ui.Px(24));
                x -= b.Width;
                b.Location = new Point(x, _y);
                Controls.Add(b);
                x -= Ui.Px(8);
            }
            _y += h + Ui.Px(6);
        }

        /// <summary>Runs a dialog without this popup closing underneath it.</summary>
        public T Hold_<T>(Func<T> f)
        {
            Hold = true;
            try { return f(); }
            finally { Hold = false; if (!IsDisposed) Activate(); }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!Hold) BeginInvoke(new Action(Close));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Ui.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>Plain modal dialog for a short text (password, PIN). No links, no help button.</summary>
    class AskDialog : Form
    {
        readonly TextBox _box;
        public string Value => _box.Text;

        public AskDialog(string title, string prompt, bool secret, string ok = "אישור")
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            BackColor = Ui.PopupBg;
            AutoScaleMode = AutoScaleMode.None;
            int w = Ui.Px(340), pad = Ui.Px(16);

            var lbl = Ui.Label(prompt, 10);
            lbl.TextAlign = ContentAlignment.TopLeft;
            var sz = TextRenderer.MeasureText(prompt, lbl.Font, new Size(w - pad * 2, 1000), TextFormatFlags.WordBreak);
            lbl.SetBounds(pad, pad, w - pad * 2, sz.Height + Ui.Px(4));
            _box = new TextBox { Font = Ui.F(11), UseSystemPasswordChar = secret, RightToLeft = RightToLeft.Yes, ShortcutsEnabled = false };
            _box.SetBounds(pad, lbl.Bottom + Ui.Px(8), w - pad * 2, Ui.Px(30));
            var okB = Ui.Button(ok, true);
            var cancel = Ui.Button("ביטול");
            okB.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            okB.SetBounds(pad, _box.Bottom + Ui.Px(14), Ui.Px(100), Ui.Px(34));
            cancel.SetBounds(okB.Right + Ui.Px(8), okB.Top, Ui.Px(100), Ui.Px(34));
            Controls.AddRange(new Control[] { lbl, _box, okB, cancel });
            AcceptButton = okB;
            CancelButton = cancel;
            ClientSize = new Size(w, okB.Bottom + pad);
        }

        public static string Ask(IWin32Window owner, string title, string prompt, bool secret)
        {
            using (var d = new AskDialog(title, prompt, secret))
                return d.ShowDialog(owner) == DialogResult.OK ? d.Value : null;
        }
    }

    static class Msg
    {
        public static void Info(IWin32Window owner, string text) =>
            MessageBox.Show(owner, text, Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

        public static bool YesNo(IWin32Window owner, string text) =>
            MessageBox.Show(owner, text, Program.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) == DialogResult.Yes;
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    }
}
