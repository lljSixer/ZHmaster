using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZHmaster
{
    sealed class EmptyStateView : Control
    {
        internal EmptyStateView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White; ForeColor = Palette.Muted; Font = new Font("等线", 11);
            AccessibleRole = AccessibleRole.StaticText;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AccessibleName = Text; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.White); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = e.Graphics.DpiX / 96F;
            float cx = Width / 2F, top = Math.Max(6, Height / 2F - 45 * scale);
            using (Pen pen = new Pen(Color.FromArgb(172, 188, 178), Math.Max(1, 1.5F * scale)))
            {
                e.Graphics.DrawRectangle(pen, cx - 15 * scale, top, 30 * scale, 36 * scale);
                e.Graphics.DrawLine(pen, cx, top + 8 * scale, cx, top + 24 * scale);
                e.Graphics.DrawLines(pen, new[] { new PointF(cx - 6 * scale, top + 18 * scale), new PointF(cx, top + 24 * scale), new PointF(cx + 6 * scale, top + 18 * scale) });
            }
            int y = (int)(top + 52 * scale);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(12, y, Math.Max(1, Width - 24), Math.Max(30, Height - y)),
                ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    sealed class FileHeaderView : Control
    {
        readonly ListView list;
        internal FileHeaderView(ListView source)
        {
            list = source;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(248, 250, 247); Font = new Font("等线", 10);
            AccessibleRole = AccessibleRole.Row;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            int x = 0;
            foreach (ColumnHeader column in list.Columns)
            {
                TextFormatFlags alignment = column.TextAlign == HorizontalAlignment.Center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
                TextRenderer.DrawText(e.Graphics, column.Text, Font, new Rectangle(x + 6, 0, Math.Max(1, column.Width - 12), Height),
                    Palette.Ink, alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                x += column.Width;
            }
            using (Pen pen = new Pen(Palette.Fill)) e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }

    sealed class FilledComboBox : ComboBox
    {
        internal FilledComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList; DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Popup; BackColor = Palette.Fill; ForeColor = Palette.Ink;
        }
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e); ItemHeight = Math.Max(20, Font.Height + 4);
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            Color fill = selected ? Color.FromArgb(220, 235, 224) : Palette.Fill;
            using (Brush brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, e.Bounds);
            string text = e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text;
            TextRenderer.DrawText(e.Graphics, text, Font, Rectangle.Inflate(e.Bounds, -3, 0), Enabled ? Palette.Ink : Palette.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}
