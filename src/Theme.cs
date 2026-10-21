using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AnimeRenamer
{
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(245, 247, 251);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(233, 237, 245);
        public static readonly Color BorderStrong = Color.FromArgb(214, 221, 233);
        public static readonly Color CardBorder = Color.FromArgb(234, 239, 247);
        public static readonly Color Text = Color.FromArgb(31, 41, 55);
        public static readonly Color Muted = Color.FromArgb(107, 114, 128);
        public static readonly Color Faint = Color.FromArgb(148, 158, 177);
        public static readonly Color Accent = Color.FromArgb(79, 107, 237);
        public static readonly Color AccentDark = Color.FromArgb(63, 87, 214);
        public static readonly Color AccentSoft = Color.FromArgb(237, 241, 255);
        public static readonly Color Green = Color.FromArgb(21, 145, 82);
        public static readonly Color GreenSoft = Color.FromArgb(232, 247, 239);
        public static readonly Color Orange = Color.FromArgb(201, 116, 18);
        public static readonly Color OrangeSoft = Color.FromArgb(255, 246, 232);
        public static readonly Color Red = Color.FromArgb(205, 50, 50);
        public static readonly Color RedSoft = Color.FromArgb(253, 238, 238);

        public static Font F(float size)
        {
            return F(size, FontStyle.Regular);
        }

        public static Font F(float size, FontStyle style)
        {
            try { return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point); }
            catch { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point); }
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) return p;
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color color)
        {
            using (var path = Round(r, radius))
            using (var b = new SolidBrush(color))
                g.FillPath(b, path);
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color color, float width)
        {
            using (var path = Round(r, radius))
            using (var p = new Pen(color, width))
                g.DrawPath(p, path);
        }
    }

    public class CardPanel : Panel
    {
        public int Radius = 0;
        public Color FillColor = Theme.Card;

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // Page-grey background: any native erase at the card edge uses this
            // colour instead of leaving a white square.
            BackColor = Theme.Bg;
            Padding = new Padding(16, 13, 16, 13);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg))
                g.FillRectangle(b, ClientRectangle);

            if (Width <= 2 || Height <= 2) return;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (Radius > 0)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Theme.FillRound(g, r, Radius, FillColor);
                Theme.DrawRound(g, r, Radius, Theme.CardBorder, 1f);
            }
            else
            {
                using (var fill = new SolidBrush(FillColor)) g.FillRectangle(fill, r);
                using (var pen = new Pen(Theme.CardBorder, 1f)) g.DrawRectangle(pen, r);
            }
        }
    }

    public enum ButtonKind { Primary, Secondary, Ghost, Danger, Soft }

    public class FlatButton : Button
    {
        public ButtonKind Kind = ButtonKind.Secondary;
        public int Radius = 9;
        private bool _hover;
        private bool _down;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.White;
            Font = Theme.F(9.5f);
            Cursor = Cursors.Hand;
            Height = 34;
            UseVisualStyleBackColor = false;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            // Opaque base: prevents transparent corners from exposing a neighbouring
            // button's fill / the window edge at non-100% DPI.
            Color parentBg = Parent != null ? Parent.BackColor : Color.White;
            if (parentBg.A < 255) parentBg = Color.White;
            using (var bg = new SolidBrush(parentBg))
                g.FillRectangle(bg, ClientRectangle);

            Color fill = Color.Transparent;
            Color border = Color.Transparent;
            Color text = Theme.Text;
            bool drawFill = true;

            switch (Kind)
            {
                case ButtonKind.Primary:
                    fill = _down ? Theme.AccentDark : (_hover ? Color.FromArgb(95, 121, 244) : Theme.Accent);
                    text = Color.White;
                    break;
                case ButtonKind.Danger:
                    fill = _down ? Color.FromArgb(180, 38, 38) : (_hover ? Color.FromArgb(222, 66, 66) : Theme.Red);
                    text = Color.White;
                    break;
                case ButtonKind.Secondary:
                    fill = _down ? Color.FromArgb(224, 232, 243) : (_hover ? Color.FromArgb(235, 241, 250) : Color.FromArgb(243, 246, 252));
                    text = Color.FromArgb(58, 68, 86);
                    break;
                case ButtonKind.Soft:
                    fill = _down ? Color.FromArgb(223, 230, 255) : (_hover ? Color.FromArgb(232, 238, 255) : Theme.AccentSoft);
                    text = Theme.AccentDark;
                    break;
                case ButtonKind.Ghost:
                    drawFill = _hover || _down;
                    fill = _down ? Color.FromArgb(52, 255, 255, 255) : Color.FromArgb(34, 255, 255, 255);
                    text = Color.White;
                    break;
            }

            if (!Enabled)
            {
                if (Kind == ButtonKind.Primary || Kind == ButtonKind.Danger) fill = Color.FromArgb(186, 195, 214);
                else { fill = Color.FromArgb(246, 247, 250); border = Theme.Border; }
                text = Color.FromArgb(156, 165, 182);
            }

            if (drawFill && fill.A > 0)
                Theme.FillRound(g, r, Radius, fill);
            if (border.A > 0)
                Theme.DrawRound(g, r, Radius, border, 1f);

            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, Text, Font, r, text, flags);
        }
    }

    public class HeaderPanel : Panel
    {
        public string Subtitle = "";
        public event EventHandler HelpClicked;
        public event EventHandler SettingsClicked;

        private Rectangle _helpRect;
        private Rectangle _settingsRect;
        private int _hover;

        public HeaderPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 88;
        }

        private void UpdateRects()
        {
            Font f = Theme.F(9.25f);
            Size hs = TextRenderer.MeasureText("使用说明", f);
            Size ss = TextRenderer.MeasureText("设置", f);
            int hw = hs.Width + 30;
            int sw = ss.Width + 30;
            int right = Width - 22;
            _settingsRect = new Rectangle(right - sw, 27, sw, 34);
            _helpRect = new Rectangle(_settingsRect.Left - 10 - hw, 27, hw, 34);
        }

        private int Hit(Point p)
        {
            if (_helpRect.Contains(p)) return 1;
            if (_settingsRect.Contains(p)) return 2;
            return 0;
        }

        protected override void OnResize(EventArgs e)
        {
            UpdateRects();
            base.OnResize(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h != _hover)
            {
                _hover = h;
                Cursor = h == 0 ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != 0) { _hover = 0; Cursor = Cursors.Default; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (_helpRect.Contains(e.Location))
                {
                    var h = HelpClicked;
                    if (h != null) h(this, EventArgs.Empty);
                }
                else if (_settingsRect.Contains(e.Location))
                {
                    var h = SettingsClicked;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var r = ClientRectangle;
            using (var b = new LinearGradientBrush(r, Color.FromArgb(66, 84, 205), Color.FromArgb(112, 78, 208), 12f))
                g.FillRectangle(b, r);

            using (var b1 = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
                g.FillEllipse(b1, Width - 300, -120, 360, 300);
            using (var b2 = new SolidBrush(Color.FromArgb(14, 255, 255, 255)))
                g.FillEllipse(b2, Width - 500, 30, 260, 200);
            using (var b3 = new SolidBrush(Color.FromArgb(12, 255, 255, 255)))
                g.FillEllipse(b3, Width - 120, -40, 160, 160);

            if (_settingsRect.Width == 0) UpdateRects();
            DrawHeaderButton(g, _helpRect, "使用说明", _hover == 1);
            DrawHeaderButton(g, _settingsRect, "设置", _hover == 2);

            var titleRect = new Rectangle(24, 15, Math.Max(200, Width - 400), 34);
            TextRenderer.DrawText(g, "番剧批量重命名", Theme.F(19f, FontStyle.Bold), titleRect,
                Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            var subRect = new Rectangle(25, 49, Math.Max(200, Width - 400), 24);
            TextRenderer.DrawText(g, Subtitle, Theme.F(9.25f), subRect,
                Color.FromArgb(214, 255, 255, 255),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            var verRect = new Rectangle(Width - 260, Height - 28, 240, 20);
            TextRenderer.DrawText(g, "v" + Program.VersionText + "  ·  单文件绿色版", Theme.F(8.25f), verRect,
                Color.FromArgb(150, 255, 255, 255),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private static void DrawHeaderButton(Graphics g, Rectangle r, string text, bool hover)
        {
            if (hover)
            {
                using (var b = new SolidBrush(Color.FromArgb(52, 255, 255, 255)))
                    Theme.FillRound(g, r, 8, b.Color);
            }
            TextRenderer.DrawText(g, text, Theme.F(9.25f), r, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    public class ThinProgress : Control
    {
        private int _value;
        private int _maximum = 100;

        public ThinProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 8;
            BackColor = Color.White;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        public int Value
        {
            get { return _value; }
            set { _value = Math.Max(0, Math.Min(_maximum, value)); Invalidate(); }
        }

        public int Maximum
        {
            get { return _maximum; }
            set { _maximum = Math.Max(1, value); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, (Height - 8) / 2, Width - 1, 7);
            Theme.FillRound(g, r, 3, Color.FromArgb(226, 231, 240));
            if (_value > 0)
            {
                int w = (int)Math.Round((double)r.Width * _value / _maximum);
                if (w < 2) w = 2;
                Theme.FillRound(g, new Rectangle(r.X, r.Y, w, r.Height), 3, Theme.Accent);
            }
        }
    }

    public static class Ui
    {
        public static Label Label(string text, float size, Color color, FontStyle style)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Font = Theme.F(size, style);
            l.ForeColor = color;
            l.BackColor = Color.White;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.UseMnemonic = false;
            return l;
        }

        public static Label Caption(string text)
        {
            var l = Label(text, 9f, Theme.Muted, FontStyle.Regular);
            l.Tag = "rowcaption";
            return l;
        }

        public static Label CardTitle(string text)
        {
            return Label(text, 10.5f, Theme.Text, FontStyle.Bold);
        }

        public static TextBox TextBox()
        {
            var t = new TextBox();
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = Theme.F(9.5f);
            t.ForeColor = Theme.Text;
            t.BackColor = Color.FromArgb(252, 253, 255);
            t.Height = 28;
            return t;
        }

        public static ComboBox Combo(bool editable)
        {
            var c = new ComboBox();
            c.DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.Font = Theme.F(9.25f);
            c.ForeColor = Theme.Text;
            c.BackColor = Color.White;
            c.Height = 28;
            return c;
        }

        public static CheckBox Check(string text, bool value)
        {
            var c = new CheckBox();
            c.Text = text;
            c.Checked = value;
            c.AutoSize = true;
            c.Font = Theme.F(9f);
            c.ForeColor = Theme.Text;
            c.BackColor = Color.White;
            c.Cursor = Cursors.Hand;
            c.UseVisualStyleBackColor = false;
            return c;
        }

        public static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Color.White;
            g.BorderStyle = BorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.GridColor = Color.FromArgb(241, 244, 249);
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.AllowUserToOrderColumns = false;
            g.MultiSelect = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            g.ColumnHeadersHeight = 34;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.RowTemplate.Height = 30;
            g.Font = Theme.F(9f);
            g.DefaultCellStyle.Font = Theme.F(9f);
            g.DefaultCellStyle.ForeColor = Theme.Text;
            g.DefaultCellStyle.BackColor = Color.White;
            g.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            g.DefaultCellStyle.SelectionForeColor = Theme.Text;
            g.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(251, 252, 254);
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
            g.ColumnHeadersDefaultCellStyle.Font = Theme.F(8.75f, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(248, 250, 253);
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Muted;
            g.ScrollBars = ScrollBars.Vertical;
        }
    }
}
