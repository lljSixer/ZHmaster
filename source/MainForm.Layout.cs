using System;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace ZHmaster
{
    sealed partial class MainForm
    {
        bool layoutReady, arranging, fitting, resizePending;
        volatile bool interactiveResize;
        long lastResizeLayout;
        readonly System.Windows.Forms.Timer resizeTimer = new System.Windows.Forms.Timer { Interval = 16 };
        int fitWidth = -1, fitMode = -1, fitDpi = -1, fitColumnCount = -1;
        readonly Panel sidebar;
        readonly Label brand, slogan, sideNote, workspaceLabel, saveLabel, nameLabel, pageLabel, saveModeLabel;
        readonly ActionButton open, details;
        readonly RoundedField nameField, formatField, saveModeField;
        readonly FileHeaderView header;

        int Px(int value) { return (int)Math.Round(value * DeviceDpi / 96.0); }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!layoutReady || WindowState == FormWindowState.Minimized) return;
            if (interactiveResize)
            {
                resizePending = true;
                // WM_TIMER has lower priority than WM_SIZE. Also apply from the resize
                // stream so continuous mouse movement cannot starve the pending layout.
                if ((Stopwatch.GetTimestamp() - lastResizeLayout) * 1000.0 / Stopwatch.Frequency >= 16) ApplyPendingResize();
                else resizeTimer.Start();
            }
            else ArrangeWorkspace();
        }
        protected override void OnResizeBegin(EventArgs e)
        {
            interactiveResize = true;
            lastResizeLayout = Stopwatch.GetTimestamp();
            base.OnResizeBegin(e);
        }
        protected override void OnResizeEnd(EventArgs e)
        {
            resizeTimer.Stop(); resizePending = false; interactiveResize = false;
            ArrangeWorkspace(); FlushUiUpdates(false);
            base.OnResizeEnd(e);
        }
        void ApplyPendingResize()
        {
            resizePending = false; lastResizeLayout = Stopwatch.GetTimestamp();
            ArrangeWorkspace();
        }
        static void Place(Control control, int x, int y, int width, int height)
        {
            Rectangle bounds = new Rectangle(x, y, width, height);
            if (control.Bounds != bounds) control.Bounds = bounds;
        }
        void ArrangeWorkspace()
        {
            if (!layoutReady || arranging) return;
            arranging = true; SuspendLayout();
            sidebar.SuspendLayout(); listFrame.SuspendLayout(); imageSettings.SuspendLayout(); wordSettings.SuspendLayout();
            try
            {
                int side = Px(246), x = side + Px(32), w = Math.Max(Px(620), ClientSize.Width - x - Px(32));
                int footer = ClientSize.Height - Px(54), bar = footer - Px(20), message = bar - Px(31);
                int settings = message - Px(137), note = settings - Px(37);
                Place(sidebar, 0, 0, side, ClientSize.Height);
                Place(brand, Px(25), Px(38), Px(208), Px(43));
                Place(workspaceLabel, Px(28), Px(83), Px(192), Px(26));
                Place(imageMode, Px(16), Px(154), Px(214), Px(48));
                Place(wordMode, Px(16), Px(212), Px(214), Px(48));
                Place(audioMode, Px(16), Px(270), Px(214), Px(48));
                Place(slogan, Px(26), ClientSize.Height - Px(153), Px(204), Px(27));
                Place(sideNote, Px(26), ClientSize.Height - Px(111), Px(204), Px(84));
                Place(title, x - Px(3), Px(48), w + Px(3), Px(48));
                Place(subtitle, x, Px(111), w, Px(28));
                Place(add, x, Px(154), Px(130), Px(38));
                Place(up, x + Px(142), Px(154), Px(66), Px(38));
                Place(down, x + Px(216), Px(154), Px(66), Px(38));
                Place(remove, x + Px(mode == 0 ? 290 : 142), Px(154), Px(66), Px(38));
                Place(clear, x + Px(mode == 0 ? 364 : 216), Px(154), Px(66), Px(38));
                Place(count, x + w - Px(152), Px(163), Px(152), Px(24));
                Place(listFrame, x, Px(206), w, Math.Max(Px(145), note - Px(11) - Px(206)));
                int inset = Px(8), head = Px(32);
                Place(header, inset, inset, Math.Max(1, listFrame.ClientSize.Width - inset * 2), head);
                Place(list, inset, inset + head, Math.Max(1, listFrame.ClientSize.Width - inset * 2), Math.Max(1, listFrame.ClientSize.Height - inset * 2 - head));
                Place(empty, list.Left, list.Top, list.Width, list.Height);
                Place(hint, x, note, w, Px(25));

                int pathRow = settings + Px(mode == 0 ? 30 : 87);
                Place(saveLabel, x, settings + Px(mode == 0 ? 0 : 57), Px(140), Px(25));
                Place(folderField, x, pathRow, w - Px(136), Px(38));
                Place(browse, x + w - Px(124), pathRow, Px(124), Px(38));
                Place(imageSettings, x, settings + Px(87), w, Px(38));
                Place(wordSettings, x, settings, w, Px(38));
                if (mode == 0)
                {
                    int nameWidth = Math.Max(Px(180), (w - Px(220)) / 2);
                    Place(nameLabel, 0, Px(9), Px(88), Px(25));
                    Place(nameField, Px(90), 0, nameWidth, Px(38));
                    int pageX = nameField.Right + Px(26);
                    Place(pageLabel, pageX, Px(9), Px(49), Px(25));
                    Place(formatField, pageX + Px(52), 0, w - pageX - Px(52), Px(38));
                }
                else
                {
                    Place(saveModeLabel, 0, Px(9), Px(88), Px(25));
                    int policyWidth = Math.Min(Px(300), Math.Max(Px(240), (w - Px(220)) / 2));
                    Place(saveModeField, Px(90), 0, policyWidth, Px(38));
                    Place(saveDescription, saveModeField.Right + Px(26), Px(10), w - saveModeField.Right - Px(26), Px(25));
                }

                Place(status, x, message, w, Px(25));
                Place(progress, x, bar, w, Px(4));
                Place(open, x, footer, Px(146), Px(38));
                Place(details, x + Px(158), footer, Px(104), Px(38));
                Place(start, x + w - Px(168), footer, Px(168), Px(38));
                Place(stop, start.Left - Px(88), footer, Px(76), Px(38));
            }
            finally
            {
                wordSettings.ResumeLayout(false); imageSettings.ResumeLayout(false); listFrame.ResumeLayout(false); sidebar.ResumeLayout(false);
                ResumeLayout(false); arranging = false;
            }
            FitColumns();
        }
        void ConfigureColumns()
        {
            if (mode == 2 && list.Columns.Count == 3) list.Columns.Add("音频参数", Px(250));
            if (mode != 2 && list.Columns.Count == 4) list.Columns.RemoveAt(3);
            list.Columns[0].Text = mode == 0 ? "顺序" : "状态";
            list.Columns[2].Text = mode == 2 ? "时长" : "所在文件夹";
            list.Columns[2].TextAlign = mode == 2 ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            if (mode == 2) list.Columns[3].Text = "音频参数";
            fitMode = -1;
            if (header != null) header.Invalidate();
            header.AccessibleName = String.Join("，", list.Columns.Cast<ColumnHeader>().Select(column => column.Text));
        }
        void SetColumnWidth(int index, int width)
        {
            if (list.Columns[index].Width != width) list.Columns[index].Width = width;
        }
        void FitColumns()
        {
            if (list == null || list.Columns.Count < 3 || fitting) return;
            int available = list.ClientSize.Width;
            if (available == fitWidth && mode == fitMode && DeviceDpi == fitDpi && list.Columns.Count == fitColumnCount) return;
            fitWidth = available; fitMode = mode; fitDpi = DeviceDpi; fitColumnCount = list.Columns.Count;
            fitting = true;
            try
            {
                SetColumnWidth(0, Px(mode == 0 ? 58 : 82));
                if (mode == 2 && list.Columns.Count == 4)
                {
                    SetColumnWidth(2, Px(78));
                    SetColumnWidth(1, Math.Min(Px(420), Math.Max(Px(180), (int)((available - list.Columns[0].Width - list.Columns[2].Width) * .46))));
                    SetColumnWidth(3, Math.Max(Px(120), available - list.Columns[0].Width - list.Columns[1].Width - list.Columns[2].Width - 2));
                }
                else
                {
                    SetColumnWidth(1, Math.Min(Px(520), Math.Max(Px(220), (int)(available * .4))));
                    SetColumnWidth(2, Math.Max(Px(120), available - list.Columns[0].Width - list.Columns[1].Width - 2));
                }
                if (header != null) header.Invalidate();
            }
            finally { fitting = false; }
        }
    }
}
