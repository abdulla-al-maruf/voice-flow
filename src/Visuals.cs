using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class Pill : Form
{
    internal static readonly Color Red = Color.FromArgb(255, 126, 116), Amber = Color.FromArgb(246, 194, 111),
        Green = Color.FromArgb(109, 220, 186), Gray = Color.FromArgb(186, 192, 204);
    readonly System.Windows.Forms.Timer hide = new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer motion = new System.Windows.Forms.Timer { Interval = 15 };
    readonly System.Windows.Forms.Timer activity = new System.Windows.Forms.Timer { Interval = 50 };
    readonly System.Windows.Forms.Timer resize = new System.Windows.Forms.Timer { Interval = 15 };
    readonly Stopwatch resizeClock = new Stopwatch();
    int cardW, cardH, fromW, fromH, goalW, goalH;   // the card glides between sizes instead of jumping
    readonly Font body = InstalledFont("Potro Sans Bangla", "Nirmala UI", 17);
    readonly Font heading = InstalledFont("Potro Sans Bangla Medium", "Nirmala UI", 14);
    readonly Font small = InstalledFont("Potro Sans Bangla", "Nirmala UI", 13);
    readonly Stopwatch clock = new Stopwatch();
    readonly Stopwatch activityClock = new Stopwatch();
    readonly bool animate;
    const int Shadow = 12, ContentPadding = 22;
    const TextFormatFlags BodyFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
    static readonly Color Surface = Color.FromArgb(29, 31, 34), SurfaceTop = Color.FromArgb(38, 40, 45), SurfaceBottom = Color.FromArgb(23, 25, 28),
        Ink = Color.FromArgb(232, 233, 230), Muted = Color.FromArgb(160, 166, 173);
    const double CardAlpha = 0.93;   // the desktop shows through a little, so it does not feel like a solid block
    static Brush SurfaceBrush(int height) { return new LinearGradientBrush(new Rectangle(0, 0, 1, Math.Max(1, height)), SurfaceTop, SurfaceBottom, 90f); }
    string message = "", draft = "", hint = "";
    string displayed = "";
    bool listening, working, quiet;
    double activityPhase;
    Color accent = Gray;
    int bodyHeight, textWidth;
    Point anchor;
    Bitmap frame;
    double visibility, from, goal;
    int motionMs;
    internal Rectangle Area = Screen.PrimaryScreen.WorkingArea;
    internal Func<float> Level;            // live microphone loudness 0..1; null = gentle demo motion
    internal Func<bool> ExitIfIdle;
    float shownLevel;
    internal string DisplayedText { get { return displayed; } }
    internal bool MotionRunning { get { return motion.Enabled; } }
    internal double VisibilityAmount { get { return visibility; } }
    internal bool ActivityRunning { get { return activity.Enabled; } }
    internal bool ResizeSettled { get { return !resize.Enabled && cardW == goalW && cardH == goalH; } }
    internal double ActivityPhase { get { return activityPhase; } }

    internal Pill(bool? enableMotion = null)
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        bool clientAnimation;
        animate = enableMotion ?? (SystemParametersInfo(0x1042, 0, out clientAnimation, 0) && clientAnimation && !SystemInformation.TerminalServerSession);
        AccessibleRole = AccessibleRole.StaticText;
        hide.Tick += delegate { Dismiss(); };
        motion.Tick += delegate { AdvanceMotion(); };
        resize.Tick += delegate
        {
            double t = Math.Min(1, resizeClock.Elapsed.TotalMilliseconds / 220);
            double e = 1 - Math.Pow(1 - t, 3);
            cardW = (int)Math.Round(fromW + (goalW - fromW) * e); cardH = (int)Math.Round(fromH + (goalH - fromH) * e);
            Rebuild(); Present();
            if (t >= 1) { resize.Stop(); resizeClock.Stop(); }
        };
        activity.Tick += delegate
        {
            if (!Visible || goal == 0) { activity.Stop(); return; }
            activityPhase = activityClock.Elapsed.TotalSeconds * 5;
            if (Level != null) { float target = Level(); shownLevel += (target - shownLevel) * (target > shownLevel ? 0.6f : 0.18f); }
            using (Graphics g = Graphics.FromImage(frame))
            using (var brush = SurfaceBrush(cardH))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(Shadow, Shadow);
                g.FillRectangle(brush, ContentPadding - 3, 9, Width - 2 * Shadow - 2 * ContentPadding + 6, 28);
                DrawStatus(g, Width - 2 * Shadow);
            }
            Present();
        };
        Text = "VoiceFlow.State:idle";
        if (Handle == IntPtr.Zero) throw new InvalidOperationException("Preview window could not be created.");
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80 | 0x20 | 0x80000; return p; } }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x8051&&m.WParam.ToInt32()==0x4156){m.Result=ExitIfIdle!=null&&ExitIfIdle()?(IntPtr)1:IntPtr.Zero;return;}
        if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }
        if (m.Msg == 0x84) { m.Result = (IntPtr)(-1); return; }
        base.WndProc(ref m);
    }

    internal void Set(string title, Color color, int hideAfterMs, string text = "", string footer = "", bool show = true)
    {
        message = title; accent = color; draft = text ?? "";
        listening = title.EndsWith("শুনছি", StringComparison.Ordinal);
        working = title == "লেখা সম্পূর্ণ হচ্ছে" || title == "লেখা উদ্ধার হচ্ছে";
        quiet = listening || working || title == "লেখা বসেছে" || title == "বাতিল হয়েছে";
        hint = quiet ? "" : footer;
        LayoutPreview();
        hide.Stop();
        if (show)
        {
            if (!Visible) { visibility = 0; Present(); Show(); }
            if (visibility < 1 && (!motion.Enabled || goal == 0)) MoveTo(1);
            else Present();
            if (animate && (listening || working))
            { if (!activity.Enabled) { activityClock.Restart(); activity.Start(); } }
            else { activity.Stop(); activityClock.Stop(); }
        }
        if (show && hideAfterMs > 0) { hide.Interval = hideAfterMs; hide.Start(); }
    }
    internal void Preview(string text)
    {
        if (draft == text) return;
        draft = text ?? ""; LayoutPreview();
        if (Visible) Present();
    }

    void LayoutPreview()
    {
        int width = draft.Length == 0 && quiet ? 116 : Math.Min(448, Math.Max(160, Area.Width - 40));
        textWidth = width - 2 * ContentPadding;
        string shown = Tail(draft);
        int maxHeight = Measure("কথা English\nকথা English\nকথা English");
        if (Measure(shown) > maxHeight)
        {
            int[] elements = StringInfo.ParseCombiningCharacters(shown);
            int low = 0, high = elements.Length - 1;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (Measure("… " + shown.Substring(elements[mid])) <= maxHeight) high = mid;
                else low = mid + 1;
            }
            int offset = elements[low];
            int space = shown.IndexOf(' ', offset);
            shown = "… " + shown.Substring(space >= 0 && space < offset + 24 ? space + 1 : offset);
        }
        displayed = shown;
        bodyHeight = shown.Length == 0 ? 0 : Math.Min(maxHeight, Measure(shown));
        int height = 46 + (bodyHeight > 0 ? bodyHeight + 14 : 0) + (hint.Length > 0 ? 30 : 0);
        AccessibleName = message + ". " + draft;
        goalW = width; goalH = height;
        if (animate && Visible && visibility > 0 && cardW > 0 && (cardW != width || cardH != height))
        {
            fromW = cardW; fromH = cardH;
            resizeClock.Restart(); if (!resize.Enabled) resize.Start();
            Rebuild();                      // redraw at the current size with the new content; the timer glides it
            return;
        }
        resize.Stop(); cardW = width; cardH = height;
        Rebuild();
    }

    void Rebuild()
    {
        Size = new Size(cardW + 2 * Shadow, cardH + 2 * Shadow);
        anchor = new Point(Area.Left + (Area.Width - Width) / 2, Area.Bottom - Height - 18);
        Bitmap old = frame; frame = RenderCard(cardW, cardH); if (old != null) old.Dispose();
    }
    int Measure(string text) { return text.Length == 0 ? 0 : TextRenderer.MeasureText(text, body, new Size(textWidth, 2000), BodyFlags).Height; }

    internal static string Tail(string text)
    {
        if (text.Length <= 420) return text;
        int offset = text.Length - 420;
        int space = text.IndexOf(' ', offset);
        if (space < 0) foreach (int element in StringInfo.ParseCombiningCharacters(text)) if (element >= offset) { offset = element; break; }
        return "… " + text.Substring(space >= 0 ? space + 1 : offset);
    }

    Bitmap RenderCard(int width, int height)
    {
        var result = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(result))
        using (var content = new Bitmap(width, height, PixelFormat.Format24bppRgb))
        using (Graphics c = Graphics.FromImage(content))
        {
            g.SmoothingMode = c.SmoothingMode = SmoothingMode.AntiAlias;
            // Render GDI text on an opaque surface, then mask it; GDI cannot preserve per-pixel alpha.
            using (var surface = SurfaceBrush(height)) c.FillRectangle(surface, 0, 0, width, height);
            DrawStatus(c, width);
            if (bodyHeight > 0) TextRenderer.DrawText(c, displayed, body, new Rectangle(ContentPadding, 44, textWidth, bodyHeight), Ink, BodyFlags);
            if (hint.Length > 0)
            {
                int y = height - 29;
                Label(c, hint, small, new Rectangle(ContentPadding, y - 1, textWidth, 22), Muted);
            }
            for (int spread = 10; spread >= 1; spread--)
                using (var shadow = Rounded(new Rectangle(Shadow - spread, Shadow + 3 - spread, width + 2 * spread, height + 2 * spread), 16 + spread))
                using (var brush = new SolidBrush(Color.FromArgb(3 + (10 - spread) / 3, 0, 0, 0))) g.FillPath(brush, shadow);
            using (var texture = new TextureBrush(content))
            using (var path = Rounded(new Rectangle(Shadow, Shadow, width, height), 16))
            {
                texture.TranslateTransform(Shadow, Shadow); g.FillPath(texture, path);
                using (var edge = new Pen(Color.FromArgb(26, 255, 255, 255), 1f)) g.DrawPath(edge, path);   // faint hairline rim
            }
        }
        return result;
    }
    static void Label(Graphics g, string text, Font font, Rectangle rect, Color color)
    { TextRenderer.DrawText(g, text, font, rect, color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis); }
    void DrawStatus(Graphics g, int width)
    {
        if (quiet && !listening && !working)
        {
            using (var pen = new Pen(accent, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                if (accent == Green) g.DrawLines(pen, new[] { new Point(width / 2 - 7, 23), new Point(width / 2 - 2, 28), new Point(width / 2 + 8, 18) });
                else { g.DrawLine(pen, width / 2 - 5, 18, width / 2 + 5, 28); g.DrawLine(pen, width / 2 + 5, 18, width / 2 - 5, 28); }
            return;
        }
        float bob = listening && animate ? (float)Math.Sin(activityPhase) * 0.7f : 0;
        Visuals.Microphone(g, new RectangleF(ContentPadding, 15 + bob, 13, 16), accent, false);
        if (!quiet) Label(g, message, heading, new Rectangle(ContentPadding + 22, 11, width - 70, 25), Ink);
        if (listening)
            using (var pen = new Pen(accent, 2.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 5; i++)
                {
                    // Bars follow the real microphone loudness, with a small ripple so they never look frozen.
                    double ripple = 0.5 + 0.5 * Math.Sin(activityPhase + i * 0.9);
                    float amount = Level == null ? (float)ripple : shownLevel * (float)(0.7 + 0.3 * ripple) + 0.06f * (float)ripple;
                    float h = 3 + amount * (i == 2 ? 15 : i == 1 || i == 3 ? 12 : 8);
                    g.DrawLine(pen, width - 49 + i * 5, 23 - h / 2, width - 49 + i * 5, 23 + h / 2);
                }
        if (working)
            for (int i = 0; i < 3; i++)
                using (var brush = new SolidBrush(Color.FromArgb((int)(100 + 155 * (0.5 + 0.5 * Math.Sin(activityPhase - i))), accent)))
                    g.FillEllipse(brush, width - 46 + i * 7, 21, 4, 4);
    }
    internal Bitmap Snapshot() { return (Bitmap)frame.Clone(); }
    internal void Dismiss() { hide.Stop(); activity.Stop(); activityClock.Stop(); if (Visible) MoveTo(0); }
    void MoveTo(double target)
    {
        from = visibility; goal = target; motionMs = target == 1 ? 210 : 220;
        if (!animate) { visibility = target; Present(); if (target == 0) base.Hide(); return; }
        clock.Restart(); motion.Start(); Present();
    }
    void AdvanceMotion()
    {
        double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / motionMs);
        // Entrance: quint ease-out, settles softly. Exit: smoothstep, eases in and out so it melts away instead of snapping.
        double ease = goal == 1 ? 1 - Math.Pow(1 - t, 5) : t * t * (3 - 2 * t);
        visibility = from + (goal - from) * ease;
        Present();
        if (t < 1) return;
        motion.Stop(); clock.Stop();
        if (goal == 0) { resize.Stop(); resizeClock.Stop(); base.Hide(); }
    }
    void Present()
    {
        if (frame == null) return;
        IntPtr screen = GetDC(IntPtr.Zero), dc = CreateCompatibleDC(screen), bitmap = frame.GetHbitmap(Color.FromArgb(0)), old = SelectObject(dc, bitmap);
        try
        {
            var size = new SIZE { cx = Width, cy = Height };
            var source = new POINT(); var position = new POINT { x = anchor.X, y = anchor.Y + (int)Math.Round(12 * (1 - visibility)) };
            var blend = new BLENDFUNCTION { SourceConstantAlpha = (byte)Math.Round(255 * visibility * CardAlpha), AlphaFormat = 1 };
            if (!UpdateLayeredWindow(Handle, screen, ref position, ref size, dc, ref source, 0, ref blend, 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { SelectObject(dc, old); DeleteObject(bitmap); DeleteDC(dc); ReleaseDC(IntPtr.Zero, screen); }
    }
    internal static string FontName()
    { using (var fonts = new System.Drawing.Text.InstalledFontCollection()) foreach (FontFamily f in fonts.Families) if (f.Name == "Potro Sans Bangla") return f.Name; return "Nirmala UI"; }
    static Font InstalledFont(string preferred, string fallback, float size)
    { using (var fonts = new System.Drawing.Text.InstalledFontCollection()) { foreach (FontFamily f in fonts.Families) if (f.Name == preferred) return new Font(preferred, size, FontStyle.Regular, GraphicsUnit.Pixel); } return new Font(fallback, size, FontStyle.Regular, GraphicsUnit.Pixel); }
    internal static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
        return path;
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { hide.Dispose(); motion.Dispose(); activity.Dispose(); resize.Dispose(); if (frame != null) frame.Dispose(); body.Dispose(); heading.Dispose(); small.Dispose(); } base.Dispose(disposing); }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref POINT position, ref SIZE size, IntPtr dc, ref POINT source, uint color, ref BLENDFUNCTION blend, uint flags);
}

// Tray and in-window menus: white, large text, roomy rows, yellow highlight (matches the main window).
internal sealed class MenuStyle : ToolStripProfessionalRenderer
{
    static readonly Color Back = Color.White, Hover = Color.FromArgb(255, 243, 190), Line = Color.FromArgb(226, 226, 222),
        Text = Color.FromArgb(17, 17, 17), Dim = Color.FromArgb(125, 125, 118);
    internal MenuStyle() : base(new Colors()) { RoundedEdges = false; }

    internal static void Apply(ContextMenuStrip menu)
    {
        menu.Renderer = new MenuStyle();
        menu.BackColor = Back; menu.ForeColor = Text;
        menu.Font = new Font(Pill.FontName(), 12f);
        menu.Padding = new Padding(8, 10, 8, 10);
        foreach (ToolStripItem item in menu.Items) { item.Padding = new Padding(10, 9, 14, 9); item.ForeColor = Text; if (item.Tag as string == "heading") item.Font = new Font(Pill.FontName(), 12.5f, FontStyle.Bold); }
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    { e.TextColor = e.Item.Enabled || e.Item.Tag as string == "heading" ? Text : Dim; base.OnRenderItemText(e); }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(Hover))
        using (var path = Rounded(new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2), 9)) e.Graphics.FillPath(brush, path);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    { using (var pen = new Pen(Line)) e.Graphics.DrawLine(pen, 10, e.Item.Height / 2, e.Item.Width - 10, e.Item.Height / 2); }
    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = radius * 2; var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
        return path;
    }
    sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Back; } }
        public override Color ImageMarginGradientBegin { get { return Back; } }
        public override Color ImageMarginGradientMiddle { get { return Back; } }
        public override Color ImageMarginGradientEnd { get { return Back; } }
        public override Color MenuBorder { get { return Line; } }
        public override Color MenuItemBorder { get { return Color.Transparent; } }
        public override Color MenuItemSelected { get { return Hover; } }
        public override Color SeparatorDark { get { return Line; } }
        public override Color SeparatorLight { get { return Line; } }
    }
}

internal static class Visuals
{
    internal static void Microphone(Graphics g, RectangleF bounds, Color color, bool waves)
    {
        GraphicsState saved = g.Save();
        g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 24f, bounds.Height / 28f);
        using (var pen = new Pen(color, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var brush = new SolidBrush(color))
        {
            g.FillEllipse(brush, 8, 1, 8, 8);
            g.FillRectangle(brush, 8, 5, 8, 8);
            g.FillEllipse(brush, 8, 9, 8, 8);
            g.DrawArc(pen, 4, 7, 16, 16, 0, 180);
            g.DrawLine(pen, 4, 12, 4, 15); g.DrawLine(pen, 20, 12, 20, 15);
            g.DrawLine(pen, 12, 23, 12, 27); g.DrawLine(pen, 8, 27, 16, 27);
            if (waves) { g.DrawLine(pen, 0, 6, 0, 15); g.DrawLine(pen, 24, 6, 24, 15); }
        }
        g.Restore(saved);
    }

    static Bitmap Image(string state, int size)
    {
        var image = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(image))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Same language as the card: charcoal disc, five rounded voice bars; the bar colour tells the state.
            using (var brush = new SolidBrush(Color.FromArgb(29, 31, 34))) g.FillEllipse(brush, 0, 0, size - 1, size - 1);
            Color bars = state == "recording" ? Pill.Red : state == "finishing" ? Pill.Amber : state == "error" ? Pill.Red : Color.FromArgb(137, 229, 200);
            float s = size / 32f, width = Math.Max(1.6f, 2.8f * s);
            float[] heights = state=="recording"?new float[]{10,19,12,23,9}:new float[]{7,13,18,13,7};
            using (var pen = new Pen(bars, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 5 && state!="finishing" && state!="error"; i++)
                {
                    float x = (8 + i * 4) * s, h = heights[i] * s * 0.5f;
                    g.DrawLine(pen, x, 16 * s - h + width / 2, x, 16 * s + h - width / 2);
                }
            if(state=="finishing")using(var pen=new Pen(bars,2.5f*s)){g.DrawArc(pen,8*s,8*s,16*s,16*s,15,260);g.DrawLine(pen,16*s,11*s,16*s,16*s);g.DrawLine(pen,16*s,16*s,20*s,17*s);}
            if(state=="error")using(var pen=new Pen(bars,2.5f*s)){g.DrawLine(pen,16*s,7*s,16*s,18*s);g.DrawEllipse(pen,15.5f*s,23*s,s,s);}
        }
        return image;
    }
    internal static Icon Icon(string state)
    {
        using (Bitmap image = Image(state, 32))
        {
            IntPtr handle = image.GetHicon();
            try { return (Icon)System.Drawing.Icon.FromHandle(handle).Clone(); }
            finally { DestroyIcon(handle); }
        }
    }

    internal static void RenderAssets()
    {
        IntPtr originalFocus = Native.GetForegroundWindow();
        string assets = Path.Combine(Program.Root, "assets"); Directory.CreateDirectory(assets);
        int[] sizes = { 16, 20, 24, 32, 48, 64, 128, 256 };
        var frames = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (Bitmap image = Image("idle", sizes[i])) using (var buffer = new MemoryStream())
            { image.Save(buffer, ImageFormat.Png); frames[i] = buffer.ToArray(); }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(assets, "voice.ico"))))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
            }
            foreach (byte[] frame in frames) writer.Write(frame);
        }
        foreach (string state in new[] { "idle", "recording", "finishing", "error" })
            using (Bitmap image = Image(state, 96)) image.Save(Path.Combine(assets, "icon-" + state + ".png"));
        using (var pill = new Pill())
        {
            pill.Area = new Rectangle(0, 0, 1920, 1080);
            pill.Set("শুনছি", Pill.Red, 0, "", "", false);
            if (pill.Width > 150 || pill.DisplayedText.Length != 0) throw new Exception("Listening did not start with a compact icon-only card.");
            using (var image = pill.Snapshot()) image.Save(Path.Combine(assets, "preview-start.png"));
            pill.Set("শুনছি", Pill.Red, 0, "আমি বাংলায় কথা বলছি, মাঝে মাঝে English-ও মিশিয়ে বলছি। শুরু থেকে শেষ পর্যন্ত কথাগুলো এখানে দেখা যাচ্ছে।", "Ctrl+Space  থামান     Esc  বাতিল", false);
            using (var image = pill.Snapshot()) image.Save(Path.Combine(assets, "preview-listening.png"));
            pill.Set("লেখা সম্পূর্ণ হচ্ছে", Pill.Amber, 0, "ডিজাইনটা আরও সহজ আর পরিষ্কার হবে। শেষ শব্দগুলোও পুরো নেওয়া হচ্ছে।", "শেষ শব্দগুলো নেওয়া হচ্ছে…", false);
            using (var image = pill.Snapshot()) image.Save(Path.Combine(assets, "preview-finishing.png"));
            string longText = string.Join(" ", System.Linq.Enumerable.Repeat("বাংলা কথা আর English মিলে লম্বা একটি বাক্য।", 35)) + " এই শেষ শব্দ।";
            pill.Set("শুনছি", Pill.Red, 0, longText, "Ctrl+Space  থামান     Esc  বাতিল", false);
            if (!pill.DisplayedText.EndsWith("এই শেষ শব্দ।")) throw new Exception("The live caption hid the final words.");
            using (var image = pill.Snapshot()) image.Save(Path.Combine(assets, "preview-long.png"));
            pill.Set("লেখা বসেছে", Pill.Green, 0, "", "", false);
            using (var image = pill.Snapshot()) image.Save(Path.Combine(assets, "preview-done.png"));
            pill.Area = new Rectangle(0, 0, 320, 600);
            pill.Set("শুনছি", Pill.Red, 0, longText, "Ctrl+Space  থামান     Esc  বাতিল", false);
            if (pill.Width > 320 || !pill.DisplayedText.EndsWith("এই শেষ শব্দ।")) throw new Exception("Narrow-screen caption overflowed or hid its ending.");
            using (var image = pill.Snapshot()) image.Save(Path.Combine(assets, "preview-narrow.png"));
            long styles = GetWindowLongPtr(pill.Handle, -20).ToInt64();
            if ((styles & 0x08080020) != 0x08080020) throw new Exception("Preview is not click-through and non-activating.");
        }
        using (var pill = new Pill(true))
        {
            pill.Area = new Rectangle(-2000, -2000, 800, 600);
            var timer = Stopwatch.StartNew();
            pill.Set("শুনছি", Pill.Red, 0, "অ্যানিমেশন পরীক্ষা English");
            if (timer.ElapsedMilliseconds > 100) throw new Exception("Showing the card blocked its caller.");
            Pump(70);
            if (!pill.Visible || pill.VisibilityAmount <= 0 || pill.VisibilityAmount >= 1) throw new Exception("Entrance did not animate.");
            Pump(220);
            if (pill.MotionRunning || pill.VisibilityAmount != 1) throw new Exception("Entrance did not settle and release its timer.");
            if (!pill.ActivityRunning || pill.ActivityPhase <= 0) throw new Exception("Listening indicator did not animate.");
            string before;
            using (var image = pill.Snapshot()) using (var buffer = new MemoryStream()) { image.Save(buffer, ImageFormat.Png); before = Convert.ToBase64String(buffer.ToArray()); }
            Pump(120);
            using (var image = pill.Snapshot()) using (var buffer = new MemoryStream())
            { image.Save(buffer, ImageFormat.Png); if (before == Convert.ToBase64String(buffer.ToArray())) throw new Exception("Listening animation did not change visible pixels."); }
            pill.Preview("লেখা পরিবর্তন হলে কার্ড আবার অ্যানিমেশন করবে না।");
            if (pill.MotionRunning) throw new Exception("Caption updates restarted the entrance.");
            pill.Preview("লম্বা লেখা এলে কার্ড লাফ না দিয়ে মসৃণভাবে বড় হবে। English mixed text, দ্বিতীয় লাইন, তৃতীয় লাইন পর্যন্ত।");
            Pump(300);
            if (!pill.ResizeSettled) throw new Exception("Card size did not glide to its new size and stop its timer.");
            pill.Dismiss(); Pump(35);
            pill.Set("আবার শুনছি", Pill.Red, 0, "দ্রুত আবার shortcut");
            Pump(280); // entrance is 210 ms
            if (!pill.Visible || pill.VisibilityAmount != 1) throw new Exception("A new recording was hidden by the old exit.");
            pill.Set("লেখা বসেছে", Pill.Green, 50); Pump(340); // 50 ms wait + 220 ms exit
            if (pill.Visible || pill.MotionRunning || pill.ActivityRunning) throw new Exception("Auto-dismiss did not finish or left a running animation.");
        }
        using (var pill = new Pill(false))
        {
            pill.Area = new Rectangle(-2000, -2000, 800, 600);
            pill.Set("শুনছি", Pill.Red, 0);
            if (pill.VisibilityAmount != 1 || pill.MotionRunning || pill.ActivityRunning) throw new Exception("Reduced-motion preference was ignored.");
            pill.Dismiss();
            if (pill.Visible) throw new Exception("Reduced-motion dismiss failed.");
        }
        if (Native.GetForegroundWindow() != originalFocus) throw new Exception("Rendering preview stole focus.");
        File.WriteAllText(Path.Combine(assets, "visual-results.txt"), "PASS: compact icon-only start, animated listening pixels, Bengali/English captions, 3-line tail and narrow-screen ending, native non-activating/click-through flags, foreground focus unchanged, nonblocking entrance, interrupted exit, stable caption updates, automatic dismissal, all hidden animation timers stopped, reduced motion, multi-size native icons.", Program.Utf8);
    }
    static void Pump(int milliseconds)
    { var timer = Stopwatch.StartNew(); while (timer.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); System.Threading.Thread.Sleep(5); } }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
}
