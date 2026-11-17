using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZHmaster
{
    sealed class BufferedListView : ListView
    {
        internal BufferedListView() { DoubleBuffered = true; }
    }

    static class Palette
    {
        internal static readonly Color Ink = Color.FromArgb(26, 48, 47);
        internal static readonly Color Green = Color.FromArgb(13, 116, 105);
        internal static readonly Color Muted = Color.FromArgb(111, 125, 117);
        internal static readonly Color Canvas = Color.FromArgb(246, 247, 242);
        internal static readonly Color Stroke = Color.FromArgb(216, 221, 216);
        internal static readonly Color Fill = Color.FromArgb(237, 241, 238);
        internal static readonly Color Side = Color.FromArgb(24, 52, 49);
        internal static readonly Color Error = Color.FromArgb(174, 58, 49);
        internal static GraphicsPath Rounded(RectangleF box, float radius)
        {
            float diameter = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
            path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
            path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    sealed class ActionButton : Button
    {
        internal bool Primary;
        internal bool OutlineGreen;
        internal bool Navigation;
        internal bool Active;
        bool hover;
        internal ActionButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = new Font("等线", 10, FontStyle.Bold);
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Palette.Canvas : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill, foreground, stroke;
            if (Navigation)
            {
                fill = Active ? Palette.Green : hover ? Color.FromArgb(36, 70, 63) : Palette.Side;
                foreground = Enabled ? Color.White : Color.FromArgb(142, 161, 151);
                stroke = fill;
            }
            else if (!Enabled)
            {
                fill = Color.FromArgb(232, 237, 232); foreground = Color.FromArgb(153, 165, 157); stroke = Color.FromArgb(226, 232, 226);
            }
            else if (Primary)
            {
                fill = hover ? Color.FromArgb(9, 94, 85) : Palette.Green; foreground = Color.White; stroke = fill;
            }
            else
            {
                fill = hover ? Color.FromArgb(231, 241, 233) : Color.White;
                foreground = OutlineGreen ? Palette.Green : Palette.Ink;
                stroke = OutlineGreen || Focused ? Palette.Green : Palette.Stroke;
            }
            using (GraphicsPath path = Palette.Rounded(new RectangleF(.5F, .5F, Width - 1F, Height - 1F), 6))
            using (Brush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(stroke))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            Rectangle text = new Rectangle(8, 0, Width - 16, Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, text, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(5, 5, Width - 10, Height - 10), foreground, fill);
        }
    }

    sealed class RoundedField : Panel
    {
        internal readonly Control Editor;
        bool hover;
        int editorHeight, editorPadding;
        internal RoundedField(Control editor)
        {
            Editor = editor;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Fill;
            editor.BackColor = Palette.Fill;
            editor.ForeColor = Palette.Ink;
            editor.Font = new Font("等线", 10);
            TextBox text = editor as TextBox;
            if (text != null) { text.BorderStyle = BorderStyle.None; text.AutoSize = false; }
            ComboBox combo = editor as ComboBox;
            if (combo != null) combo.FlatStyle = FlatStyle.Popup;
            editor.Enter += delegate { Invalidate(); };
            editor.Leave += delegate { Invalidate(); };
            editor.MouseEnter += delegate { hover = true; Invalidate(); };
            editor.MouseLeave += delegate { hover = false; Invalidate(); };
            editor.FontChanged += delegate { editorHeight = 0; LayoutEditor(); };
            MouseEnter += delegate { hover = true; Invalidate(); };
            MouseLeave += delegate { hover = false; Invalidate(); };
            Controls.Add(editor);
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutEditor();
        }
        internal void LayoutEditor()
        {
            if (Editor != null)
            {
                if (editorHeight == 0)
                {
                    editorPadding = (int)Math.Round(12 * Editor.Font.SizeInPoints / 10F);
                    editorHeight = Editor is ComboBox ? Editor.PreferredSize.Height : Math.Max(22, TextRenderer.MeasureText("国Ag", Editor.Font).Height + 2);
                }
                Rectangle bounds = new Rectangle(editorPadding, Math.Max(2, (Height - editorHeight) / 2), Math.Max(20, Width - editorPadding * 2), editorHeight);
                if (Editor.Bounds != bounds) Editor.Bounds = bounds;
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent == null ? Palette.Canvas : Parent.BackColor); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Palette.Rounded(new RectangleF(.5F, .5F, Width - 1F, Height - 1F), 6))
            using (Brush brush = new SolidBrush(Palette.Fill))
            using (Pen pen = new Pen(Enabled && (ContainsFocus || hover) ? Palette.Green : Palette.Stroke))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            base.OnPaint(e);
        }
    }

    sealed class FileListFrame : Panel
    {
        internal FileListFrame()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Canvas;
            Padding = new Padding(1);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Palette.Canvas); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Palette.Rounded(new RectangleF(.5F, .5F, Width - 1F, Height - 1F), 6))
            using (Brush brush = new SolidBrush(Color.White))
            using (Pen pen = new Pen(Palette.Stroke))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
        }
    }
}
