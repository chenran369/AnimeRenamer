using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AnimeRenamer
{
    public class MainForm : Form
    {
        private readonly Config _config;
        private readonly string _dataDir;
        private readonly string _startPath;
        private readonly List<FolderJob> _jobs = new List<FolderJob>();
        private FolderJob _current;

        private bool _busy;
        private int _busyDepth;
        private bool _loading;
        private bool _restricted;
        private bool _rendering;
        private int _searchToken;

        private HeaderPanel _header;
        private TableLayoutPanel _rightTable;
        private RowStyle _matchRowStyle, _namingRowStyle;
        private ListBox _folderList;
        private Label _emptyHint;
        private Label _lblFolderCount;

        private FlatButton _btnAdd, _btnRemove, _btnClear;
        private FlatButton _btnSearch, _btnPreview, _btnExecute, _btnUndo, _btnAlign;
        private FlatButton _btnSelectAll, _btnSelectNone, _btnLog;
        private CheckBox _chkRecursive, _chkSubs, _chkSidecars, _chkSpecials, _chkSkipExtras;
        private TextBox _txtKeyword, _txtTitle;
        private ComboBox _cboSource, _cboResults, _cboTemplate, _cboLanguage;
        private NumericUpDown _numSeason, _numOffset;
        private DataGridView _grid;
        private Label _lblStatus, _lblMatch, _lblSummary, _lblExample;
        private ThinProgress _progress;
        private RichTextBox _log;
        private CardPanel _logCard;
        private Timer _debounce;
        private int _colCheck, _colStatus, _colOld, _colNew, _colNote;

        private static readonly string[] PresetTemplates = {
            "{title} - S{season:02d}E{ep:02d}",
            "{title} - S{season:02d}E{ep:02d} - {ep_title}",
            "{title} 第{ep:02d}话",
            "{title} - {ep:02d}",
            "{title}.S{season:02d}E{ep:02d}",
            "{title} - S{season:02d}E{ep:02d} - {resolution}"
        };

        public MainForm(Config config, string dataDir, string startPath)
        {
            _config = config;
            _dataDir = dataDir;
            _startPath = startPath;

            Text = "番剧批量重命名";
            BackColor = Theme.Bg;
            Font = Theme.F(9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Clamp(config.WindowW, 1040, 2400), Clamp(config.WindowH, 600, 1400));
            MinimumSize = new Size(1040, 600);
            AllowDrop = true;
            KeyPreview = true;
            DoubleBuffered = true;

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            SuspendLayout();
            BuildUi();
            ResumeLayout(true);
            LoadConfigUi();

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            KeyDown += OnKeyDown;
            Shown += OnShown;
            FormClosing += OnFormClosing;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            MinimumSize = new Size(1040, 600);
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int w = Math.Min(Width, wa.Width);
            int h = Math.Min(Height, wa.Height);
            if (w != Width || h != Height) Size = new Size(w, h);
            if (Left < wa.Left) Left = wa.Left;
            if (Top < wa.Top) Top = wa.Top;
            AdaptHeights();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            AdaptHeights();
        }

        private void AdaptHeights()
        {
            if (_matchRowStyle == null || _namingRowStyle == null) return;
            float match = 176;
            float naming = 158;
            if (ClientSize.Height < 780) { match = 166; }
            if (ClientSize.Height < 700) { match = 152; }
            if (Math.Abs(_matchRowStyle.Height - match) < 0.5f && Math.Abs(_namingRowStyle.Height - naming) < 0.5f) return;
            _matchRowStyle.Height = match;
            _namingRowStyle.Height = naming;
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.BackColor = Theme.Bg;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            Controls.Add(root);

            _header = new HeaderPanel();
            _header.Dock = DockStyle.Fill;
            _header.Margin = new Padding(0);
            _header.Subtitle = "自动识别季集  ·  匹配 Bangumi / TMDB  ·  字幕同步  ·  一键撤销";
            _header.HelpClicked += OnHelp;
            _header.SettingsClicked += OnSettings;
            root.Controls.Add(_header, 0, 0);

            var body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = Theme.Bg;
            body.Padding = new Padding(18, 14, 18, 12);
            root.Controls.Add(body, 0, 1);

            var bodyTable = new TableLayoutPanel();
            bodyTable.Dock = DockStyle.Fill;
            bodyTable.ColumnCount = 2;
            bodyTable.RowCount = 1;
            bodyTable.BackColor = Theme.Bg;
            bodyTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 332));
            bodyTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.Controls.Add(bodyTable);

            CardPanel left = BuildLeftCard();
            left.Margin = new Padding(0, 0, 14, 0);
            bodyTable.Controls.Add(left, 0, 0);

            _rightTable = new TableLayoutPanel();
            _rightTable.Dock = DockStyle.Fill;
            _rightTable.ColumnCount = 1;
            _rightTable.RowCount = 4;
            _rightTable.BackColor = Theme.Bg;
            _rightTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
            _rightTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
            _rightTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _rightTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            _matchRowStyle = _rightTable.RowStyles[0];
            _namingRowStyle = _rightTable.RowStyles[1];
            bodyTable.Controls.Add(_rightTable, 1, 0);

            CardPanel matchCard = BuildMatchCard();
            matchCard.Margin = new Padding(0, 0, 0, 12);
            _rightTable.Controls.Add(matchCard, 0, 0);

            CardPanel namingCard = BuildNamingCard();
            namingCard.Margin = new Padding(0, 0, 0, 12);
            _rightTable.Controls.Add(namingCard, 0, 1);

            CardPanel previewCard = BuildPreviewCard();
            previewCard.Margin = new Padding(0, 0, 0, 12);
            _rightTable.Controls.Add(previewCard, 0, 2);

            _logCard = BuildLogCard();
            _logCard.Margin = new Padding(0);
            _rightTable.Controls.Add(_logCard, 0, 3);
            _rightTable.RowStyles[3].Height = 0;
            _btnLog.Text = "显示日志";

            root.Controls.Add(BuildBottomBar(), 0, 2);

            _debounce = new Timer();
            _debounce.Interval = 260;
            _debounce.Tick += delegate
            {
                _debounce.Stop();
                RefreshPreview();
                SaveConfig();
            };

            EnableDropRecursive(this);
        }

        private void EnableDropRecursive(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (!(c is TextBoxBase) && !(c is ComboBox))
                {
                    c.AllowDrop = true;
                    c.DragEnter += OnDragEnter;
                    c.DragDrop += OnDragDrop;
                }
                EnableDropRecursive(c);
            }
        }

        private CardPanel BuildLeftCard()
        {
            var card = new CardPanel();
            card.Dock = DockStyle.Fill;

            var t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 1;
            t.RowCount = 5;
            t.BackColor = Theme.Card;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            card.Controls.Add(t);

            var head = new TableLayoutPanel();
            head.Dock = DockStyle.Fill;
            head.ColumnCount = 2;
            head.RowCount = 1;
            head.BackColor = Theme.Card;
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            Label title = Ui.CardTitle("番剧目录");
            title.Dock = DockStyle.Fill;
            head.Controls.Add(title, 0, 0);
            _lblFolderCount = Ui.Label("0", 9f, Theme.Muted, FontStyle.Regular);
            _lblFolderCount.TextAlign = ContentAlignment.MiddleRight;
            _lblFolderCount.Dock = DockStyle.Fill;
            head.Controls.Add(_lblFolderCount, 1, 0);
            t.Controls.Add(head, 0, 0);

            var listHost = new Panel();
            listHost.Dock = DockStyle.Fill;
            listHost.BackColor = Theme.Card;
            listHost.Padding = new Padding(0, 4, 0, 4);
            t.Controls.Add(listHost, 0, 1);

            _folderList = new ListBox();
            _folderList.Dock = DockStyle.Fill;
            _folderList.DrawMode = DrawMode.OwnerDrawFixed;
            _folderList.ItemHeight = 62;
            _folderList.BorderStyle = BorderStyle.None;
            _folderList.BackColor = Theme.Card;
            _folderList.ForeColor = Theme.Text;
            _folderList.IntegralHeight = false;
            _folderList.Font = Theme.F(9.5f);
            _folderList.DrawItem += FolderList_DrawItem;
            _folderList.SelectedIndexChanged += OnFolderSelectionChanged;
            listHost.Controls.Add(_folderList);

            _emptyHint = new Label();
            _emptyHint.Dock = DockStyle.Fill;
            _emptyHint.TextAlign = ContentAlignment.MiddleCenter;
            _emptyHint.ForeColor = Theme.Faint;
            _emptyHint.Font = Theme.F(9.5f);
            _emptyHint.BackColor = Theme.Card;
            _emptyHint.Text = "拖入番剧文件夹\r\n或点击「添加文件夹」";
            _emptyHint.Visible = true;
            listHost.Controls.Add(_emptyHint);
            _emptyHint.BringToFront();

            var btnRow = new FlowLayoutPanel();
            btnRow.Dock = DockStyle.Fill;
            btnRow.FlowDirection = FlowDirection.LeftToRight;
            btnRow.WrapContents = false;
            btnRow.BackColor = Theme.Card;
            btnRow.Padding = new Padding(0, 6, 0, 0);
            t.Controls.Add(btnRow, 0, 2);

            _btnAdd = new FlatButton();
            _btnAdd.Text = "+  添加文件夹";
            _btnAdd.Kind = ButtonKind.Primary;
            _btnAdd.Size = new Size(138, 36);
            _btnAdd.Margin = new Padding(0, 0, 6, 0);
            _btnAdd.Click += OnAddClick;
            btnRow.Controls.Add(_btnAdd);

            _btnRemove = new FlatButton();
            _btnRemove.Text = "移除";
            _btnRemove.Kind = ButtonKind.Secondary;
            _btnRemove.Size = new Size(62, 36);
            _btnRemove.Margin = new Padding(0, 0, 6, 0);
            _btnRemove.Click += OnRemoveClick;
            btnRow.Controls.Add(_btnRemove);

            _btnClear = new FlatButton();
            _btnClear.Text = "清空";
            _btnClear.Kind = ButtonKind.Secondary;
            _btnClear.Size = new Size(58, 36);
            _btnClear.Click += OnClearClick;
            btnRow.Controls.Add(_btnClear);

            _chkRecursive = Ui.Check("包含子文件夹（递归扫描）", _config.Recursive);
            _chkRecursive.Dock = DockStyle.Fill;
            _chkRecursive.Margin = new Padding(2, 4, 0, 0);
            _chkRecursive.CheckedChanged += OnRecursiveChanged;
            t.Controls.Add(_chkRecursive, 0, 3);

            Label hint = Ui.Label("提示：文件夹可以直接拖进窗口", 8.25f, Theme.Faint, FontStyle.Regular);
            hint.Dock = DockStyle.Fill;
            t.Controls.Add(hint, 0, 4);

            return card;
        }

        private CardPanel BuildMatchCard()
        {
            var card = new CardPanel();
            card.Dock = DockStyle.Fill;

            var outer = new TableLayoutPanel();
            outer.Dock = DockStyle.Fill;
            outer.ColumnCount = 1;
            outer.RowCount = 4;
            outer.BackColor = Theme.Card;
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            card.Controls.Add(outer);

            var head = new TableLayoutPanel();
            head.Dock = DockStyle.Fill;
            head.ColumnCount = 2;
            head.RowCount = 1;
            head.BackColor = Theme.Card;
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            Label title = Ui.CardTitle("① 匹配番剧");
            title.Dock = DockStyle.Fill;
            head.Controls.Add(title, 0, 0);
            Label tip = Ui.Label("自动匹配后可修改标题、季与集数偏移", 8.25f, Theme.Faint, FontStyle.Regular);
            tip.TextAlign = ContentAlignment.MiddleRight;
            tip.Dock = DockStyle.Fill;
            head.Controls.Add(tip, 1, 0);
            outer.Controls.Add(head, 0, 0);

            _txtKeyword = Ui.TextBox();
            _txtKeyword.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; StartSearch(); }
            };
            _btnSearch = new FlatButton();
            _btnSearch.Text = "搜索";
            _btnSearch.Kind = ButtonKind.Soft;
            _btnSearch.Font = Theme.F(9f);
            _btnSearch.Click += delegate { StartSearch(); };

            _cboSource = Ui.Combo(false);
            _cboSource.Items.AddRange(new object[] { "Bangumi 番组计划", "TMDB", "仅本地" });
            _cboSource.SelectedIndexChanged += OnSourceChanged;

            _cboLanguage = Ui.Combo(false);
            _cboLanguage.Items.AddRange(new object[] { "中文标题", "原名标题" });
            _cboLanguage.SelectedIndexChanged += OnLanguageChanged;

            var row1 = MakeRow(new Control[] {
                Ui.Caption("关键词"), _txtKeyword, _btnSearch,
                Ui.Caption("数据源"), _cboSource,
                Ui.Caption("语言"), _cboLanguage
            }, new int[] { 46, -100, 74, 52, 132, 44, 84 });
            outer.Controls.Add(row1, 0, 1);

            _cboResults = Ui.Combo(false);
            _cboResults.SelectedIndexChanged += OnResultChanged;
            _lblMatch = Ui.Label("", 8.5f, Theme.Muted, FontStyle.Regular);
            _lblMatch.AutoEllipsis = true;
            var row2 = MakeRow(new Control[] { Ui.Caption("匹配"), _cboResults, _lblMatch },
                new int[] { 46, 300, -100 });
            outer.Controls.Add(row2, 0, 2);

            _txtTitle = Ui.TextBox();
            _txtTitle.TextChanged += OnTitleChanged;
            _numSeason = new NumericUpDown();
            _numSeason.Minimum = 1;
            _numSeason.Maximum = 99;
            _numSeason.Value = 1;
            _numSeason.TextAlign = HorizontalAlignment.Center;
            _numSeason.BorderStyle = BorderStyle.FixedSingle;
            _numSeason.Font = Theme.F(9.25f);
            _numSeason.ValueChanged += OnSeasonChanged;

            _numOffset = new NumericUpDown();
            _numOffset.Minimum = -999;
            _numOffset.Maximum = 999;
            _numOffset.Value = 0;
            _numOffset.TextAlign = HorizontalAlignment.Center;
            _numOffset.BorderStyle = BorderStyle.FixedSingle;
            _numOffset.Font = Theme.F(9.25f);
            _numOffset.ValueChanged += OnOffsetChanged;

            _btnAlign = new FlatButton();
            _btnAlign.Text = "对齐 01";
            _btnAlign.Kind = ButtonKind.Secondary;
            _btnAlign.Font = Theme.F(8.5f);
            _btnAlign.Click += OnAlignClick;

            var row3 = MakeRow(new Control[] {
                Ui.Caption("标题"), _txtTitle,
                Ui.Caption("季数"), _numSeason,
                Ui.Caption("集偏移"), _numOffset, _btnAlign
            }, new int[] { 46, -100, 44, 50, 56, 56, 64 });
            outer.Controls.Add(row3, 0, 3);

            return card;
        }

        private CardPanel BuildNamingCard()
        {
            var card = new CardPanel();
            card.Dock = DockStyle.Fill;

            var outer = new TableLayoutPanel();
            outer.Dock = DockStyle.Fill;
            outer.ColumnCount = 1;
            outer.RowCount = 4;
            outer.BackColor = Theme.Card;
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            card.Controls.Add(outer);

            Label title = Ui.CardTitle("② 命名规则");
            title.Dock = DockStyle.Fill;
            outer.Controls.Add(title, 0, 0);

            _cboTemplate = Ui.Combo(true);
            _cboTemplate.TextChanged += OnTemplateChanged;
            _cboTemplate.Items.AddRange(PresetTemplates);

            var reset = new FlatButton();
            reset.Text = "恢复默认";
            reset.Kind = ButtonKind.Secondary;
            reset.Font = Theme.F(8.5f);
            reset.Click += delegate
            {
                _loading = true;
                _cboTemplate.Text = Config.DefaultTemplate;
                _loading = false;
                _config.Template = Config.DefaultTemplate;
                SchedulePreview();
                SaveConfig();
            };

            var row1 = MakeRow(new Control[] { Ui.Caption("模板"), _cboTemplate, reset },
                new int[] { 46, -100, 86 });
            outer.Controls.Add(row1, 0, 1);

            var checks = new FlowLayoutPanel();
            checks.Dock = DockStyle.Fill;
            checks.FlowDirection = FlowDirection.LeftToRight;
            checks.WrapContents = false;
            checks.BackColor = Theme.Card;
            checks.Margin = new Padding(0, 2, 0, 0);
            outer.Controls.Add(checks, 0, 2);

            _chkSubs = Ui.Check("重命名字幕", _config.RenameSubs);
            _chkSubs.Margin = new Padding(0, 3, 18, 0);
            _chkSubs.CheckedChanged += OnOptionChanged;
            checks.Controls.Add(_chkSubs);

            _chkSidecars = Ui.Check("同步 .nfo / 图片", _config.RenameSidecars);
            _chkSidecars.Margin = new Padding(0, 3, 18, 0);
            _chkSidecars.CheckedChanged += OnOptionChanged;
            checks.Controls.Add(_chkSidecars);

            _chkSpecials = Ui.Check("包含特别篇", _config.IncludeSpecials);
            _chkSpecials.Margin = new Padding(0, 3, 18, 0);
            _chkSpecials.CheckedChanged += OnOptionChanged;
            checks.Controls.Add(_chkSpecials);

            _chkSkipExtras = Ui.Check("跳过片头片尾 / 预告", _config.SkipExtras);
            _chkSkipExtras.Margin = new Padding(0, 3, 18, 0);
            _chkSkipExtras.CheckedChanged += OnOptionChanged;
            checks.Controls.Add(_chkSkipExtras);

            _lblExample = Ui.Label("", 8.5f, Theme.Faint, FontStyle.Regular);
            _lblExample.AutoEllipsis = true;
            _lblExample.Dock = DockStyle.Fill;
            _lblExample.Margin = new Padding(0, 0, 0, 0);
            outer.Controls.Add(_lblExample, 0, 3);

            return card;
        }

        private CardPanel BuildPreviewCard()
        {
            var card = new CardPanel();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(12, 10, 12, 12);

            var t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 1;
            t.RowCount = 2;
            t.BackColor = Theme.Card;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.Controls.Add(t);

            var toolbar = new Panel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.BackColor = Theme.Card;
            t.Controls.Add(toolbar, 0, 0);

            var leftFlow = new FlowLayoutPanel();
            leftFlow.Dock = DockStyle.Left;
            leftFlow.Width = 250;
            leftFlow.FlowDirection = FlowDirection.LeftToRight;
            leftFlow.WrapContents = false;
            leftFlow.BackColor = Theme.Card;
            leftFlow.Padding = new Padding(0, 4, 0, 0);
            toolbar.Controls.Add(leftFlow);

            _btnPreview = new FlatButton();
            _btnPreview.Text = "刷新预览";
            _btnPreview.Kind = ButtonKind.Soft;
            _btnPreview.Size = new Size(92, 30);
            _btnPreview.Margin = new Padding(0, 0, 8, 0);
            _btnPreview.Click += delegate { RefreshPreview(); };
            leftFlow.Controls.Add(_btnPreview);

            _btnSelectAll = new FlatButton();
            _btnSelectAll.Text = "全选";
            _btnSelectAll.Kind = ButtonKind.Secondary;
            _btnSelectAll.Size = new Size(60, 30);
            _btnSelectAll.Margin = new Padding(0, 0, 8, 0);
            _btnSelectAll.Click += delegate { SetAllIncluded(true); };
            leftFlow.Controls.Add(_btnSelectAll);

            _btnSelectNone = new FlatButton();
            _btnSelectNone.Text = "全不选";
            _btnSelectNone.Kind = ButtonKind.Secondary;
            _btnSelectNone.Size = new Size(68, 30);
            _btnSelectNone.Click += delegate { SetAllIncluded(false); };
            leftFlow.Controls.Add(_btnSelectNone);

            var rightFlow = new FlowLayoutPanel();
            rightFlow.Dock = DockStyle.Right;
            rightFlow.Width = 380;
            rightFlow.FlowDirection = FlowDirection.RightToLeft;
            rightFlow.WrapContents = false;
            rightFlow.BackColor = Theme.Card;
            rightFlow.Padding = new Padding(0, 4, 0, 0);
            toolbar.Controls.Add(rightFlow);

            _btnLog = new FlatButton();
            _btnLog.Text = "隐藏日志";
            _btnLog.Kind = ButtonKind.Secondary;
            _btnLog.Size = new Size(82, 30);
            _btnLog.Margin = new Padding(8, 0, 0, 0);
            _btnLog.Click += OnToggleLog;
            rightFlow.Controls.Add(_btnLog);

            _lblSummary = Ui.Label("", 8.5f, Theme.Muted, FontStyle.Regular);
            _lblSummary.TextAlign = ContentAlignment.MiddleRight;
            _lblSummary.AutoEllipsis = true;
            _lblSummary.Size = new Size(280, 30);
            rightFlow.Controls.Add(_lblSummary);

            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.AutoGenerateColumns = false;
            Ui.StyleGrid(_grid);
            _grid.CellContentClick += Grid_CellContentClick;
            _grid.CellPainting += Grid_CellPainting;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CellToolTipTextNeeded += Grid_CellToolTip;
            _grid.CellDoubleClick += Grid_CellDoubleClick;
            _grid.ContextMenuStrip = BuildGridMenu();
            t.Controls.Add(_grid, 0, 1);

            var colCheck = new DataGridViewCheckBoxColumn();
            colCheck.Name = "colCheck";
            colCheck.HeaderText = "";
            colCheck.DataPropertyName = "Included";
            colCheck.Width = 34;
            colCheck.MinimumWidth = 34;
            colCheck.ReadOnly = true;
            colCheck.SortMode = DataGridViewColumnSortMode.NotSortable;
            colCheck.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _grid.Columns.Add(colCheck);
            _colCheck = colCheck.Index;

            var colStatus = new DataGridViewTextBoxColumn();
            colStatus.Name = "colStatus";
            colStatus.HeaderText = "状态";
            colStatus.DataPropertyName = "Status";
            colStatus.Width = 86;
            colStatus.ReadOnly = true;
            colStatus.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(colStatus);
            _colStatus = colStatus.Index;

            var colOld = new DataGridViewTextBoxColumn();
            colOld.Name = "colOld";
            colOld.HeaderText = "原文件名";
            colOld.DataPropertyName = "OldName";
            colOld.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colOld.FillWeight = 36;
            colOld.MinimumWidth = 160;
            colOld.ReadOnly = true;
            colOld.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(colOld);
            _colOld = colOld.Index;

            var colNew = new DataGridViewTextBoxColumn();
            colNew.Name = "colNew";
            colNew.HeaderText = "新文件名";
            colNew.DataPropertyName = "NewName";
            colNew.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colNew.FillWeight = 46;
            colNew.MinimumWidth = 180;
            colNew.ReadOnly = true;
            colNew.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(colNew);
            _colNew = colNew.Index;

            var colNote = new DataGridViewTextBoxColumn();
            colNote.Name = "colNote";
            colNote.HeaderText = "备注";
            colNote.DataPropertyName = "Note";
            colNote.Width = 160;
            colNote.ReadOnly = true;
            colNote.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(colNote);
            _colNote = colNote.Index;

            return card;
        }

        private ContextMenuStrip BuildGridMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Font = Theme.F(9f);
            menu.Items.Add("打开所在文件夹", null, delegate { OpenSelectedInExplorer(); });
            menu.Items.Add("复制新文件名", null, delegate { CopySelectedName(); });
            menu.Items.Add("复制完整路径", null, delegate { CopySelectedPath(); });
            return menu;
        }

        private CardPanel BuildLogCard()
        {
            var card = new CardPanel();
            card.Dock = DockStyle.Fill;
            card.FillColor = Color.FromArgb(15, 23, 42);
            card.Padding = new Padding(12, 8, 12, 8);

            _log = new RichTextBox();
            _log.Dock = DockStyle.Fill;
            _log.BorderStyle = BorderStyle.None;
            _log.ReadOnly = true;
            _log.BackColor = Color.FromArgb(15, 23, 42);
            _log.ForeColor = Color.FromArgb(203, 213, 225);
            _log.Font = new Font("Consolas", 8.5f);
            _log.DetectUrls = false;
            _log.ScrollBars = RichTextBoxScrollBars.Vertical;
            _log.WordWrap = true;
            card.Controls.Add(_log);
            return card;
        }

        private Panel BuildBottomBar()
        {
            var bar = new Panel();
            bar.Dock = DockStyle.Fill;
            bar.BackColor = Color.White;
            bar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, 0, 0, bar.Width, 0);
            };

            var t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 3;
            t.RowCount = 1;
            t.BackColor = Color.White;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            bar.Controls.Add(t);

            _lblStatus = Ui.Label("就绪 · 选择文件夹后自动识别", 9f, Theme.Muted, FontStyle.Regular);
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.AutoEllipsis = true;
            _lblStatus.Margin = new Padding(18, 0, 10, 0);
            t.Controls.Add(_lblStatus, 0, 0);

            _progress = new ThinProgress();
            _progress.Dock = DockStyle.Fill;
            _progress.Margin = new Padding(0, 29, 16, 29);
            _progress.Maximum = 100;
            t.Controls.Add(_progress, 1, 0);

            var actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.WrapContents = false;
            actions.BackColor = Color.White;
            actions.Padding = new Padding(0, 13, 18, 13);
            t.Controls.Add(actions, 2, 0);

            _btnExecute = new FlatButton();
            _btnExecute.Text = "执行重命名";
            _btnExecute.Kind = ButtonKind.Primary;
            _btnExecute.Size = new Size(150, 40);
            _btnExecute.Font = Theme.F(10f, FontStyle.Bold);
            _btnExecute.Margin = new Padding(10, 0, 0, 0);
            _btnExecute.Click += OnExecuteClick;
            actions.Controls.Add(_btnExecute);

            _btnUndo = new FlatButton();
            _btnUndo.Text = "撤销上次";
            _btnUndo.Kind = ButtonKind.Secondary;
            _btnUndo.Size = new Size(108, 40);
            _btnUndo.Margin = new Padding(0);
            _btnUndo.Click += OnUndoClick;
            actions.Controls.Add(_btnUndo);

            return bar;
        }

        private static TableLayoutPanel MakeRow(Control[] controls, int[] widths)
        {
            var t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = controls.Length;
            t.RowCount = 1;
            t.BackColor = Theme.Card;
            t.Margin = new Padding(0, 3, 0, 3);
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            for (int i = 0; i < controls.Length; i++)
            {
                int w = widths[i];
                if (w < 0) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, -w));
                else t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, w));

                Control c = controls[i];
                c.Dock = DockStyle.Fill;
                c.Margin = new Padding(i == 0 ? 0 : 6, 0, 0, 0);
                if (c.Tag as string == "rowcaption" && c is Label)
                {
                    ((Label)c).TextAlign = ContentAlignment.TopLeft;
                    c.Padding = new Padding(0, 4, 0, 0);
                }
                t.Controls.Add(c, i, 0);
            }
            return t;
        }

        private void LoadConfigUi()
        {
            _restricted = !string.IsNullOrEmpty(Program.DataDirNote);
            _loading = true;
            _cboSource.SelectedIndex = _config.Source == "tmdb" ? 1 : (_config.Source == "local" ? 2 : 0);
            _cboLanguage.SelectedIndex = _config.Language == "orig" ? 1 : 0;
            _chkRecursive.Checked = _config.Recursive;
            _chkSubs.Checked = _config.RenameSubs;
            _chkSidecars.Checked = _config.RenameSidecars;
            _chkSpecials.Checked = _config.IncludeSpecials;
            _chkSkipExtras.Checked = _config.SkipExtras;

            _cboTemplate.Items.Clear();
            foreach (string p in PresetTemplates) _cboTemplate.Items.Add(p);
            if (_config.RecentTemplates != null)
                foreach (string p in _config.RecentTemplates)
                    if (!string.IsNullOrWhiteSpace(p) && !_cboTemplate.Items.Contains(p)) _cboTemplate.Items.Add(p);
            _cboTemplate.Text = _config.Template;

            _progress.Maximum = 100;
            _progress.Value = 0;
            _loading = false;

            Log("欢迎使用番剧批量重命名 v" + Program.VersionText + "。添加文件夹开始，执行后可通过“撤销上次”恢复。",
                Color.FromArgb(148, 163, 184));
            if (!string.IsNullOrEmpty(Program.DataDirNote))
                Log(Program.DataDirNote, Theme.Orange);
            SetIdleStatus();
        }

        private void OnShown(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_startPath))
            {
                string p = _startPath;
                var ignored = AddFolders(new string[] { p });
            }
            UpdateActionState();
            if (_restricted && !_rendering)
            {
                MessageBox.Show(this,
                    "当前窗口运行在受限沙箱中，无法修改文件。\n\n" +
                    "请关闭本窗口，到 Windows 桌面双击：\n" +
                    "「启动番剧重命名.bat」\n\n" +
                    "注意：不要双击 AnimeRenamer.exe。\n" +
                    "用启动 bat 打开后，标题栏应显示 v" + Program.VersionText + "，底部不再出现“受限模式”。",
                    "需要正常权限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_busy)
            {
                if (MessageBox.Show(this, "正在处理中，确定要退出吗？", "提示",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            _config.WindowW = ClientSize.Width;
            _config.WindowH = ClientSize.Height;
            if (_cboTemplate.Text.Trim().Length > 0) _config.Template = _cboTemplate.Text.Trim();
            _config.Save(_dataDir);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.O) { e.SuppressKeyPress = true; OnAddClick(null, EventArgs.Empty); }
            else if (e.KeyCode == Keys.F5) { e.SuppressKeyPress = true; RefreshPreview(); }
            else if (e.KeyCode == Keys.F1) { e.SuppressKeyPress = true; OnHelp(null, EventArgs.Empty); }
            else if (e.Control && e.KeyCode == Keys.Oemcomma) { e.SuppressKeyPress = true; OnSettings(null, EventArgs.Empty); }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items != null && items.Any(Directory.Exists)) e.Effect = DragDropEffects.Copy;
        }

        private async void OnDragDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null) return;
            var dirs = items.Where(Directory.Exists).ToList();
            if (dirs.Count > 0) await AddFolders(dirs);
        }

        private async void OnAddClick(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择要批量重命名的番剧文件夹";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    await AddFolders(new string[] { dlg.SelectedPath });
            }
        }

        private void OnRemoveClick(object sender, EventArgs e)
        {
            if (_current == null) return;
            _jobs.Remove(_current);
            _current = _jobs.FirstOrDefault();
            RefreshFolderList();
            SelectJob(_current, false);
            Log("已从列表移除文件夹（不会影响磁盘文件）。");
        }

        private void OnClearClick(object sender, EventArgs e)
        {
            if (_jobs.Count == 0) return;
            if (MessageBox.Show(this, "清空目录列表？（不会影响磁盘文件）", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _jobs.Clear();
            _current = null;
            RefreshFolderList();
            SelectJob(null, false);
        }

        private async Task AddFolders(IEnumerable<string> paths)
        {
            if (_busy) return;
            var dirs = paths.Where(Directory.Exists).Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (dirs.Count == 0) return;

            BeginBusy("正在扫描文件夹…");
            try
            {
                foreach (string dir in dirs)
                {
                    FolderJob existing = _jobs.FirstOrDefault(j =>
                        string.Equals(Path.GetFullPath(j.Root).TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        SelectJob(existing, false);
                        continue;
                    }

                    var job = new FolderJob();
                    job.Root = dir;
                    job.Recursive = _config.Recursive;
                    job.Template = _config.Template;
                    _lblStatus.Text = "正在扫描 " + dir + " …";

                    List<FileItem> files = await Task.Run(delegate { return Scanner.Scan(dir, job.Recursive); });
                    job.Files = files;
                    InitializeJob(job);
                    _jobs.Add(job);
                    _current = job;
                    RefreshFolderList();
                    SelectJob(job, false);

                    Log("已添加：" + dir + "（" + files.Count + " 个相关文件）");
                    if (files.Count == 0)
                        Log("没有找到视频或字幕文件，请确认目录是否正确。", Theme.Orange);

                    if (_config.Source != "local" && job.Keyword.Length > 0)
                        await SearchAsync(job, true);
                }
            }
            catch (Exception ex)
            {
                Log("添加文件夹失败：" + ex.Message, Theme.Red);
                MessageBox.Show(this, "添加文件夹失败：\n" + ex.Message, "出错了",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                EndBusy();
                UpdateActionState();
                SetIdleStatus();
                Invalidate(true);
            }
        }

        private void InitializeJob(FolderJob job)
        {
            FileItem first = job.Files.FirstOrDefault(f => f.Kind == MediaKind.Video && f.Parsed.HasEpisode)
                             ?? job.Files.FirstOrDefault(f => f.Kind == MediaKind.Video)
                             ?? job.Files.FirstOrDefault();

            if (first != null)
            {
                string detected = NameParser.DetectTitle(job.Root, first.Path);
                if (string.IsNullOrWhiteSpace(detected)) detected = NameParser.CleanTitle(job.DisplayName);
                job.DetectedTitle = detected;
                job.Keyword = detected;
                job.Season = first.Parsed.SeasonExplicit ? first.Parsed.Season :
                    Math.Max(1, NameParser.DetectSeason(first.Path, job.Root));
            }
            else
            {
                job.DetectedTitle = NameParser.CleanTitle(job.DisplayName);
                job.Keyword = job.DetectedTitle;
                job.Season = 1;
            }
            if (job.Season < 1) job.Season = 1;
            job.Title = job.DetectedTitle;
        }

        private void RefreshFolderList()
        {
            _loading = true;
            _folderList.BeginUpdate();
            _folderList.Items.Clear();
            foreach (FolderJob job in _jobs) _folderList.Items.Add(job);
            if (_current != null && _jobs.Contains(_current))
                _folderList.SelectedItem = _current;
            _folderList.EndUpdate();
            _lblFolderCount.Text = _jobs.Count.ToString();
            _emptyHint.Visible = _jobs.Count == 0;
            _loading = false;
            UpdateActionState();
        }

        private void SelectJob(FolderJob job, bool autoSearch)
        {
            _current = job;
            _loading = true;
            if (_folderList.SelectedItem != job)
            {
                _folderList.SelectedItem = job;
            }

            if (job != null)
            {
                _txtKeyword.Text = job.Keyword;
                _txtTitle.Text = job.EffectiveTitle;
                _numSeason.Value = Clamp(job.Season, 1, 99);
                _numOffset.Value = Clamp(job.Offset, -999, 999);
                _cboResults.Items.Clear();
                foreach (SubjectInfo s in job.Candidates) _cboResults.Items.Add(s.Display);
                if (job.Subject != null)
                {
                    int idx = job.Candidates.FindIndex(x => x.Source == job.Subject.Source && x.Id == job.Subject.Id);
                    if (idx >= 0) _cboResults.SelectedIndex = idx;
                }
                else if (job.Candidates.Count > 0)
                {
                    _cboResults.SelectedIndex = 0;
                }
                _txtTitle.Enabled = true;
                _numSeason.Enabled = true;
                _numOffset.Enabled = true;
            }
            else
            {
                _txtKeyword.Text = "";
                _txtTitle.Text = "";
                _cboResults.Items.Clear();
                _lblMatch.Text = "";
                _txtTitle.Enabled = false;
                _numSeason.Enabled = false;
                _numOffset.Enabled = false;
            }
            _loading = false;

            UpdateMatchStatus();
            RefreshPreview();

            if (job != null && autoSearch && _config.Source != "local" &&
                job.Candidates.Count == 0 && !job.MetadataLoading && job.Keyword.Length > 0)
            {
                var ignored = SearchAsync(job, true);
            }

            Invalidate(true);
        }

        private void OnFolderSelectionChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            SelectJob(_folderList.SelectedItem as FolderJob, true);
        }

        private void StartSearch()
        {
            if (_current == null) return;
            string keyword = _txtKeyword.Text.Trim();
            if (keyword.Length == 0)
            {
                MessageBox.Show(this, "请输入要搜索的番剧名称。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _current.Keyword = keyword;
            _current.Candidates.Clear();
            _current.Subject = null;
            var ignored = SearchAsync(_current, false);
        }

        private async Task SearchAsync(FolderJob job, bool auto)
        {
            if (job == null || _config.Source == "local") return;
            string keyword = (job.Keyword ?? "").Trim();
            if (keyword.Length == 0) return;

            int token = ++_searchToken;
            job.SearchToken = token;
            job.MetadataLoading = true;
            job.MatchState = "搜索中";
            UpdateMatchStatus();
            RefreshFolderList();
            BeginBusy("正在 " + SourceName() + " 搜索「" + keyword + "」…");

            try
            {
                Config snapshot = CloneConfig();
                string source = _config.Source;
                List<SubjectInfo> results = await Task.Run(delegate
                {
                    return MetadataClient.Search(source, keyword, snapshot);
                });

                if (token != _searchToken || !_jobs.Contains(job)) return;

                job.Candidates = results ?? new List<SubjectInfo>();
                if (job.Candidates.Count == 0)
                {
                    job.MatchState = "未找到";
                    Log("没有搜到「" + keyword + "」，可以修改关键词，或使用本地标题直接命名。", Theme.Orange);
                    if (_current == job)
                    {
                        _loading = true;
                        _cboResults.Items.Clear();
                        _loading = false;
                    }
                    return;
                }

                SubjectInfo chosen = null;
                double bestScore = -1;
                foreach (SubjectInfo s in job.Candidates)
                {
                    double score = Math.Max(NameParser.Similarity(keyword, s.NameCn), NameParser.Similarity(keyword, s.Name));
                    if (score > bestScore) { bestScore = score; chosen = s; }
                }
                if (chosen == null) chosen = job.Candidates[0];

                if (_current == job)
                {
                    _loading = true;
                    _cboResults.Items.Clear();
                    foreach (SubjectInfo s in job.Candidates) _cboResults.Items.Add(s.Display);
                    int idx = job.Candidates.FindIndex(x => x.Id == chosen.Id && x.Source == chosen.Source);
                    if (idx >= 0) _cboResults.SelectedIndex = idx;
                    _loading = false;
                }

                ApplySubject(job, chosen, auto, bestScore >= 0.35);
                if (bestScore < 0.35)
                    Log("匹配度较低，请确认标题是否正确：" + chosen.Display, Theme.Orange);
            }
            catch (Exception ex)
            {
                if (token != _searchToken) return;
                job.MatchState = "匹配失败";
                Log(ex.Message, Theme.Red);
            }
            finally
            {
                job.MetadataLoading = false;
                if (token == _searchToken)
                {
                    if (_current == job) UpdateMatchStatus();
                    RefreshFolderList();
                }
                EndBusy();
                if (token == _searchToken) SetIdleStatus();
            }
        }

        private void ApplySubject(FolderJob job, SubjectInfo subject, bool auto, bool confident)
        {
            if (job == null || subject == null) return;
            job.Subject = subject;
            job.MatchState = confident ? "已匹配" : "待确认";

            // 用户主动搜索/选择时，标题始终跟随所选条目；自动匹配则保留手动改过的标题。
            if (!job.TitleCustomized || !auto)
            {
                job.Title = subject.PreferredTitle(_config.Language == "cn");
                job.TitleCustomized = false;
            }
            if (subject.Year > 0) job.Year = subject.Year;

            if (_current == job)
            {
                _loading = true;
                _txtTitle.Text = job.EffectiveTitle;
                _loading = false;
                UpdateMatchStatus();
            }
            RefreshFolderList();
            FetchEpisodes(job);
            if (_current == job) RefreshPreview();
            if (auto) Log("匹配到：" + subject.Display + "（可在预览中确认）");
        }

        private async void FetchEpisodes(FolderJob job)
        {
            if (job == null || job.Subject == null) return;
            SubjectInfo subject = job.Subject;
            int season = job.Season;
            try
            {
                Config snapshot = CloneConfig();
                Dictionary<int, string> titles = await Task.Run(delegate
                {
                    return MetadataClient.GetEpisodeTitles(subject, season, snapshot);
                });
                if (job.Subject != subject) return;
                job.EpisodeTitles = titles ?? new Dictionary<int, string>();
                if (_current == job && _cboTemplate.Text.IndexOf("{ep_title}", StringComparison.OrdinalIgnoreCase) >= 0)
                    RefreshPreview();
            }
            catch (Exception ex)
            {
                Log("获取单集标题失败：" + ex.Message, Theme.Orange);
            }
        }

        private void OnSourceChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            _config.Source = _cboSource.SelectedIndex == 1 ? "tmdb" : (_cboSource.SelectedIndex == 2 ? "local" : "bangumi");
            SaveConfig();
            if (_current == null) return;

            _current.Candidates.Clear();
            _current.Subject = null;
            _current.MatchState = _config.Source == "local" ? "本地命名" : "待匹配";
            if (_config.Source == "local" && !_current.TitleCustomized)
                _current.Title = _current.DetectedTitle;
            _loading = true;
            _cboResults.Items.Clear();
            if (_config.Source == "local") _txtTitle.Text = _current.EffectiveTitle;
            _loading = false;
            UpdateMatchStatus();
            RefreshPreview();
            RefreshFolderList();

            if (_config.Source != "local" && _current.Keyword.Length > 0)
            {
                var ignored = SearchAsync(_current, true);
            }
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            _config.Language = _cboLanguage.SelectedIndex == 1 ? "orig" : "cn";
            foreach (FolderJob job in _jobs)
            {
                if (job.Subject != null && !job.TitleCustomized)
                    job.Title = job.Subject.PreferredTitle(_config.Language == "cn");
            }
            if (_current != null)
            {
                _loading = true;
                _txtTitle.Text = _current.EffectiveTitle;
                _loading = false;
            }
            RefreshPreview();
            RefreshFolderList();
            SaveConfig();
        }

        private void OnResultChanged(object sender, EventArgs e)
        {
            if (_loading || _current == null) return;
            int idx = _cboResults.SelectedIndex;
            if (idx < 0 || idx >= _current.Candidates.Count) return;
            SubjectInfo subject = _current.Candidates[idx];
            if (_current.Subject != null && _current.Subject.Source == subject.Source && _current.Subject.Id == subject.Id) return;
            ApplySubject(_current, subject, false, true);
        }

        private void OnTitleChanged(object sender, EventArgs e)
        {
            if (_loading || _current == null) return;
            _current.Title = _txtTitle.Text.Trim();
            _current.TitleCustomized = true;
            SchedulePreview();
            _folderList.Invalidate();
        }

        private void OnSeasonChanged(object sender, EventArgs e)
        {
            if (_loading || _current == null) return;
            int s = (int)_numSeason.Value;
            if (s == _current.Season) return;
            _current.Season = s;
            _current.EpisodeTitles.Clear();
            if (_current.Subject != null) FetchEpisodes(_current);
            SchedulePreview();
        }

        private void OnOffsetChanged(object sender, EventArgs e)
        {
            if (_loading || _current == null) return;
            _current.Offset = (int)_numOffset.Value;
            SchedulePreview();
        }

        private void OnAlignClick(object sender, EventArgs e)
        {
            if (_current == null) return;
            var eps = _current.Files
                .Where(f => f.Kind == MediaKind.Video && f.Parsed.HasEpisode && !f.Parsed.IsSpecial)
                .Select(f => f.Parsed.Episode).ToList();
            if (eps.Count == 0)
            {
                Log("没有识别到集数，无法自动对齐。", Theme.Orange);
                return;
            }
            int min = eps.Min();
            int offset = min > 1 ? 1 - min : 0;
            _current.Offset = offset;
            _loading = true;
            _numOffset.Value = Clamp(offset, -999, 999);
            _loading = false;
            SchedulePreview();
            Log("检测到最小集数 " + min + "，已设置偏移 " + offset + "，使第一集对应 01。");
        }

        private void OnTemplateChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            string t = _cboTemplate.Text.Trim();
            if (t.Length == 0) return;
            _config.Template = t;
            SchedulePreview();
        }

        private void OnOptionChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            _config.RenameSubs = _chkSubs.Checked;
            _config.RenameSidecars = _chkSidecars.Checked;
            _config.IncludeSpecials = _chkSpecials.Checked;
            _config.SkipExtras = _chkSkipExtras.Checked;
            SchedulePreview();
            SaveConfig();
        }

        private async void OnRecursiveChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            _config.Recursive = _chkRecursive.Checked;
            SaveConfig();
            await RescanAllAsync(true);
        }

        private async Task RescanAllAsync(bool notify)
        {
            if (_jobs.Count == 0) return;
            BeginBusy("正在重新扫描…");
            try
            {
                foreach (FolderJob job in _jobs)
                {
                    FolderJob j = job;
                    _lblStatus.Text = "正在扫描 " + j.DisplayName + " …";
                    j.Files = await Task.Run(delegate { return Scanner.Scan(j.Root, j.Recursive); });
                }
                RefreshPreview();
                RefreshFolderList();
                if (notify) Log("已按新的扫描设置刷新目录。");
            }
            catch (Exception ex)
            {
                Log("重新扫描失败：" + ex.Message, Theme.Red);
            }
            finally
            {
                EndBusy();
                SetIdleStatus();
            }
        }

        private PlanOptions CurrentOptions()
        {
            var opt = new PlanOptions();
            opt.Template = string.IsNullOrWhiteSpace(_cboTemplate.Text) ? _config.Template : _cboTemplate.Text.Trim();
            opt.RenameSubs = _config.RenameSubs;
            opt.RenameSidecars = _config.RenameSidecars;
            opt.IncludeSpecials = _config.IncludeSpecials;
            opt.SkipExtras = _config.SkipExtras;
            opt.Language = _config.Language;
            return opt;
        }

        private void RefreshPreview()
        {
            if (_current == null)
            {
                _grid.DataSource = null;
                _lblSummary.Text = "";
                _lblExample.Text = "";
                return;
            }

            Dictionary<string, bool> included = CaptureIncluded();
            Planner.Build(_current, CurrentOptions());
            ApplyIncluded(_current, included);
            _grid.DataSource = new BindingList<PlanItem>(_current.Plan);
            _grid.ClearSelection();
            UpdateSummary();
            UpdateExample();
            _folderList.Invalidate();
            UpdateActionState();
        }

        private Dictionary<string, bool> CaptureIncluded()
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (FolderJob job in _jobs)
            {
                foreach (PlanItem p in job.Plan)
                {
                    if (p.Status == "就绪") map[PathUtil.Full(p.Source)] = p.Included;
                }
            }
            return map;
        }

        private static void ApplyIncluded(FolderJob job, Dictionary<string, bool> map)
        {
            if (map == null || map.Count == 0) return;
            foreach (PlanItem p in job.Plan)
            {
                if (p.Status != "就绪") continue;
                bool value;
                if (map.TryGetValue(PathUtil.Full(p.Source), out value)) p.Included = value;
            }
        }

        private void UpdateSummary()
        {
            if (_current == null)
            {
                _lblSummary.Text = "";
                return;
            }
            int total = _current.Plan.Count;
            int videos = _current.Plan.Count(p => p.Kind == MediaKind.Video);
            int subs = _current.Plan.Count(p => p.Kind == MediaKind.Subtitle);
            int ready = _current.Plan.Count(p => p.Included && p.Status == "就绪");
            int conflict = _current.Plan.Count(p => p.Status == "冲突");
            _lblSummary.Text = string.Format("共 {0} · 视频 {1} · 字幕 {2} · 待执行 {3} · 冲突 {4}",
                total, videos, subs, ready, conflict);
        }

        private void UpdateExample()
        {
            if (_current == null) { _lblExample.Text = ""; return; }
            PlanItem first = _current.Plan.FirstOrDefault(p => p.Included && p.Status == "就绪");
            if (first != null)
            {
                _lblExample.Text = "示例：" + first.NewName;
                return;
            }
            var tokens = new Dictionary<string, object>();
            tokens["title"] = _current.EffectiveTitle;
            tokens["season"] = Math.Max(1, _current.Season);
            tokens["ep"] = 1;
            tokens["ep_title"] = "第一话";
            tokens["year"] = _current.Year > 0 ? _current.Year.ToString() : "";
            tokens["resolution"] = "1080p";
            tokens["group"] = "";
            tokens["title_cn"] = _current.EffectiveTitle;
            tokens["title_orig"] = _current.EffectiveTitle;
            string sample = NameParser.ApplyTemplate(CurrentOptions().Template, tokens);
            _lblExample.Text = "示例：" + NameParser.SanitizeName(sample) + ".mkv";
        }

        private void UpdateMatchStatus()
        {
            if (_current == null) { _lblMatch.Text = ""; return; }
            string text = _current.MatchState;
            if (_current.Subject != null) text += " · " + _current.Subject.ShortDisplay;
            _lblMatch.Text = text;
            if (_current.MatchState == "已匹配") _lblMatch.ForeColor = Theme.Green;
            else if (_current.MatchState == "待确认" || _current.MatchState == "未找到") _lblMatch.ForeColor = Theme.Orange;
            else if (_current.MatchState == "匹配失败") _lblMatch.ForeColor = Theme.Red;
            else _lblMatch.ForeColor = Theme.Muted;
        }

        private void UpdateActionState()
        {
            bool on = !_busy;
            if (_btnAdd != null) _btnAdd.Enabled = on;
            if (_btnRemove != null) _btnRemove.Enabled = on && _current != null;
            if (_btnClear != null) _btnClear.Enabled = on && _jobs.Count > 0;
            if (_btnSearch != null) _btnSearch.Enabled = on && _current != null;
            if (_btnPreview != null) _btnPreview.Enabled = on && _current != null;
            if (_btnExecute != null) _btnExecute.Enabled = on && _jobs.Count > 0 && !_restricted;
            if (_btnUndo != null) _btnUndo.Enabled = on;
            if (_btnAlign != null) _btnAlign.Enabled = on && _current != null;
            if (_btnSelectAll != null) _btnSelectAll.Enabled = on;
            if (_btnSelectNone != null) _btnSelectNone.Enabled = on;
            if (_chkRecursive != null) _chkRecursive.Enabled = on;
            if (_cboTemplate != null) _cboTemplate.Enabled = on;
            if (_cboSource != null) _cboSource.Enabled = on;
            if (_txtKeyword != null) _txtKeyword.Enabled = on;
            UseWaitCursor = _busy;
        }

        private void BeginBusy(string message)
        {
            _busyDepth++;
            _busy = true;
            if (!string.IsNullOrEmpty(message)) _lblStatus.Text = message;
            UpdateActionState();
        }

        private void SetIdleStatus()
        {
            if (_lblStatus == null) return;
            _lblStatus.ForeColor = _restricted ? Theme.Orange : Theme.Muted;
            string prefix = _restricted ? "受限模式：请用桌面「启动番剧重命名.bat」启动 · " : "就绪 · ";
            if (_jobs.Count == 0)
            {
                _lblStatus.Text = prefix + "选择文件夹后自动识别";
                return;
            }
            int ready = _jobs.SelectMany(delegate(FolderJob j) { return j.Plan; })
                             .Count(delegate(PlanItem p) { return p.Included && p.Status == "就绪"; });
            _lblStatus.Text = prefix + _jobs.Count + " 个目录 · " + ready + " 个文件可重命名";
        }

        private void EndBusy()
        {
            if (_busyDepth > 0) _busyDepth--;
            if (_busyDepth == 0)
            {
                _busy = false;
                UpdateActionState();
            }
        }

        private void SchedulePreview()
        {
            if (_loading) return;
            _debounce.Stop();
            _debounce.Start();
        }

        private void OnAddFoldersDummy() { }

        private async void OnExecuteClick(object sender, EventArgs e)
        {
            if (_restricted)
            {
                MessageBox.Show(this,
                    "当前窗口是受限模式，无法改名。\n\n请关闭本窗口，到 Windows 桌面双击「启动番剧重命名.bat」重新打开。",
                    "受限模式", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_busy || _jobs.Count == 0) return;

            PlanOptions opt = CurrentOptions();
            Dictionary<string, bool> included = CaptureIncluded();
            foreach (FolderJob job in _jobs)
            {
                Planner.Build(job, opt);
                ApplyIncluded(job, included);
            }
            var ready = _jobs.SelectMany(j => j.Plan).Where(p => p.Included && p.Status == "就绪").ToList();
            if (ready.Count == 0)
            {
                MessageBox.Show(this, "没有可重命名的文件。\n请检查匹配结果和预览中的勾选状态。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int videos = ready.Count(p => p.Kind == MediaKind.Video);
            int others = ready.Count - videos;
            string msg = "将重命名 " + ready.Count + " 个文件（视频 " + videos + "，字幕 / 附属 " + others + "）。\n\n" +
                         "改名前的名称会记录在撤销历史中，执行后可使用“撤销上次”恢复。\n\n是否开始？";
            if (MessageBox.Show(this, msg, "确认重命名", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            BeginBusy("正在重命名…");
            _progress.Maximum = 100;
            _progress.Value = 0;
            var progress = new Progress<ProgressInfo>(delegate(ProgressInfo p)
            {
                _progress.Value = p.Percent;
                _lblStatus.Text = p.Message;
            });

            RenameResult result;
            try
            {
                result = await Task.Run(delegate { return Renamer.Execute(ready, _dataDir, progress); });
            }
            catch (Exception ex)
            {
                Log("重命名失败：" + ex.Message, Theme.Red);
                MessageBox.Show(this, "重命名失败：\n" + ex.Message, "出错了", MessageBoxButtons.OK, MessageBoxIcon.Error);
                EndBusy();
                return;
            }

            foreach (FolderJob job in _jobs)
            {
                FolderJob j = job;
                j.Files = await Task.Run(delegate { return Scanner.Scan(j.Root, j.Recursive); });
            }

            _progress.Value = 100;
            EndBusy();
            RefreshPreview();
            RefreshFolderList();
            _lblStatus.Text = "重命名完成：成功 " + result.Renamed + "，失败 " + result.Failed + "。";

            foreach (string err in result.Errors) Log(err, Theme.Orange);
            Log("重命名完成：成功 " + result.Renamed + "，跳过 " + result.Skipped + "，失败 " + result.Failed + "。",
                result.Failed > 0 ? Theme.Orange : Theme.Green);

            var sb = new StringBuilder();
            sb.AppendLine("成功重命名 " + result.Renamed + " 个文件。");
            if (result.Skipped > 0) sb.AppendLine("跳过 " + result.Skipped + " 个（目标已存在或文件缺失）。");
            if (result.Failed > 0) sb.AppendLine("失败 " + result.Failed + " 个。");
            if (!string.IsNullOrEmpty(result.HistoryPath)) sb.AppendLine("已保存撤销记录，可点击“撤销上次”还原。");

            if (result.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("详情（前 6 条）：");
                int shown = Math.Min(6, result.Errors.Count);
                for (int i = 0; i < shown; i++) sb.AppendLine("· " + result.Errors[i]);
                if (result.Errors.Count > shown) sb.AppendLine("…其余见运行日志。");
            }

            bool denied = result.Errors.Any(delegate(string err)
            {
                return err.IndexOf("访问被拒绝", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       err.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       err.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0;
            });
            bool inUse = result.Errors.Any(delegate(string err)
            {
                return err.IndexOf("正在使用", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       err.IndexOf("being used", StringComparison.OrdinalIgnoreCase) >= 0;
            });
            if (denied)
                sb.AppendLine("\n提示：目标文件夹拒绝写入。请关闭占用文件的下载/播放/杀毒程序；如果程序运行在受限沙箱中，请把 EXE 复制到普通桌面后直接双击运行。");
            else if (inUse)
                sb.AppendLine("\n提示：文件正被其他程序占用，请先停止做种或播放后重试。");

            MessageBox.Show(this, sb.ToString(), "完成", MessageBoxButtons.OK,
                result.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private async void OnUndoClick(object sender, EventArgs e)
        {
            if (_busy) return;
            if (MessageBox.Show(this, "撤销最近一次重命名？\n文件会恢复到原来的名称。", "确认撤销",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            BeginBusy("正在撤销…");
            _progress.Maximum = 100;
            _progress.Value = 0;
            var progress = new Progress<ProgressInfo>(delegate(ProgressInfo p)
            {
                _progress.Value = p.Percent;
                _lblStatus.Text = p.Message;
            });

            UndoResult result;
            try
            {
                result = await Task.Run(delegate { return Renamer.UndoLast(_dataDir, progress); });
            }
            catch (Exception ex)
            {
                Log("撤销失败：" + ex.Message, Theme.Red);
                EndBusy();
                return;
            }

            foreach (FolderJob job in _jobs)
            {
                FolderJob j = job;
                j.Files = await Task.Run(delegate { return Scanner.Scan(j.Root, j.Recursive); });
            }

            _progress.Value = 100;
            EndBusy();
            RefreshPreview();
            RefreshFolderList();
            _lblStatus.Text = result.Message;

            foreach (string err in result.Errors) Log(err, Theme.Orange);
            Log(result.Message, result.Restored > 0 ? Theme.Green : Theme.Orange);
            MessageBox.Show(this, result.Message, "撤销", MessageBoxButtons.OK,
                result.Restored > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void SetAllIncluded(bool include)
        {
            if (_current == null) return;
            foreach (PlanItem item in _current.Plan)
            {
                if (item.Status == "就绪") item.Included = include;
            }
            _grid.Refresh();
            UpdateSummary();
            UpdateExample();
        }

        private void Grid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _colCheck) return;
            var item = _grid.Rows[e.RowIndex].DataBoundItem as PlanItem;
            if (item == null || item.Status != "就绪") return;
            item.Included = !item.Included;
            _grid.InvalidateRow(e.RowIndex);
            UpdateSummary();
            UpdateExample();
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _colStatus) return;
            var item = _grid.Rows[e.RowIndex].DataBoundItem as PlanItem;
            if (item == null) return;

            e.PaintBackground(e.CellBounds, true);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color fg, bg;
            StatusColors(item.Status, out fg, out bg);
            var r = new Rectangle(e.CellBounds.X + 6, e.CellBounds.Y + 5, e.CellBounds.Width - 12, e.CellBounds.Height - 10);
            if (r.Width > 4 && r.Height > 4)
            {
                Theme.FillRound(e.Graphics, r, 6, bg);
                TextRenderer.DrawText(e.Graphics, item.Status, Theme.F(8.5f), r, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            e.Handled = true;
        }

        private static void StatusColors(string status, out Color fg, out Color bg)
        {
            switch (status)
            {
                case "就绪":
                    fg = Theme.Green; bg = Theme.GreenSoft; return;
                case "冲突":
                    fg = Theme.Red; bg = Theme.RedSoft; return;
                case "未识别":
                case "未匹配":
                    fg = Theme.Orange; bg = Theme.OrangeSoft; return;
                default:
                    fg = Theme.Faint; bg = Color.FromArgb(243, 245, 249); return;
            }
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var item = _grid.Rows[e.RowIndex].DataBoundItem as PlanItem;
            if (item == null) return;

            if (e.ColumnIndex == _colOld)
            {
                e.CellStyle.ForeColor = Theme.Muted;
            }
            else if (e.ColumnIndex == _colNew)
            {
                if (item.Status == "冲突") e.CellStyle.ForeColor = Theme.Red;
                else if (item.Included) e.CellStyle.ForeColor = Theme.Text;
                else e.CellStyle.ForeColor = Theme.Faint;
            }
            else if (e.ColumnIndex == _colNote)
            {
                e.CellStyle.ForeColor = Theme.Muted;
                e.CellStyle.Font = Theme.F(8.5f);
            }
        }

        private void Grid_CellToolTip(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var item = _grid.Rows[e.RowIndex].DataBoundItem as PlanItem;
            if (item == null) return;
            e.ToolTipText = item.OldName + "\r\n→ " + item.NewName + (string.IsNullOrEmpty(item.Note) ? "" : "\r\n" + item.Note);
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0) OpenSelectedInExplorer();
        }

        private PlanItem SelectedItem()
        {
            if (_grid.CurrentRow == null) return null;
            return _grid.CurrentRow.DataBoundItem as PlanItem;
        }

        private void OpenSelectedInExplorer()
        {
            PlanItem item = SelectedItem();
            if (item == null) return;
            try { Process.Start("explorer.exe", "/select,\"" + item.Source + "\""); }
            catch (Exception ex) { Log("无法打开资源管理器：" + ex.Message, Theme.Orange); }
        }

        private void CopySelectedName()
        {
            PlanItem item = SelectedItem();
            if (item == null) return;
            try { Clipboard.SetText(item.NewName); Log("已复制新文件名：" + item.NewName); }
            catch { }
        }

        private void CopySelectedPath()
        {
            PlanItem item = SelectedItem();
            if (item == null) return;
            try { Clipboard.SetText(item.Target); Log("已复制目标路径：" + item.Target); }
            catch { }
        }

        private void OnToggleLog(object sender, EventArgs e)
        {
            bool visible = _rightTable.RowStyles[3].Height > 10;
            SetLogVisible(!visible);
        }

        private void SetLogVisible(bool visible)
        {
            _rightTable.RowStyles[3].Height = visible ? 96 : 0;
            _btnLog.Text = visible ? "隐藏日志" : "显示日志";
        }

        private void FolderList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var job = _folderList.Items[e.Index] as FolderJob;
            if (job == null) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Rectangle r = e.Bounds;

            using (var b = new SolidBrush(Theme.Card)) g.FillRectangle(b, r);
            if (selected)
            {
                Theme.FillRound(g, new Rectangle(r.X + 2, r.Y + 3, r.Width - 6, r.Height - 7), 8, Theme.AccentSoft);
                using (var b = new SolidBrush(Theme.Accent))
                    g.FillRectangle(b, r.X + 2, r.Y + 13, 3, r.Height - 27);
            }

            Color dot;
            if (job.MatchState == "已匹配") dot = Theme.Green;
            else if (job.MatchState == "待确认" || job.MatchState == "搜索中") dot = Theme.Orange;
            else if (job.MatchState == "匹配失败" || job.MatchState == "未找到") dot = Theme.Red;
            else dot = Theme.Faint;

            using (var b = new SolidBrush(dot)) g.FillEllipse(b, r.Right - 24, r.Y + 13, 7, 7);

            var nameRect = new Rectangle(r.X + 12, r.Y + 8, r.Width - 46, 22);
            TextRenderer.DrawText(g, job.DisplayName, Theme.F(9.75f, FontStyle.Bold), nameRect, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            var subRect = new Rectangle(r.X + 12, r.Y + 30, r.Width - 24, 18);
            TextRenderer.DrawText(g, job.SubText, Theme.F(8.25f), subRect, Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void OnHelp(object sender, EventArgs e)
        {
            using (var dlg = new HelpForm())
                dlg.ShowDialog(this);
        }

        private void OnSettings(object sender, EventArgs e)
        {
            using (var dlg = new SettingsForm(_config, _dataDir))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                dlg.ApplyTo(_config);
                _config.Save(_dataDir);

                _loading = true;
                _cboSource.SelectedIndex = _config.Source == "tmdb" ? 1 : (_config.Source == "local" ? 2 : 0);
                _cboLanguage.SelectedIndex = _config.Language == "orig" ? 1 : 0;
                _loading = false;

                foreach (FolderJob job in _jobs)
                {
                    if (job.Subject != null && !job.TitleCustomized)
                        job.Title = job.Subject.PreferredTitle(_config.Language == "cn");
                }
                if (_current != null)
                {
                    _loading = true;
                    _txtTitle.Text = _current.EffectiveTitle;
                    _loading = false;
                }
                RefreshPreview();
                RefreshFolderList();
                Log("设置已保存。");
            }
        }

        private string SourceName()
        {
            if (_config.Source == "tmdb") return "TMDB";
            if (_config.Source == "local") return "本地";
            return "Bangumi";
        }

        private Config CloneConfig()
        {
            var c = new Config();
            c.Template = _config.Template;
            c.Source = _config.Source;
            c.Language = _config.Language;
            c.TmdbKey = _config.TmdbKey;
            c.Proxy = _config.Proxy;
            c.Recursive = _config.Recursive;
            c.RenameSubs = _config.RenameSubs;
            c.RenameSidecars = _config.RenameSidecars;
            c.IncludeSpecials = _config.IncludeSpecials;
            c.SkipExtras = _config.SkipExtras;
            return c;
        }

        private void SaveConfig()
        {
            if (_cboTemplate.Text.Trim().Length > 0) _config.Template = _cboTemplate.Text.Trim();
            _config.RememberTemplate(_config.Template);
            _config.Save(_dataDir);
        }

        private void Log(string message)
        {
            Log(message, Color.FromArgb(203, 213, 225));
        }

        private void Log(string message, Color color)
        {
            if (_log == null) return;
            if (color == Theme.Red && _rightTable != null) SetLogVisible(true);
            try
            {
                if (_log.TextLength > 60000) _log.Clear();
                _log.SelectionStart = _log.TextLength;
                _log.SelectionLength = 0;
                _log.SelectionColor = color;
                _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
                _log.SelectionColor = _log.ForeColor;
                _log.ScrollToCaret();
            }
            catch { }

            try
            {
                File.AppendAllText(Path.Combine(_dataDir, "app.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch { }
        }

        // ---------------- demo rendering ----------------

        public void RenderTo(string path, bool demo)
        {
            _rendering = true;
            if (demo || _jobs.Count == 0) LoadDemo();
            Show();
            Application.DoEvents();
            if (Environment.GetEnvironmentVariable("ANIME_RENAMER_DUMP") == "1")
            {
                try { DumpLayout(Path.Combine(Path.GetDirectoryName(path), "layout.txt")); }
                catch { }
            }
            using (var bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            Close();
        }

        private void DumpLayout(string file)
        {
            var sb = new StringBuilder();
            DumpControl(this, sb, 0);
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        }

        private static void DumpControl(Control c, StringBuilder sb, int depth)
        {
            sb.AppendLine(new string(' ', depth * 2) + c.GetType().Name + " [" + (c.Text ?? "").Replace("\r", " ").Replace("\n", " ") + "] " +
                          "b=" + c.Bounds + " client=" + c.ClientSize + " fontH=" + c.Font.Height + " visible=" + c.Visible);
            foreach (Control child in c.Controls) DumpControl(child, sb, depth + 1);
        }

        private void LoadDemo()
        {
            string root = @"D:\Anime\[VCB-Studio] Sousou no Frieren [Ma10p_1080p]";
            var job = new FolderJob();
            job.Root = root;
            job.Recursive = true;
            job.DetectedTitle = "Sousou no Frieren";
            job.Keyword = "葬送的芙莉莲";
            job.Title = "葬送的芙莉莲";
            job.Season = 1;
            job.Year = 2023;
            job.MatchState = "已匹配";
            job.Subject = new SubjectInfo
            {
                Source = "bangumi",
                Id = "400602",
                Name = "Sousou no Frieren",
                NameCn = "葬送的芙莉莲",
                Year = 2023,
                Eps = 28,
                Score = 8.6
            };
            job.Candidates.Add(job.Subject);
            job.Candidates.Add(new SubjectInfo { Source = "bangumi", Id = "425998", Name = "Sousou no Frieren", NameCn = "葬送的芙莉莲 第二季", Year = 2026, Eps = 12, Score = 7.9 });
            job.EpisodeTitles[1] = "冒险的结束";
            job.EpisodeTitles[2] = "那一定很有趣";
            job.EpisodeTitles[3] = "苍月草";
            job.EpisodeTitles[4] = "小小的幸福";

            string[] files = {
                "[VCB-Studio] Sousou no Frieren [01][Ma10p_1080p][x265_flac].mkv",
                "[VCB-Studio] Sousou no Frieren [02][Ma10p_1080p][x265_flac].mkv",
                "[VCB-Studio] Sousou no Frieren [03][Ma10p_1080p][x265_flac].mkv",
                "[VCB-Studio] Sousou no Frieren [04][Ma10p_1080p][x265_flac].mkv",
                "[VCB-Studio] Sousou no Frieren [05][Ma10p_1080p][x265_flac].mkv",
                "[VCB-Studio] Sousou no Frieren [01][Ma10p_1080p][x265_flac].chs.ass",
                "[VCB-Studio] Sousou no Frieren [02][Ma10p_1080p][x265_flac].chs.ass",
                "[VCB-Studio] Sousou no Frieren [03][Ma10p_1080p][x265_flac].cht.ass",
                "[VCB-Studio] Sousou no Frieren [04][Ma10p_1080p][x265_flac].cht.ass",
                "[VCB-Studio] Sousou no Frieren [05][Ma10p_1080p][x265_flac].cht.ass",
                "[VCB-Studio] Sousou no Frieren [NCOP01][Ma10p_1080p][x265_flac].mkv"
            };
            foreach (string name in files)
            {
                string ext = Path.GetExtension(name);
                MediaKind kind;
                NameParser.TryKind(ext, out kind);
                job.Files.Add(new FileItem
                {
                    Path = Path.Combine(root, name),
                    Name = name,
                    Size = 1024 * 1024,
                    Kind = kind,
                    Parsed = NameParser.Parse(name)
                });
            }
            _jobs.Add(job);
            _current = job;

            var job2 = new FolderJob();
            job2.Root = @"E:\Anime\药师少女的独语";
            job2.DetectedTitle = "药师少女的独语";
            job2.Keyword = "药师少女的独语";
            job2.Title = "药师少女的独语";
            job2.Season = 1;
            job2.MatchState = "待匹配";
            _jobs.Add(job2);

            RefreshFolderList();
            SelectJob(job, false);
            Log("（演示数据）已添加 2 个番剧目录，匹配到 Bangumi 条目。", Color.FromArgb(134, 239, 172));
        }
    }
}
