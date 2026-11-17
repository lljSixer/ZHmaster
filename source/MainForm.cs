using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ZHmaster
{
    sealed partial class MainForm : Form
    {
        readonly List<string>[] queues = { new List<string>(), new List<string>(), new List<string>() };
        readonly HashSet<string>[] queuePaths = { new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase) };
        readonly Dictionary<string, ListViewItem> visibleRows = new Dictionary<string, ListViewItem>(StringComparer.OrdinalIgnoreCase);
        bool updatingList;
        readonly Dictionary<string, string> itemStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly List<Control> editable = new List<Control>();
        readonly List<string> outcomes = new List<string>();
        readonly bool environmentAvailable;
        readonly bool audioEnvironmentAvailable;
        readonly string[] saveFolders;
        readonly Dictionary<string, string> audioParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly ActionButton imageMode, wordMode, audioMode, add, up, down, remove, clear, browse, start, stop;
        readonly Label title, subtitle, count, hint, status, saveDescription;
        readonly EmptyStateView empty;
        readonly ListView list;
        readonly FileListFrame listFrame;
        readonly TextBox folder, pdfName;
        readonly ComboBox format, saveMode;
        readonly RoundedField folderField;
        readonly Panel imageSettings, wordSettings;
        readonly ProgressBar progress;
        readonly ToolTip tips = new ToolTip();
        int mode;
        volatile bool cancel;
        volatile bool busy;

        internal MainForm() : this(WordExporter.Available, AudioExporter.Available) { }
        internal MainForm(bool available) : this(available, AudioExporter.Available) { }
        internal MainForm(bool available, bool audioAvailable)
        {
            environmentAvailable = available;
            audioEnvironmentAvailable = audioAvailable;
            saveFolders = new[] { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDF输出"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDF输出"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WAV输出") };
            Text = "ZHmaster · 文件转换工作台 1.2.2";
            ClientSize = new Size(1070, 760);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            InitializeUpdateTimers();
            MinimumSize = SizeFromClientSize(new Size(980, 680));
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("等线", 10);
            BackColor = Palette.Canvas;
            ForeColor = Palette.Ink;
            string icon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZHmaster.ico");
            if (File.Exists(icon)) Icon = new Icon(icon);
            AllowDrop = true;
            DragEnter += OnDragEnter; DragDrop += OnDragDrop;

            sidebar = new Panel { BackColor = Palette.Side, Bounds = new Rectangle(0, 0, 246, 760) };
            Controls.Add(sidebar);
            brand = LabelAt(sidebar, "ZHmaster", 25, 38, 208, 43, 24, Color.White);
            brand.Name = "brand"; brand.Font = new Font("Bahnschrift", 24, FontStyle.Bold);
            workspaceLabel = LabelAt(sidebar, "文件转换工作台", 28, 83, 192, 26, 11, Color.FromArgb(176, 204, 193));
            imageMode = ButtonAt(sidebar, "01    图片转 PDF", 16, 154, 214, 48, delegate { Switch(0); });
            wordMode = ButtonAt(sidebar, "02    Word 转 PDF", 16, 212, 214, 48, delegate { Switch(1); });
            audioMode = ButtonAt(sidebar, "03    音频转 WAV", 16, 270, 214, 48, delegate { Switch(2); });
            imageMode.Navigation = wordMode.Navigation = audioMode.Navigation = true;
            slogan = LabelAt(sidebar, "简单转换，完整保留", 26, 607, 204, 27, 11, Color.White);
            slogan.Name = "slogan";
            sideNote = LabelAt(sidebar, "本机处理 · 文件留在本机\n\n版本 1.2.2", 26, 649, 204, 84, 9, Color.FromArgb(176, 204, 193));

            title = LabelAt(this, "", 275, 48, 763, 48, 23, Palette.Ink);
            subtitle = LabelAt(this, "", 278, 111, 760, 28, 10, Palette.Muted);
            add = ButtonAt(this, "＋ 添加图片", 278, 154, 130, 38, delegate { ChooseFiles(); });
            add.OutlineGreen = true;
            up = ButtonAt(this, "上移", 420, 154, 66, 38, delegate { MoveSelected(-1); });
            down = ButtonAt(this, "下移", 494, 154, 66, 38, delegate { MoveSelected(1); });
            remove = ButtonAt(this, "移除", 568, 154, 66, 38, delegate { RemoveSelected(); });
            clear = ButtonAt(this, "清空", 642, 154, 66, 38, delegate {
                if (mode > 0) foreach (string path in queues[mode]) { itemStates.Remove(path); audioParameters.Remove(path); audioMetadata.Remove(path); }
                queues[mode].Clear(); queuePaths[mode].Clear(); RefreshList();
            });
            count = LabelAt(this, "", 886, 163, 152, 24, 10, Palette.Muted);
            count.TextAlign = ContentAlignment.MiddleRight;

            listFrame = new FileListFrame { Bounds = new Rectangle(278, 206, 760, 264), Padding = new Padding(8), AllowDrop = true };
            list = new BufferedListView { Dock = DockStyle.None, View = View.Details, FullRowSelect = true, HideSelection = false,
                BorderStyle = BorderStyle.None, MultiSelect = true, ShowItemToolTips = true, BackColor = Color.White,
                ForeColor = Palette.Ink, AllowDrop = true, HeaderStyle = ColumnHeaderStyle.None };
            list.Columns.Add("顺序", 58); list.Columns.Add("文件名", 300); list.Columns.Add("所在文件夹", 350);
            list.SelectedIndexChanged += delegate { if (!updatingList) UpdateActions(); };
            list.ClientSizeChanged += delegate { if (!arranging) FitColumns(); };
            list.HandleCreated += delegate { FitColumns(); };
            list.DragEnter += OnDragEnter; list.DragDrop += OnDragDrop;
            list.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (busy) return;
                if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
                if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem item in list.Items) item.Selected = true; e.Handled = true; }
            };
            listFrame.DragEnter += OnDragEnter; listFrame.DragDrop += OnDragDrop;
            listFrame.Controls.Add(list); Controls.Add(listFrame);
            header = new FileHeaderView(list);
            listFrame.Controls.Add(header);
            empty = new EmptyStateView { AllowDrop = true };
            empty.DragEnter += OnDragEnter; empty.DragDrop += OnDragDrop;
            listFrame.Controls.Add(empty);
            empty.BringToFront();
            hint = LabelAt(this, "", 278, 481, 760, 25, 9, Palette.Muted);

            saveLabel = LabelAt(this, "保存位置", 278, 518, 140, 25, 10, Palette.Ink);
            folder = new TextBox { Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDF输出"), AccessibleName = "保存位置" };
            folderField = FieldAt(this, folder, 278, 548, 624, 38);
            browse = ButtonAt(this, "浏览…", 914, 548, 124, 38, delegate {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "选择保存位置", SelectedPath = Directory.Exists(folder.Text) ? folder.Text : AppDomain.CurrentDomain.BaseDirectory })
                    if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
            });
            imageSettings = new Panel { Bounds = new Rectangle(278, 605, 760, 38), BackColor = Palette.Canvas };
            wordSettings = new Panel { Bounds = imageSettings.Bounds, BackColor = Palette.Canvas };
            Controls.Add(imageSettings); Controls.Add(wordSettings);
            nameLabel = LabelAt(imageSettings, "PDF 名称", 0, 9, 88, 25, 10, Palette.Ink);
            pdfName = new TextBox { Text = "图片合并.pdf", AccessibleName = "PDF 名称" };
            nameField = FieldAt(imageSettings, pdfName, 90, 0, 270, 38);
            pageLabel = LabelAt(imageSettings, "页面", 386, 9, 49, 25, 10, Palette.Ink);
            format = Combo("页面", new[] { "适应原图尺寸（无白边）", "A4 纸张（等比居中）" });
            formatField = FieldAt(imageSettings, format, 438, 0, 322, 38);
            saveModeLabel = LabelAt(wordSettings, "保存方式", 0, 9, 88, 25, 10, Palette.Ink);
            saveMode = Combo("保存方式", new[] { "指定保存位置", "原文件所在文件夹" });
            saveModeField = FieldAt(wordSettings, saveMode, 90, 0, 270, 38);
            saveDescription = LabelAt(wordSettings, "每份文档独立输出为 PDF", 386, 10, 374, 25, 9, Palette.Muted);
            saveMode.SelectedIndexChanged += delegate { UpdateActions(); SetReadyStatus(); };

            status = LabelAt(this, "", 278, 655, 760, 25, 10, Palette.Green);
            status.AutoEllipsis = true;
            progress = new ProgressBar { Bounds = new Rectangle(278, 686, 760, 4), Minimum = 0, Maximum = 100 };
            Controls.Add(progress);
            open = ButtonAt(this, "打开保存位置", 278, 706, 146, 38, delegate { OpenSaveLocation(); });
            details = ButtonAt(this, "转换记录", 436, 706, 104, 38, delegate {
                MessageBox.Show(this, outcomes.Count == 0 ? "还没有转换记录。" : String.Join("\n\n", outcomes), "转换记录", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            stop = ButtonAt(this, "取消", 782, 706, 76, 38, delegate {
                cancel = true; stop.Enabled = false; status.Text = "正在结束当前文件，请稍候…";
            });
            start = ButtonAt(this, "生成 PDF", 870, 706, 168, 38, delegate { BeginConvert(); });
            start.Primary = true;
            editable.AddRange(new Control[] { imageMode, wordMode, audioMode, add, list, pdfName, format, saveMode, details });
            AcceptButton = start;
            tips.SetToolTip(list, "Ctrl+A 全选；Delete 从列表移除。图片按列表顺序生成页面；Word 与音频各自独立输出。");
            tips.SetToolTip(folder, "选择“原文件所在文件夹”时，不使用此路径。");
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { cancel = true; e.Cancel = true; status.Text = "正在结束当前转换，完成后即可关闭。"; }
                else StopAudioPreviews();
            };
            Shown += delegate {
                MinimumSize = SizeFromClientSize(new Size(Px(980), Px(680)));
                ArrangeWorkspace(); UpdateActions();
                foreach (string audioPath in queues[2]) ScheduleAudioPreview(audioPath);
            };
            layoutReady = true;
            Switch(0);
        }

        Label LabelAt(Control parent, string text, int x, int y, int width, int height, float size, Color color)
        {
            Label label = new Label { Text = text, Bounds = new Rectangle(x, y, width, height), ForeColor = color,
                Font = new Font("等线", size), BackColor = parent.BackColor, AutoEllipsis = false };
            parent.Controls.Add(label); return label;
        }
        ActionButton ButtonAt(Control parent, string text, int x, int y, int width, int height, EventHandler action)
        {
            ActionButton button = new ActionButton { Text = text, Bounds = new Rectangle(x, y, width, height) };
            button.Click += action; parent.Controls.Add(button); return button;
        }
        RoundedField FieldAt(Control parent, Control editor, int x, int y, int width, int height)
        {
            RoundedField field = new RoundedField(editor) { Bounds = new Rectangle(x, y, width, height) };
            parent.Controls.Add(field); return field;
        }
        ComboBox Combo(string name, string[] values)
        {
            ComboBox combo = new FilledComboBox { AccessibleName = name };
            combo.Items.AddRange(values); combo.SelectedIndex = 0; return combo;
        }
        void Switch(int next)
        {
            if (busy || next < 0 || next > 2) return;
            saveFolders[mode] = folder.Text;
            mode = next; folder.Text = saveFolders[mode];
            title.Text = mode == 0 ? "将多张图片合并为单个 PDF" : mode == 1 ? "将 Word 文档批量转换为 PDF" : "将音频批量转换为无损 WAV";
            subtitle.Text = mode == 0 ? "支持拖拽添加与自由排序，每张图片自动生成独立页面" : mode == 1 ? "保留原始排版与分页，每份文档独立输出为 PDF" : "保持原始采样率与位深，每个音频独立输出为 WAV";
            add.Text = mode == 0 ? "＋ 添加图片" : mode == 1 ? "＋ 添加 Word" : "＋ 添加音频";
            hint.Text = mode == 0 ? "支持 JPG、JPEG、PNG、BMP 格式 · 选中条目后可调整顺序" : mode == 1 ? "支持 DOC、DOCX 格式 · 保留原文档排版与分页" : "支持 FLAC、OGG、OPUS、APE、WV 等格式 · 保持原参数与音量";
            empty.Text = mode == 0 ? "将图片拖拽到此处，或点击「＋ 添加图片」" : mode == 1 ? "将 Word 文档拖拽到此处，或点击「＋ 添加 Word」" : "将音频文件拖拽到此处，或点击「＋ 添加音频」";
            imageSettings.Visible = mode == 0; wordSettings.Visible = mode > 0;
            up.Visible = down.Visible = mode == 0;
            ConfigureColumns();
            imageMode.Active = mode == 0; wordMode.Active = mode == 1; audioMode.Active = mode == 2;
            imageMode.Invalidate(); wordMode.Invalidate(); audioMode.Invalidate();
            start.Text = mode == 0 ? "生成 PDF" : mode == 1 ? "开始转换" : "导出 WAV";
            progress.Value = 0;
            RefreshList(); ArrangeWorkspace();
        }
        void UpdateActions()
        {
            if (start == null) return;
            bool hasFiles = queues[mode].Count > 0;
            int selected = list.SelectedIndices.Count;
            bool single = selected == 1;
            up.Enabled = !busy && mode == 0 && single && list.SelectedIndices[0] > 0;
            down.Enabled = !busy && mode == 0 && single && list.SelectedIndices[0] < queues[mode].Count - 1;
            remove.Enabled = !busy && selected > 0;
            clear.Enabled = !busy && hasFiles;
            start.Enabled = !busy && hasFiles && (mode == 0 || mode == 1 && environmentAvailable || mode == 2 && audioEnvironmentAvailable);
            stop.Visible = busy;
            stop.Enabled = busy && !cancel;
            bool chosenFolder = mode == 0 || saveMode.SelectedIndex == 0;
            folderField.Enabled = !busy && chosenFolder;
            browse.Enabled = !busy && chosenFolder;
            saveDescription.Text = "同名文件将自动添加序号";
        }
        void SetReadyStatus()
        {
            if (status == null || busy) return;
            status.ForeColor = Palette.Green;
            if (mode == 0) status.Text = queues[0].Count == 0 ? "请先添加图片" : "准备就绪，共 " + queues[0].Count + " 页";
            else if (mode == 1 && !environmentAvailable) { status.Text = "未检测到可用的文档转换环境，请先安装并初始化办公软件"; status.ForeColor = Palette.Error; }
            else if (mode == 2 && !audioEnvironmentAvailable) { status.Text = "缺少音频转换组件，请检查 tools 文件夹"; status.ForeColor = Palette.Error; }
            else if (mode == 1) status.Text = queues[1].Count == 0 ? "请先添加 Word 文档" : "准备就绪 · 同名文件将自动重命名";
            else status.Text = queues[2].Count == 0 ? "请先添加音频" : "准备就绪 · 输出后自动校验音频数据一致性";
        }
        internal void AddFiles(string[] paths)
        {
            if (busy) return;
            int ignored = 0;
            int visibleCount = queues[mode].Count;
            foreach (string path in paths)
            {
                string full;
                try { full = Path.GetFullPath(path); } catch { ignored++; continue; }
                string extension = Path.GetExtension(full).ToLowerInvariant();
                int target = Files.Images.Contains(extension) ? 0 : Files.Documents.Contains(extension) ? 1 : Files.Audio.Contains(extension) ? 2 : -1;
                if (target < 0 || !File.Exists(full)) { ignored++; continue; }
                if (!queuePaths[target].Add(full)) continue;
                queues[target].Add(full);
                if (target > 0) itemStates[full] = "待转换";
                if (target == 2) ScheduleAudioPreview(full);
            }
            bool switched = false;
            if (queues[mode].Count == 0)
            {
                int target = Array.FindIndex(queues, queue => queue.Count > 0);
                if (target >= 0) { Switch(target); switched = true; }
            }
            if (!switched && queues[mode].Count > visibleCount)
            {
                updatingList = true; list.BeginUpdate();
                try { AppendRows(visibleCount); }
                finally { list.EndUpdate(); updatingList = false; }
                UpdateListSummary();
            }
            if (ignored > 0 && (mode == 0 || mode == 1 && environmentAvailable || mode == 2 && audioEnvironmentAvailable)) status.Text += " · 已忽略 " + ignored + " 个不支持或不存在的文件";
        }
        void OnDragEnter(object sender, DragEventArgs e) { e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; }
        void OnDragDrop(object sender, DragEventArgs e) { if (!busy && e.Data.GetDataPresent(DataFormats.FileDrop)) AddFiles((string[])e.Data.GetData(DataFormats.FileDrop)); }
        void ChooseFiles()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Multiselect = true, CheckFileExists = true,
                Title = mode == 0 ? "添加要生成 PDF 的图片" : mode == 1 ? "添加要转换的 Word 文档" : "添加要导出 WAV 的音频",
                Filter = mode == 0 ? "图片文件|*.jpg;*.jpeg;*.png;*.bmp" : mode == 1 ? "Word 文档|*.docx;*.doc" : "音频文件|*.flac;*.ogg;*.opus;*.ape;*.wv;*.wav" })
                if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(dialog.FileNames);
        }
        ListViewItem CreateRow(string path, int index)
        {
            string state = mode == 0 ? (index + 1).ToString("D2") : itemStates[path];
            ListViewItem item = new ListViewItem(state) { Tag = path, ToolTipText = path, UseItemStyleForSubItems = false };
            item.SubItems.Add(Path.GetFileName(path));
            string parameters;
            if (mode == 2)
            {
                AudioInfo info;
                item.SubItems.Add(audioMetadata.TryGetValue(path, out info) ? info.DurationText : audioParameters.ContainsKey(path) || !audioEnvironmentAvailable ? "--:--" : "读取中…");
                item.SubItems.Add(audioParameters.TryGetValue(path, out parameters) ? parameters : audioEnvironmentAvailable ? "读取中…" : "缺少音频组件");
            }
            else item.SubItems.Add(Path.GetDirectoryName(path));
            PaintState(item, state);
            visibleRows[path] = item;
            return item;
        }
        void AppendRows(int first)
        {
            ListViewItem[] rows = new ListViewItem[queues[mode].Count - first];
            for (int i = first; i < queues[mode].Count; i++) rows[i - first] = CreateRow(queues[mode][i], i);
            list.Items.AddRange(rows);
        }
        void RefreshList()
        {
            updatingList = true; list.BeginUpdate();
            try { list.Items.Clear(); visibleRows.Clear(); AppendRows(0); }
            finally { list.EndUpdate(); updatingList = false; }
            UpdateListSummary();
        }
        void UpdateListSummary()
        {
            bool isEmpty = queues[mode].Count == 0;
            list.Visible = !isEmpty;
            empty.Visible = isEmpty;
            if (isEmpty) empty.BringToFront();
            count.Text = queues[mode].Count + " 个文件";
            FitColumns(); UpdateActions(); SetReadyStatus();
        }
        static void PaintState(ListViewItem item, string state)
        {
            item.ForeColor = state == "转换失败" ? Palette.Error : state == "已完成" || state == "转换中" || state == "校验中" || state == "检测中" ? Palette.Green : Palette.Muted;
            for (int i = 1; i < item.SubItems.Count; i++) item.SubItems[i].ForeColor = Palette.Ink;
        }
        void SetItemState(string path, string state)
        {
            itemStates[path] = state;
            ListViewItem item;
            if (mode == 0 || !visibleRows.TryGetValue(path, out item) || item.Text == state) return;
            item.Text = state; PaintState(item, state);
        }
        void MoveSelected(int direction)
        {
            if (busy || mode != 0 || list.SelectedIndices.Count != 1) return;
            int index = list.SelectedIndices[0], next = index + direction;
            if (next < 0 || next >= queues[0].Count) return;
            string path = queues[0][index]; queues[0].RemoveAt(index); queues[0].Insert(next, path);
            updatingList = true; list.BeginUpdate();
            try
            {
                ListViewItem item = list.Items[index];
                list.Items.RemoveAt(index); list.Items.Insert(next, item);
                list.Items[index].Text = (index + 1).ToString("D2");
                item.Text = (next + 1).ToString("D2"); item.Selected = true; item.Focused = true;
            }
            finally { list.EndUpdate(); updatingList = false; }
            list.Items[next].EnsureVisible(); list.Focus(); UpdateActions();
        }
        void RemoveSelected()
        {
            if (busy || list.SelectedIndices.Count == 0) return;
            int[] indices = list.SelectedIndices.Cast<int>().OrderByDescending(x => x).ToArray();
            updatingList = true; list.BeginUpdate();
            try
            {
                foreach (int index in indices)
                {
                    string path = queues[mode][index];
                    if (mode > 0) { itemStates.Remove(path); audioParameters.Remove(path); audioMetadata.Remove(path); }
                    queues[mode].RemoveAt(index); queuePaths[mode].Remove(path); visibleRows.Remove(path); list.Items.RemoveAt(index);
                }
                if (mode == 0) for (int i = indices.Min(); i < list.Items.Count; i++) list.Items[i].Text = (i + 1).ToString("D2");
            }
            finally { list.EndUpdate(); updatingList = false; }
            UpdateListSummary();
        }
        void OpenSaveLocation()
        {
            try
            {
                if (mode > 0 && saveMode.SelectedIndex == 1)
                {
                    string selected = list.SelectedItems.Count > 0 ? (string)list.SelectedItems[0].Tag : queues[mode].FirstOrDefault();
                    if (selected == null) { ShowError("请先添加或选中一个文件。"); return; }
                    Files.OpenFolder(Path.GetDirectoryName(selected));
                }
                else Files.OpenFolder(Path.GetFullPath(folder.Text.Trim()));
            }
            catch (Exception error) { ShowError(error.Message); }
        }
        void ShowError(string text) { MessageBox.Show(this, text, "转换提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        void Ui(Action action)
        {
            if (shuttingDown || IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(new Action(delegate { if (!shuttingDown && !IsDisposed && !Disposing) action(); })); }
            catch (InvalidOperationException) { if (!shuttingDown && !IsDisposed && !Disposing) throw; }
        }

        void BeginConvert()
        {
            if (busy || queues[mode].Count == 0) return;
            string outputDirectory = null, outputFile = null;
            bool sourceFolders = mode > 0 && saveMode.SelectedIndex == 1;
            try
            {
                if (!sourceFolders)
                {
                    if (String.IsNullOrWhiteSpace(folder.Text)) throw new ArgumentException("请选择保存位置。");
                    outputDirectory = Path.GetFullPath(folder.Text.Trim()); Directory.CreateDirectory(outputDirectory);
                }
                if (mode == 0)
                {
                    string name = pdfName.Text.Trim();
                    if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                    if (String.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".") || name.EndsWith(" "))
                        throw new ArgumentException("请填写有效的 PDF 文件名，名称中不能包含路径符号。");
                    outputFile = Files.Available(outputDirectory, name + ".pdf");
                }
                else if (mode == 1 && !environmentAvailable) throw new InvalidOperationException("未检测到可用的文档转换环境。");
                else if (mode == 2 && !audioEnvironmentAvailable) throw new InvalidOperationException("缺少音频转换组件。");
            }
            catch (Exception error) { ShowError(error.Message); return; }
            string[] inputs = queues[mode].ToArray(); int jobMode = mode;
            bool a4 = format.SelectedIndex == 1;
            FlushUiUpdates(true);
            if (jobMode > 0)
            {
                list.BeginUpdate();
                try { foreach (string path in inputs) SetItemState(path, "待转换"); }
                finally { list.EndUpdate(); }
            }
            busy = true; cancel = false; progress.Value = 0; outcomes.Clear();
            foreach (Control control in editable) control.Enabled = false;
            UpdateActions(); status.ForeColor = Palette.Green; status.Text = "正在准备转换…";
            Thread worker = new Thread(delegate()
            {
                int succeeded = 0, failed = 0;
                bool wasCancelled = false;
                Action<double, string> report = delegate(double done, string text) { QueueProgress(Math.Max(0, Math.Min(100, (int)(done * 100 / inputs.Length))), text); };
                Action<string, string> fileState = QueueItemState;
                try
                {
                    if (cancel) throw new OperationCanceledException();
                    if (jobMode == 0)
                    {
                        PdfImages.Convert(inputs, outputFile, a4, delegate(int done, string text) { report(done, text); }, delegate { return cancel; });
                        outcomes.Add("已生成：" + outputFile); succeeded = inputs.Length;
                    }
                    else
                    {
                        WordExporter word = null;
                        AudioExporter audio = null;
                        try
                        {
                            if (jobMode == 1) word = new WordExporter(); else audio = new AudioExporter();
                            for (int i = 0; i < inputs.Length; i++)
                            {
                                if (cancel) { wasCancelled = true; break; }
                                string source = inputs[i]; int index = i;
                                fileState(source, jobMode == 1 ? "转换中" : "检测中");
                                report(i, "正在处理：" + Path.GetFileName(source));
                                try
                                {
                                    string destination = sourceFolders ? Path.GetDirectoryName(source) : outputDirectory;
                                    if (jobMode == 1)
                                    {
                                        string result = word.Convert(source, destination);
                                        outcomes.Add("已生成：" + result);
                                    }
                                    else
                                    {
                                        AudioResult result = audio.Convert(source, destination, delegate { return cancel; },
                                            delegate(double fraction, string phase) {
                                                fileState(source, phase.StartsWith("校验") ? "校验中" : phase.StartsWith("读取") ? "检测中" : phase == "已完成" ? "已完成" : "转换中");
                                                report(index + fraction, Path.GetFileName(source) + "：" + phase);
                                            },
                                            delegate(AudioInfo info) { QueueAudioMetadata(source, info, null); });
                                        outcomes.Add("已生成：" + result.OutputPath + "\n" + result.Output.Description + "\n音频采样数据校验一致\nSHA-256：" + result.PcmHash);
                                    }
                                    succeeded++; fileState(source, "已完成");
                                }
                                catch (OperationCanceledException) { wasCancelled = true; fileState(source, "已取消"); break; }
                                catch (Exception error) { failed++; outcomes.Add("转换失败：" + Path.GetFileName(source) + "\n" + error.Message); fileState(source, "转换失败"); }
                                report(i + 1, "已处理 " + (i + 1) + " / " + inputs.Length + " 个文件");
                            }
                        }
                        finally { if (word != null) word.Dispose(); }
                    }
                }
                catch (OperationCanceledException) { wasCancelled = true; }
                catch (Exception error)
                {
                    failed = jobMode > 0 ? inputs.Length - succeeded : 1;
                    outcomes.Add(error.Message);
                    if (jobMode > 0) Ui(delegate {
                        FlushUiUpdates(true);
                        list.BeginUpdate();
                        try { foreach (string path in inputs) if (itemStates[path] != "已完成") SetItemState(path, "转换失败"); }
                        finally { list.EndUpdate(); }
                    });
                }
                Ui(delegate {
                    FlushUiUpdates(true);
                    if (wasCancelled && jobMode > 0)
                        foreach (string path in inputs) if (itemStates[path] == "待转换") SetItemState(path, "已取消");
                    busy = false; foreach (Control control in editable) control.Enabled = true;
                    UpdateActions();
                    status.ForeColor = failed > 0 ? Palette.Error : Palette.Green;
                    if (wasCancelled) status.Text = "已取消 · 已完成的文件仍保留在保存位置";
                    else if (failed > 0) status.Text = jobMode == 0 ? "生成失败 · 点击“转换记录”查看原因" : "已完成 " + succeeded + " 个，失败 " + failed + " 个 · 点击“转换记录”查看原因";
                    else { progress.Value = 100; status.Text = jobMode == 0 ? "已生成 PDF，共 " + inputs.Length + " 页 · 可打开保存位置查看" : jobMode == 1 ? "已完成 " + succeeded + " 份文档转换" : "已完成 " + succeeded + " 个音频 · 音频数据校验一致"; }
                    if (failed > 0) ShowError(String.Join("\n\n", outcomes.Where(x => !x.StartsWith("已生成："))));
                });
            });
            worker.IsBackground = false;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }
    }
}
