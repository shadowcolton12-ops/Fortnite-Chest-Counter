// Chest Counter - C# Windows Forms (.NET 6 or newer)
// Dark purple theme with animated gradient waves and glass-style cards.
// Replace the contents of Program.cs in your Windows Forms project with this file.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace ChestCounter
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    // Saved to chest_counter.json next to the .exe
    public class Settings
    {
        public int Count { get; set; } = 0;
        public Keys IncreaseKey { get; set; } = Keys.F1;
        public Keys DecreaseKey { get; set; } = Keys.F2;
    }

    // ------------------------------------------------------------------
    // Global keyboard hook (keys are observed, not blocked)
    // ------------------------------------------------------------------
    public sealed class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private readonly LowLevelKeyboardProc _proc; // keep a reference so it isn't garbage collected
        private IntPtr _hookId = IntPtr.Zero;

        public event Action<Keys, bool> KeyEvent; // (key, isDown)

        public KeyboardHook()
        {
            _proc = HookCallback;
            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
            {
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                Keys key = (Keys)Marshal.ReadInt32(lParam);

                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) KeyEvent?.Invoke(key, true);
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP) KeyEvent?.Invoke(key, false);
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
    }

    // ------------------------------------------------------------------
    // Theme + custom-drawn controls (all transparent so the waves show through)
    // ------------------------------------------------------------------
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(12, 10, 24);
        public static readonly Color Accent = Color.FromArgb(124, 92, 255);
        public static readonly Color AccentHover = Color.FromArgb(150, 122, 255);
        public static readonly Color Secondary = Color.FromArgb(58, 46, 96);
        public static readonly Color SecondaryHover = Color.FromArgb(80, 64, 128);
        public static readonly Color Text = Color.FromArgb(240, 242, 248);
        public static readonly Color Muted = Color.FromArgb(176, 168, 208);

        // Glass card look
        public static readonly Color Glass = Color.FromArgb(165, 18, 14, 34);
        public static readonly Color GlassBorder = Color.FromArgb(55, 170, 140, 255);

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = Math.Max(1, radius * 2);
            d = Math.Min(d, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }

    // Translucent rounded card.
    public class RoundedPanel : Panel
    {
        public Color Fill { get; set; } = Theme.Glass;
        public Color Border { get; set; } = Theme.GlassBorder;
        public int Radius { get; set; } = 16;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (var brush = new SolidBrush(Fill))
            {
                e.Graphics.FillPath(brush, path);
                if (Border.A > 0)
                    using (var pen = new Pen(Border, 1f))
                        e.Graphics.DrawPath(pen, path);
            }
        }
    }

    // Flat rounded button with hover / pressed states.
    public class RoundButton : Control
    {
        public Color FillColor { get; set; } = Theme.Secondary;
        public Color HoverColor { get; set; } = Theme.SecondaryHover;
        public int Radius { get; set; } = 10;

        private bool _hover, _down;

        public RoundButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI Semibold", 10f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill = _down ? Theme.Lerp(HoverColor, Color.Black, 0.2f) : (_hover ? HoverColor : FillColor);
            using (var path = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    // Shows a key name; glows accent colour while waiting for a new key.
    public class KeyChip : Control
    {
        public bool Active { get; set; }

        public KeyChip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = new Font("Consolas", 10f, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var path = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), 8))
            using (var fill = new SolidBrush(Color.FromArgb(200, Theme.Bg)))
            using (var pen = new Pen(Active ? Theme.Accent : Color.FromArgb(120, Theme.Secondary), 1.5f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Active ? Theme.AccentHover : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    // Pill-shaped on/off switch.
    public class ToggleSwitch : Control
    {
        private bool _checked;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get => _checked;
            set { _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
        }

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Size = new Size(44, 24);
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var track = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
            using (var trackBrush = new SolidBrush(_checked ? Theme.Accent : Theme.Secondary))
                g.FillPath(trackBrush, track);

            int d = Height - 6;
            int x = _checked ? Width - 3 - d : 3;
            using (var knob = new SolidBrush(Color.White))
                g.FillEllipse(knob, x, 3, d, d);
        }
    }

    // ------------------------------------------------------------------
    // Main window
    // ------------------------------------------------------------------
    public class MainForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private static readonly string SavePath =
            Path.Combine(AppContext.BaseDirectory, "chest_counter.json");

        private Settings _settings;
        private KeyboardHook _hook;
        private readonly HashSet<Keys> _held = new HashSet<Keys>(); // ignores key auto-repeat
        private string _capturing = null; // "inc", "dec" or null

        private Label _countLabel, _statusLabel;
        private KeyChip _incChip, _decChip;
        private TextBox _startBox;

        private readonly Font _bigFont = new Font("Segoe UI Semibold", 54f);
        private readonly Font _midFont = new Font("Segoe UI Semibold", 38f);
        private readonly System.Windows.Forms.Timer _flashTimer = new System.Windows.Forms.Timer { Interval = 15 };
        private int _flashStep = 0;
        private const int FlashSteps = 12;

        // ----- animated waves -----
        private struct WaveDef
        {
            public float Base;       // vertical position as a fraction of window height
            public float Amp;        // wave height in pixels
            public float Wavelength; // pixels per cycle
            public float Speed;      // radians per second (negative = moves the other way)
            public int Alpha;        // lower = more transparent
            public Color Color;
        }

        // Back waves are more transparent, front waves are less transparent.
        private static readonly WaveDef[] Waves =
        {
            new WaveDef { Base = 0.30f, Amp = 18, Wavelength = 260, Speed =  0.60f, Alpha =  45, Color = Color.FromArgb(150, 100, 255) },
            new WaveDef { Base = 0.46f, Amp = 22, Wavelength = 330, Speed = -0.45f, Alpha =  70, Color = Color.FromArgb(124,  92, 255) },
            new WaveDef { Base = 0.62f, Amp = 20, Wavelength = 230, Speed =  0.80f, Alpha = 105, Color = Color.FromArgb(104,  62, 224) },
            new WaveDef { Base = 0.78f, Amp = 16, Wavelength = 290, Speed = -0.70f, Alpha = 140, Color = Color.FromArgb( 84,  42, 194) },
            new WaveDef { Base = 0.90f, Amp = 12, Wavelength = 210, Speed =  1.00f, Alpha = 175, Color = Color.FromArgb( 64,  30, 154) },
        };

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer _waveTimer = new System.Windows.Forms.Timer { Interval = 33 };

        public MainForm()
        {
            _settings = LoadSettings();
            BuildUi();
            RefreshUi();

            _flashTimer.Tick += (s, e) =>
            {
                _flashStep--;
                _countLabel.ForeColor = Theme.Lerp(Theme.Accent, Color.White, _flashStep / (float)FlashSteps);
                if (_flashStep <= 0) _flashTimer.Stop();
            };

            _waveTimer.Tick += (s, e) =>
            {
                if (WindowState != FormWindowState.Minimized) Invalidate(true);
            };
            _waveTimer.Start();

            _hook = new KeyboardHook();
            _hook.KeyEvent += OnGlobalKey;

            FormClosing += (s, e) =>
            {
                _waveTimer.Stop();
                _flashTimer.Stop();
                SaveSettings();
                _hook.Dispose();
            };
        }

        // Composited painting removes flicker when the background animates behind child controls.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                // Dark title bar (Windows 10 20H1+ / Windows 11)
                int on = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
            }
            catch { /* older Windows: keep the default title bar */ }
        }

        // ---------- animated background ----------
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            DrawBackground(e.Graphics);
        }

        private void DrawBackground(Graphics g)
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            // Black -> deep purple gradient
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.Black, Color.Black, 90f))
            {
                bg.InterpolationColors = new ColorBlend
                {
                    Colors = new[]
                    {
                        Color.FromArgb(8, 6, 16),
                        Color.FromArgb(26, 13, 56),
                        Color.FromArgb(50, 24, 108)
                    },
                    Positions = new[] { 0f, 0.55f, 1f }
                };
                g.FillRectangle(bg, 0, 0, w, h);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            float t = (float)_clock.Elapsed.TotalSeconds;
            const int step = 6;
            int n = w / step + 2;

            foreach (var wv in Waves)
            {
                float baseY = wv.Base * h;
                float phase = t * wv.Speed;

                var pts = new PointF[n + 2];
                for (int i = 0; i < n; i++)
                {
                    float x = Math.Min(i * step, w);
                    float k = (float)(2 * Math.PI * x / wv.Wavelength);
                    float y = baseY
                              + wv.Amp * (float)Math.Sin(k + phase)
                              + wv.Amp * 0.35f * (float)Math.Sin(k * 1.9f - phase * 1.4f);
                    pts[i] = new PointF(x, y);
                }
                pts[n] = new PointF(w, h);
                pts[n + 1] = new PointF(0, h);

                float top = baseY - wv.Amp * 1.5f;
                Color topColor = Color.FromArgb(wv.Alpha, wv.Color);
                Color bottomColor = Color.FromArgb(wv.Alpha / 2, Theme.Lerp(wv.Color, Color.Black, 0.75f));

                using (var fill = new LinearGradientBrush(new RectangleF(0, top, w, h - top), topColor, bottomColor, 90f))
                    g.FillPolygon(fill, pts);

                // Soft glowing crest line
                var crest = new PointF[n];
                Array.Copy(pts, crest, n);
                using (var pen = new Pen(Color.FromArgb(Math.Min(255, wv.Alpha / 2 + 25), Theme.AccentHover), 1.5f))
                    g.DrawLines(pen, crest);
            }
        }

        // ---------- UI ----------
        private Label MakeLabel(string text, Font font, Color fore, Rectangle bounds,
                                ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = fore,
                BackColor = Color.Transparent,
                Bounds = bounds,
                TextAlign = align,
                AutoSize = false
            };
        }

        private void BuildUi()
        {
            Text = "Chest Counter";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(340, 600);
            BackColor = Theme.Bg;
            DoubleBuffered = true;

            var small = new Font("Segoe UI Semibold", 8.5f);
            var body = new Font("Segoe UI", 10f);
            var tips = new ToolTip();

            // Header
            Controls.Add(MakeLabel("CHEST COUNTER", new Font("Segoe UI Semibold", 10f),
                Theme.Muted, new Rectangle(0, 16, 340, 24), ContentAlignment.MiddleCenter));

            // ----- Count card -----
            var countCard = new RoundedPanel { Location = new Point(20, 50), Size = new Size(300, 200) };
            countCard.Controls.Add(MakeLabel("CHESTS OPENED", small, Theme.Muted,
                new Rectangle(0, 16, 300, 20), ContentAlignment.MiddleCenter));

            _countLabel = MakeLabel("0", _bigFont, Theme.Accent,
                new Rectangle(0, 40, 300, 84), ContentAlignment.MiddleCenter);
            countCard.Controls.Add(_countLabel);

            var minus = new RoundButton { Text = "\u2212", Font = new Font("Segoe UI Semibold", 16f), Bounds = new Rectangle(50, 140, 90, 42) };
            var plus = new RoundButton
            {
                Text = "+",
                Font = new Font("Segoe UI Semibold", 16f),
                Bounds = new Rectangle(160, 140, 90, 42),
                FillColor = Theme.Accent,
                HoverColor = Theme.AccentHover
            };
            minus.Click += (s, e) => Change(-1);
            plus.Click += (s, e) => Change(1);
            countCard.Controls.Add(minus);
            countCard.Controls.Add(plus);
            Controls.Add(countCard);

            // ----- Starting point card -----
            var setCard = new RoundedPanel { Location = new Point(20, 266), Size = new Size(300, 104) };
            setCard.Controls.Add(MakeLabel("STARTING POINT", small, Theme.Muted,
                new Rectangle(16, 14, 200, 18)));

            var inputWrap = new RoundedPanel
            {
                Location = new Point(16, 44),
                Size = new Size(150, 44),
                Fill = Color.FromArgb(235, Theme.Bg),
                Radius = 10
            };
            _startBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Bg,
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 13f),
                TextAlign = HorizontalAlignment.Center,
                MaxLength = 9,
                Width = 130,
                Left = 10
            };
            _startBox.Top = (inputWrap.Height - _startBox.Height) / 2;
            _startBox.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
            };
            _startBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { SetFromTextBox(); e.SuppressKeyPress = true; }
            };
            inputWrap.Controls.Add(_startBox);
            setCard.Controls.Add(inputWrap);

            var setBtn = new RoundButton
            {
                Text = "Set",
                Bounds = new Rectangle(174, 44, 56, 44),
                FillColor = Theme.Accent,
                HoverColor = Theme.AccentHover
            };
            var resetBtn = new RoundButton { Text = "Reset", Bounds = new Rectangle(236, 44, 48, 44), Font = new Font("Segoe UI Semibold", 8.5f) };
            setBtn.Click += (s, e) => SetFromTextBox();
            resetBtn.Click += (s, e) => { SetCount(0); _statusLabel.Text = "Count reset to 0."; };
            tips.SetToolTip(resetBtn, "Reset the counter to 0");
            setCard.Controls.Add(setBtn);
            setCard.Controls.Add(resetBtn);
            Controls.Add(setCard);

            // ----- Keybinds card -----
            var bindCard = new RoundedPanel { Location = new Point(20, 386), Size = new Size(300, 132) };
            bindCard.Controls.Add(MakeLabel("KEYBINDS", small, Theme.Muted,
                new Rectangle(16, 14, 200, 18)));

            bindCard.Controls.Add(MakeLabel("Increase", body, Theme.Text, new Rectangle(16, 46, 70, 32)));
            _incChip = new KeyChip { Bounds = new Rectangle(92, 46, 110, 32) };
            var incBtn = new RoundButton { Text = "Change", Bounds = new Rectangle(210, 46, 74, 32), Font = new Font("Segoe UI Semibold", 9f) };
            incBtn.Click += (s, e) => StartCapture("inc");

            bindCard.Controls.Add(MakeLabel("Decrease", body, Theme.Text, new Rectangle(16, 88, 70, 32)));
            _decChip = new KeyChip { Bounds = new Rectangle(92, 88, 110, 32) };
            var decBtn = new RoundButton { Text = "Change", Bounds = new Rectangle(210, 88, 74, 32), Font = new Font("Segoe UI Semibold", 9f) };
            decBtn.Click += (s, e) => StartCapture("dec");

            bindCard.Controls.AddRange(new Control[] { _incChip, incBtn, _decChip, decBtn });
            Controls.Add(bindCard);

            // ----- Footer -----
            Controls.Add(MakeLabel("Always on top", body, Theme.Text, new Rectangle(24, 534, 200, 24)));
            var toggle = new ToggleSwitch { Location = new Point(276, 534) };
            toggle.CheckedChanged += (s, e) => TopMost = toggle.Checked;
            Controls.Add(toggle);

            _statusLabel = MakeLabel("", new Font("Segoe UI", 9f), Theme.Muted,
                new Rectangle(20, 568, 300, 22), ContentAlignment.MiddleCenter);
            Controls.Add(_statusLabel);
        }

        private void RefreshUi()
        {
            string text = _settings.Count.ToString();
            _countLabel.Font = text.Length <= 5 ? _bigFont : _midFont;
            _countLabel.Text = text;

            _incChip.Text = _capturing == "inc" ? "Press a key…" : FriendlyKey(_settings.IncreaseKey);
            _incChip.Active = _capturing == "inc";
            _incChip.Invalidate();

            _decChip.Text = _capturing == "dec" ? "Press a key…" : FriendlyKey(_settings.DecreaseKey);
            _decChip.Active = _capturing == "dec";
            _decChip.Invalidate();
        }

        private static string FriendlyKey(Keys k)
        {
            string s = k.ToString();
            if (s.Length == 2 && s[0] == 'D' && char.IsDigit(s[1])) return s.Substring(1);
            switch (k)
            {
                case Keys.Oemtilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.Prior: return "PAGE UP";
                case Keys.Next: return "PAGE DOWN";
                case Keys.LControlKey: return "L CTRL";
                case Keys.RControlKey: return "R CTRL";
                case Keys.LShiftKey: return "L SHIFT";
                case Keys.RShiftKey: return "R SHIFT";
                case Keys.LMenu: return "L ALT";
                case Keys.RMenu: return "R ALT";
            }
            return s.ToUpper();
        }

        private void Flash()
        {
            _flashStep = FlashSteps;
            _countLabel.ForeColor = Color.White;
            _flashTimer.Start();
        }

        // ---------- counter ----------
        private void Change(int delta) => SetCount(Math.Max(0, _settings.Count + delta));

        private void SetCount(int value)
        {
            bool changed = value != _settings.Count;
            _settings.Count = value;
            SaveSettings();
            RefreshUi();
            if (changed) Flash();
        }

        private void SetFromTextBox()
        {
            if (int.TryParse(_startBox.Text.Trim(), out int value) && value >= 0)
            {
                SetCount(value);
                _startBox.Clear();
                _statusLabel.Text = $"Count set to {value}.";
                ActiveControl = null; // give focus back so hotkeys work with this window focused
            }
            else
            {
                _statusLabel.Text = "Enter a whole number, 0 or higher.";
            }
        }

        // ---------- keys ----------
        private void StartCapture(string which)
        {
            _capturing = which;
            _statusLabel.Text = "Press the new key… (Esc to cancel)";
            RefreshUi();
        }

        private void OnGlobalKey(Keys key, bool isDown)
        {
            if (!isDown)
            {
                _held.Remove(key);
                return;
            }
            if (!_held.Add(key)) return; // ignore auto-repeat while held

            if (_capturing != null)
            {
                FinishCapture(key);
                return;
            }

            // Don't count while you're typing in this window's number box
            if (ActiveForm == this && _startBox.Focused) return;

            if (key == _settings.IncreaseKey) Change(1);
            else if (key == _settings.DecreaseKey) Change(-1);
        }

        private void FinishCapture(Keys key)
        {
            string which = _capturing;
            _capturing = null;

            if (key == Keys.Escape)
            {
                _statusLabel.Text = "Rebind cancelled.";
                RefreshUi();
                return;
            }

            Keys other = which == "inc" ? _settings.DecreaseKey : _settings.IncreaseKey;
            if (key == other)
            {
                _statusLabel.Text = "That key is already used by the other action.";
                RefreshUi();
                return;
            }

            if (which == "inc") _settings.IncreaseKey = key;
            else _settings.DecreaseKey = key;

            SaveSettings();
            RefreshUi();
            _statusLabel.Text = $"Bound to {FriendlyKey(key)}.";
        }

        // ---------- storage ----------
        private Settings LoadSettings()
        {
            try
            {
                if (File.Exists(SavePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SavePath)) ?? new Settings();
            }
            catch { /* fall back to defaults */ }
            return new Settings();
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllText(SavePath,
                    JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                if (_statusLabel != null) _statusLabel.Text = "Could not save: " + ex.Message;
            }
        }
    }
}