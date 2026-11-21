using MouseClicker.Domain;
using MouseClicker.Actions;
using MouseClicker.Engine;
using MouseClicker.Platform;
using MouseClicker.Storage;

namespace MouseClicker.UI;

public sealed class MainForm : Form
{
    private static readonly Color CanvasColor = Color.FromArgb(222, 241, 227);
    private static readonly Color SurfaceColor = Color.FromArgb(247, 253, 248);
    private static readonly Color InkColor = Color.FromArgb(26, 52, 36);
    private static readonly Color MutedColor = Color.FromArgb(66, 94, 76);
    private static readonly Color BorderColor = Color.FromArgb(164, 195, 173);
    private static readonly Color HeaderColor = Color.FromArgb(215, 237, 221);
    private static readonly Color HeaderTextColor = Color.FromArgb(29, 92, 58);
    private static readonly Color PrimaryColor = Color.FromArgb(37, 117, 71);
    private static readonly Color AccentColor = Color.FromArgb(28, 104, 59);
    private static readonly Color DangerColor = Color.FromArgb(171, 53, 46);
    private static readonly Color WarningColor = Color.FromArgb(135, 86, 20);
    private static readonly Color NavigationColor = Color.FromArgb(25, 82, 52);
    private static readonly Color SelectedNavigationColor = Color.FromArgb(48, 127, 81);
    private static readonly Color NavigationTextColor = Color.FromArgb(244, 255, 246);
    private static readonly Color DisabledSurfaceColor = Color.FromArgb(230, 241, 233);
    private static readonly Color ActionBandColor = Color.FromArgb(255, 241, 189);
    private static readonly Font SectionFont = new("Microsoft YaHei UI", 10f, FontStyle.Bold);

    private readonly ClickEngine engine = new(new WindowsInputDriver());
    private readonly CheckBox backgroundMode = new() { Text = "后台窗口", AutoSize = true };
    private readonly Label backgroundTargetLabel = new() { Text = "尚未选定后台窗口", AutoSize = true, MaximumSize = new Size(350, 0) };
    private BackgroundInputDriver.TargetIdentity? backgroundTarget;
    private CancellationTokenSource? scheduledStart;
    private bool scheduleDialogOpen;
    private string? scheduledDescription;
    private readonly PlaybackEngine playback = new(new WindowsInputDriver());
    private readonly MacroEngine macro = new(new WindowsInputDriver());
    private readonly SettingsStore store;
    private ClickSettings? savedSettings, autoSaveBaseline;
    private readonly RecordingStore recordingStore = new();
    private readonly MacroStore macroStore = new();
    private readonly Logger logger = Logger.Instance;

    private readonly ComboBox button = Combo("左键", "中键", "右键");
    private readonly ComboBox action = Combo("单击", "按下（保持）", "松开（单次）");
    private readonly ComboBox position = Combo("跟随当前光标", "固定屏幕坐标", "多点循环");
    private readonly ComboBox stopMode = Combo("无限运行", "指定次数", "指定时长");
    private readonly ComboBox trigger = Combo("热键切换（按一下开/关）", "长按运行（按住执行，松开停）");
    private readonly Label hotkeyDraftLabel = new() { AutoSize = true, MinimumSize = new Size(174, 32), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label hotkeyDraftState = new() { AutoSize = true, MinimumSize = new Size(0, 36), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button editHotkey = new DarkButton { Text = "修改", AutoSize = true };
    private readonly NumericUpDown x = Number(-100000, 100000, 0);
    private readonly NumericUpDown y = Number(-100000, 100000, 0);
    private readonly NumericUpDown intervalMin = Number(1, 10000, 100);
    private readonly NumericUpDown intervalMax = Number(1, 10000, 100);
    private readonly NumericUpDown count = Number(0, 1000000, 10);
    private readonly NumericUpDown duration = Number(1000, 3600000, 10000);
    private readonly NumericUpDown radius = Number(0, 500, 0);
    private readonly ListBox pointList = new() { Height = 90, IntegralHeight = false };
    private readonly ComboBox pointOrder = Combo("顺序循环", "随机循环");
    private readonly Button pickPoint = new DarkButton { Text = "准星取点", AutoSize = true };
    private readonly Button testOnce = new DarkButton { Text = "测试一次", AutoSize = true };
    private readonly Button addPoint = new DarkButton { Text = "添加当前光标", AutoSize = true };
    private readonly Button removePoint = new DarkButton { Text = "删除选中", AutoSize = true };
    private readonly Button clearPoints = new DarkButton { Text = "清空", AutoSize = true };
    private readonly Button start = new DarkButton { Text = "开始", AutoSize = true };
    private readonly Button applyHotkey = new DarkButton { Text = "应用热键", AutoSize = true };
    private readonly Button openCps = new DarkButton { Text = "CPS 测速", AutoSize = true };
    private readonly Button save = new DarkButton { Text = "保存设置", AutoSize = true };
    private readonly Button recordButton = new DarkButton { Text = "开始录制", AutoSize = true };
    private readonly Button loadRecording = new DarkButton { Text = "打开录制", AutoSize = true };
    private readonly Button playButton = new DarkButton { Text = "开始回放", AutoSize = true };
    private readonly Button playPause = new DarkButton { Text = "暂停", AutoSize = true };
    private readonly Button optimizeSave = new DarkButton { Text = "优化另存", AutoSize = true };
    private readonly Button editEvents = new DarkButton { Text = "编辑等待", AutoSize = true };
    private readonly Button libraryButton = new DarkButton { Text = "录制库", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "开机自启", AutoSize = true };
    private RecordingLibrary? library;
    private readonly Dictionary<int,string> recordingHotkeys=[];
    // Opt-in trajectory thinning for "优化另存": off by default (de-dup only), so the
    // already-verified conservative path is unchanged unless the user ticks it.
    private readonly CheckBox thinMoves = new ReadableDisabledCheckBox { Text = "轨迹抽稀(保留拐点/拖拽端点)", AutoSize = true, Checked = false };
    private readonly Button editMacro = new DarkButton { Text = "编辑/新建宏", AutoSize = true };
    private readonly Button loadMacro = new DarkButton { Text = "打开宏", AutoSize = true };
    private readonly Button runMacro = new DarkButton { Text = "开始宏", AutoSize = true };
    private readonly ComboBox playSpeed = Combo("0.5×", "1×", "2×");
    private readonly ComboBox playLoops = Combo("1 轮", "2 轮", "3 轮", "5 轮", "10 轮", "无限循环");
    private readonly Label status = new() { Name = "globalStatus", AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label cursor = new() { AutoSize = true };
    private readonly Label estimate = new() { AutoSize = true, MaximumSize = new Size(380, 0) };
    private readonly Label hotkeyStatus = new() { AutoSize = true, MaximumSize = new Size(430, 0) };
    private readonly Label recordInfo = new() { AutoSize = true, MaximumSize = new Size(380, 0) };
    private readonly Label macroInfo = new() { AutoSize = true, MaximumSize = new Size(380, 0) };
    private readonly ListBox logList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Button stopCurrent = new DarkButton { Name = "stopCurrentTask", Text = "停止当前任务", Enabled = false, Dock = DockStyle.Fill };
    private readonly List<PointSpec> points = [];
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 250 };
    private (RunSnapshot Click, PlaybackSnapshot Playback, MacroEngine.MacroSnapshot Macro, bool Busy, int Recorded)? lastPolledState;
    private readonly Label clickHotkey = new() { AutoSize = true, Margin = new Padding(12, 12, 0, 0) };
    private readonly NotifyIcon tray;
    private GlobalHotkeys? hotkeys;
    private KeyboardHook? triggerHook;
    private InputRecorder? recorder;
    private RecordingDocument? loadedRecording;
    private string? loadedRecordingPath;
    private MacroDocument? loadedMacro;
    private string? loadedMacroPath;
    private CpsMeterForm? cpsWindow;
    private bool emergencyReady, toggleReady, exiting, canClose, prevBusy;
    private (bool Clicking, bool Recording, bool Playing, bool Macro, bool HasRecording, bool HasMacro)? lastActionStyle;
    private FlowLayoutPanel? countRow, durationRow, advancedDetails, editDetails;
    private Control? fixedCard, pointsCard, playbackCard;
    private Button? advancedToggle, editToggle, macroDetailsToggle, recordPathToggle;
    private bool advancedExpanded, editExpanded, macroDetailsExpanded, recordPathExpanded, loadingSettings;
    private decimal previousInterval = 100;
    // Applied hotkey snapshot the running trigger actually uses (never live control values).
    private int appliedVk = 0x77;
    private bool appliedCtrl, appliedAlt, appliedShift;
    private bool appliedToggle = true;
    private int draftVk = 0x77;
    private bool draftCtrl, draftAlt, draftShift, capturingHotkey;
    private string? hotkeyError;

    private sealed record PageView(Panel Page, Panel Content);

    internal static class NativeTheme
    {
        private const int DwmwaUseImmersiveDarkMode = 20;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, uint valueSize);

        public static void ApplyDarkTitleBar(IntPtr hwnd)
        {
            int enabled = 1;
            int caption = ColorTranslator.ToWin32(NavigationColor);
            int border = ColorTranslator.ToWin32(BorderColor);
            int text = ColorTranslator.ToWin32(NavigationTextColor);
            try
            {
                _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
                _ = DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
                _ = DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));  // DWMWA_BORDER_COLOR
                _ = DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));    // DWMWA_TEXT_COLOR
            }
            catch (System.DllNotFoundException) { }
            catch (System.EntryPointNotFoundException) { }
        }
    }

    private sealed class ReadableDisabledCheckBox : CheckBox
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Enabled) { base.OnPaint(e); return; }
            e.Graphics.Clear(BackColor);
            int size = Math.Max(12, (int)Math.Round(13f * DeviceDpi / 96f));
            var box = new Rectangle(1, (ClientSize.Height - size) / 2, size - 1, size - 1);
            using var border = new Pen(MutedColor);
            e.Graphics.DrawRectangle(border, box);
            if (Checked)
                using (var mark = new Pen(MutedColor, 2))
                    e.Graphics.DrawLines(mark, new Point[] { new(box.Left + 2, box.Top + size / 2), new(box.Left + size / 2 - 1, box.Bottom - 3), new(box.Right - 2, box.Top + 2) });
            var textBounds = new Rectangle(box.Right + 5, 0, Math.Max(0, Width - box.Right - 5), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, MutedColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private sealed class DarkComboBox : ComboBox
    {
        public DarkComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count)
            {
                base.OnDrawItem(e);
                return;
            }

            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color background = !Enabled ? DisabledSurfaceColor : selected ? HeaderColor : SurfaceColor;
            Color foreground = Enabled ? InkColor : MutedColor;
            using (var brush = new SolidBrush(background))
                e.Graphics.FillRectangle(brush, e.Bounds);

            var textBounds = Rectangle.Inflate(e.Bounds, -7, 0);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, textBounds, foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0)
                e.DrawFocusRectangle();
        }
    }

    private sealed class DarkButton : Button
    {
        public DarkButton() => SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Enabled) return;

            var bounds = ClientRectangle;
            using (var fill = new SolidBrush(DisabledSurfaceColor))
                e.Graphics.FillRectangle(fill, bounds);
            if (FlatAppearance.BorderSize > 0)
            {
                using var border = new Pen(BorderColor, Math.Max(1, FlatAppearance.BorderSize));
                e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, bounds.Width - 1), Math.Max(0, bounds.Height - 1));
            }

            var textBounds = Rectangle.Inflate(bounds, -Padding.Left, -Padding.Top);
            textBounds.Width -= Padding.Right;
            textBounds.Height -= Padding.Bottom;
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            flags |= TextAlign switch
            {
                ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
                ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
                _ => TextFormatFlags.HorizontalCenter
            };
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, MutedColor, flags);
        }
    }

    public MainForm() : this(new SettingsStore()) { }

    internal MainForm(SettingsStore store)
    {
        this.store = store;
        Text = "鼠标连点器";
        Size = new Size(500, 380);
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        BackColor = CanvasColor;
        ForeColor = InkColor;

        var shell = new Panel { Dock = DockStyle.Fill, BackColor = CanvasColor, TabIndex = 0 };
        Controls.Add(shell);
        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 36, BackColor = NavigationColor,
            Padding = new Padding(5, 3, 5, 0), WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, Name = "navigationTabs", TabIndex = 0
        };
        var mainArea = new Panel { Dock = DockStyle.Fill, BackColor = CanvasColor, TabIndex = 1 };
        shell.Controls.Add(mainArea);
        shell.Controls.Add(navigation);

        var pageHost = new Panel { Dock = DockStyle.Fill, BackColor = CanvasColor, Name = "pageHost", TabIndex = 0 };
        var footer = new Panel
        {
            Dock = DockStyle.Bottom, Height = 40, BackColor = SurfaceColor,
            Padding = new Padding(6, 2, 6, 2), Name = "globalStatusBar", TabIndex = 1
        };
        mainArea.Controls.Add(pageHost);
        mainArea.Controls.Add(footer);
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        var footerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = SurfaceColor,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        footerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.Controls.Add(footerLayout);
        footerLayout.Controls.Add(status, 0, 0);
        footerLayout.Controls.Add(stopCurrent, 1, 0);

        var clickPage = CreatePage("连点控制", "配置目标、点击位置、节奏和停止条件。运行状态与停止入口固定在窗口底部。");
        clickPage.Page.Controls["pageHeader"]!.Visible = false;
        var recordingPage = CreatePage("录制回放", "按录制、保存、重新加载、回放的流程管理实际录制。");
        var macroPage = CreatePage("基础宏", "管理基础宏。动作类型限定为 move、click、wait、loop、ref、key、text。");
        var logPage = CreatePage("运行日志", "查看最近 500 条运行记录；日志继续写入本机应用日志目录。");
        var settingsPage = CreatePage("设置", "配置启停热键、触发模式、设置保存和辅助工具。");
        var pages = new Dictionary<string, PageView>
        {
            ["click"] = clickPage,
            ["recording"] = recordingPage,
            ["macro"] = macroPage,
            ["logs"] = logPage,
            ["settings"] = settingsPage
        };
        foreach (var view in pages.Values)
        {
            view.Page.Visible = false;
            pageHost.Controls.Add(view.Page);
        }

        var navButtons = new Dictionary<string, Button>();
        var navItems = new (string Key, string Label)[]
        {
            ("click", "连点控制"),
            ("recording", "录制回放"),
            ("macro", "基础宏"),
            ("logs", "运行日志"),
            ("settings", "设置")
        };
        for (int i = 0; i < navItems.Length; i++)
        {
            var item = navItems[i];
            var nav = new DarkButton
            {
                Name = $"nav_{item.Key}", Text = item.Label, AccessibleName = item.Label,
                Size = new Size(89, 31), Margin = new Padding(0, 0, 2, 0),
                FlatStyle = FlatStyle.Flat, BackColor = NavigationColor, ForeColor = NavigationTextColor,
                TextAlign = ContentAlignment.MiddleCenter, Padding = Padding.Empty,
                Font = Font, UseVisualStyleBackColor = false, Cursor = Cursors.Hand,
                TabIndex = i, Tag = false
            };
            nav.FlatAppearance.BorderSize = 0;
            nav.FlatAppearance.MouseOverBackColor = Color.FromArgb(39, 106, 69);
            nav.FlatAppearance.MouseDownBackColor = SelectedNavigationColor;
            nav.Resize += (_, _) => ApplyRoundedNavigationRegion(nav);
            ApplyRoundedNavigationRegion(nav);
            nav.Paint += (_, e) =>
            {
                if (nav.Tag is true)
                {
                    using var accent = new SolidBrush(ActionBandColor);
                    e.Graphics.FillRectangle(accent, 10, nav.Height - 3, nav.Width - 20, 2);
                }
            };
            nav.Click += (_, _) => ActivatePage(item.Key);
            navigation.Controls.Add(nav);
            navButtons.Add(item.Key, nav);
        }

        void ActivatePage(string key)
        {
            foreach (var (pageKey, view) in pages)
            {
                view.Page.Visible = pageKey == key;
                if (pageKey == key) view.Page.BringToFront();
            }
            foreach (var (navKey, nav) in navButtons)
            {
                bool selected = navKey == key;
                nav.Tag = selected;
                nav.BackColor = selected ? SelectedNavigationColor : NavigationColor;
                nav.Invalidate();
            }
        }

        // 配置区按使用顺序组织，条件行切换时保留原控件和输入。
        var clickLeft = CreateVerticalLayout(1, "dashboardStack");
        clickPage.Content.Controls.Add(clickLeft);

        var targetGroup = Group("", clickLeft, 0, 0);
        var form = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
            ColumnCount = 4, RowCount = 8, BackColor = SurfaceColor, Margin = Padding.Empty, Padding = Padding.Empty
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 8; row++) form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        targetGroup.Controls.Add(form);
        targetGroup.SizeChanged += (_, _) =>
            form.MinimumSize = new Size(Math.Max(0, targetGroup.ClientSize.Width - targetGroup.Padding.Horizontal), 0);
        static Label FieldLabel(string value) => new() { Text = value, AutoSize = false, Height = 28, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = MutedColor, BackColor = SurfaceColor, Margin = new Padding(3, 1, 3, 1) };
        button.Width = position.Width = stopMode.Width = 136;
        form.Controls.Add(FieldLabel("鼠标按键"), 0, 0);
        form.Controls.Add(button, 1, 0);
        form.Controls.Add(FieldLabel("间隔（ms）"), 2, 0);
        form.Controls.Add(intervalMin, 3, 0);
        button.Anchor = intervalMin.Anchor = position.Anchor = stopMode.Anchor = AnchorStyles.Left;
        position.Width = button.Width;
        form.Controls.Add(FieldLabel("位置模式"), 0, 1);
        form.Controls.Add(position, 1, 1);

        var fixedRow = NewFlowRow();
        fixedRow.Controls.AddRange([FieldLabel("坐标 X"), x, FieldLabel("坐标 Y"), y, pickPoint, testOnce]);
        fixedRow.Controls[0].Width = 48;
        fixedRow.Controls[2].Width = 48;
        x.Width = y.Width = 62;
        fixedCard = fixedRow;
        form.Controls.Add(fixedRow, 0, 2);
        form.SetColumnSpan(fixedRow, 4);
        fixedRow.Visible = false;

        var pointsPanel = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 3, BackColor = SurfaceColor, Margin = Padding.Empty, Padding = Padding.Empty, Dock = DockStyle.Top };
        pointsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pointsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pointsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pointsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pointsCard = pointsPanel;
        pointList.Height = 104;
        pointList.Dock = DockStyle.Top;
        pointsPanel.Controls.Add(pointList, 0, 0);
        var pointButtons = NewFlowRow();
        pointButtons.Controls.AddRange([addPoint, removePoint, clearPoints]);
        pointsPanel.Controls.Add(pointButtons, 0, 1);
        pointOrder.Width = 150;
        var pointOrderRow = NewFlowRow();
        pointOrderRow.Controls.AddRange([FieldLabel("循环顺序"), pointOrder]);
        pointOrderRow.Controls[0].Width = 88;
        pointsPanel.Controls.Add(pointOrderRow, 0, 2);
        form.Controls.Add(pointsPanel, 0, 3);
        form.SetColumnSpan(pointsPanel, 4);
        pointsPanel.Visible = false;

        form.Controls.Add(FieldLabel("停止方式"), 0, 4);
        form.Controls.Add(stopMode, 1, 4);
        countRow = NewFlowRow();
        countRow.Controls.AddRange([FieldLabel("次数"), count]);
        countRow.Controls[0].Width = 80;
        durationRow = NewFlowRow();
        durationRow.Controls.AddRange([FieldLabel("时长（ms）"), duration]);
        durationRow.Controls[0].Width = 80;
        foreach (var field in new[] { x, y, count, duration })
            field.Margin = new Padding(3, 4, 3, 3);
        var stopDetail = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = SurfaceColor, Margin = Padding.Empty };
        stopDetail.Controls.AddRange([countRow, durationRow]);
        form.Controls.Add(stopDetail, 2, 4);
        form.SetColumnSpan(stopDetail, 2);
        var radiusTip = new ToolTip { AutoPopDelay = 8000, InitialDelay = 450, ReshowDelay = 150, ShowAlways = true };
        radiusTip.SetToolTip(radius, "随机偏移半径（像素）；设为 0 时关闭。范围和默认值不变。");
        start.Text = "开始连点";
        start.Size = new Size(102, 32);
        var runRow = NewFlowRow();
        runRow.Name = "clickActionBand";
        runRow.Dock = DockStyle.Top;
        runRow.Padding = new Padding(6, 2, 6, 2);
        runRow.Controls.Add(start);
        runRow.Controls.Add(clickHotkey);
        advancedToggle = new DarkButton { Text = "更多设置 ▸", AutoSize = true };
        form.Controls.Add(advancedToggle, 0, 5);
        form.SetColumnSpan(advancedToggle, 4);
        advancedDetails = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = SurfaceColor, Visible = false };
        form.Controls.Add(advancedDetails, 0, 6);
        form.SetColumnSpan(advancedDetails, 4);
        advancedToggle.Click += (_, _) => { advancedExpanded = !advancedExpanded; RefreshVisibility(); };
        AddRow(advancedDetails, "点击动作", action);
        AddRow(advancedDetails, "随机偏移", radius);
        var intervalRow = NewFlowRow();
        intervalRow.Controls.Add(new Label
        {
            Text = "间隔上限（ms）", Width = 108, Height = 40, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MutedColor, BackColor = SurfaceColor
        });
        intervalRow.Controls.Add(intervalMax);
        advancedDetails.Controls.Add(intervalRow);
        var intervalTip = new ToolTip { AutoPopDelay = 8000, InitialDelay = 450, ReshowDelay = 150, ShowAlways = true };
        intervalTip.SetToolTip(intervalMin, "最小点击间隔（毫秒）");
        intervalTip.SetToolTip(intervalMax, "最大点击间隔（毫秒）");
        var presets = NewFlowRow();
        var fast = new DarkButton { Text = "高速 10–100", AutoSize = true };
        var mid = new DarkButton { Text = "中速 100–1000", AutoSize = true };
        var slow = new DarkButton { Text = "低速 1–10s", AutoSize = true };
        fast.Click += (_, _) => { intervalMin.Value = 10; intervalMax.Value = 100; };
        mid.Click += (_, _) => { intervalMin.Value = 100; intervalMax.Value = 1000; };
        slow.Click += (_, _) => { intervalMin.Value = 1000; intervalMax.Value = 10000; };
        presets.Controls.AddRange([fast, mid, slow]);
        advancedDetails.Controls.Add(presets);
        advancedDetails.Controls.Add(estimate);
        form.Controls.Add(runRow, 0, 7);
        form.SetColumnSpan(runRow, 4);

        // 录制、摘要、回放和编辑按使用顺序放在同一工作区。
        var recordingLayout = CreateVerticalLayout(1, "recordingLayout");
        recordingPage.Content.Controls.Add(recordingLayout);
        var recordEntryGroup = Group("录制与文件", recordingLayout, 0, 0);
        var recordEntryRow = NewFlowRow();
        recordEntryRow.Controls.AddRange([recordButton, loadRecording, libraryButton]);
        recordEntryGroup.Controls.Add(recordEntryRow);

        recordEntryGroup.Controls.Add(recordInfo);
        recordPathToggle = new DarkButton { Text = "查看路径 ▸", AutoSize = true, Visible = false };
        recordPathToggle.Click += (_, _) => { recordPathExpanded = !recordPathExpanded; RefreshStatus(); };
        recordEntryGroup.Controls.Add(recordPathToggle);
        var playbackGroup = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = SurfaceColor, Margin = Padding.Empty };
        playbackCard = playbackGroup;
        recordEntryGroup.Controls.Add(playbackGroup);
        playSpeed.Width = 94;
        playLoops.Width = 106;
        var playbackSettings = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty, BackColor = SurfaceColor };
        playbackSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        playbackSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        playbackSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        playbackSettings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        playbackSettings.Controls.Add(FieldLabel("回放倍速"), 0, 0);
        playbackSettings.Controls.Add(playSpeed, 1, 0);
        playbackSettings.Controls.Add(FieldLabel("回放轮数"), 2, 0);
        playbackSettings.Controls.Add(playLoops, 3, 0);
        playbackGroup.Controls.Add(playbackSettings);
        var playbackButtons = NewFlowRow();
        playbackButtons.Controls.AddRange([playButton, playPause]);
        playbackGroup.Controls.Add(playbackButtons);
        playSpeed.SelectedIndex = 1;
        playLoops.SelectedIndex = 0;
        var editEventsGroup = recordEntryGroup;
        editEvents.Text = "编辑事件";
        editToggle = new DarkButton { Text = "编辑与优化 ▸", AutoSize = true };
        editToggle.Click += (_, _) => { editExpanded = !editExpanded; RefreshVisibility(); };
        editEventsGroup.Controls.Add(editToggle);
        editDetails = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = SurfaceColor, Visible = false };
        editEventsGroup.Controls.Add(editDetails);
        var editToolsRow = NewFlowRow();
        editToolsRow.Controls.AddRange([editEvents, optimizeSave]);
        editDetails.Controls.Add(editToolsRow);
        editDetails.Controls.Add(thinMoves);
        new ToolTip().SetToolTip(thinMoves,
            "拖拽（按住期间）的移动一律保留原样不抽稀。\n非拖拽移动抽稀仍可能改变悬停位置、经过路径和移动节奏，请按需开启。");

        // 基础宏：左侧持续显示名称、路径、摘要和运行结果，右侧只放入口。
        var macroInfoStack = CreateCardStack(1);
        macroPage.Content.Controls.Add(macroInfoStack);
        var macroSummaryGroup = Group("基础宏", macroInfoStack, 0, 0);
        macroSummaryGroup.Controls.Add(macroInfo);
        macroDetailsToggle = new DarkButton { Text = "查看路径 ▸", AutoSize = true, Visible = false };
        macroDetailsToggle.Click += (_, _) => { macroDetailsExpanded = !macroDetailsExpanded; RefreshStatus(); };
        macroSummaryGroup.Controls.Add(macroDetailsToggle);
        var macroRow = NewFlowRow();
        macroRow.Controls.AddRange([loadMacro, editMacro, runMacro]);
        macroSummaryGroup.Controls.Add(macroRow);

        // 日志列表填满内容区，窗口缩放时随右侧页面一起伸缩。
        var logCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = SurfaceColor,
            Padding = new Padding(14), Margin = Padding.Empty
        };
        logCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        logCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        logCard.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, logCard.Width - 1), Math.Max(0, logCard.Height - 1));
        };
        var logHeading = new Panel { Dock = DockStyle.Fill, BackColor = SurfaceColor };
        var logTitle = new Label { Name = "sectionHeader", Text = "最近运行记录", AutoSize = true, Location = new Point(2, 5), Font = SectionFont, ForeColor = InkColor };
        cursor.Dock = DockStyle.Right;
        cursor.Width = 180;
        cursor.TextAlign = ContentAlignment.MiddleRight;
        logHeading.Controls.Add(cursor);
        logHeading.Controls.Add(logTitle);
        logCard.Controls.Add(logHeading, 0, 0);
        logCard.Controls.Add(logList, 0, 1);
        logPage.Content.AutoScroll = false;
        logPage.Content.Controls.Add(logCard);

        // 设置项可独立滚动，保存入口固定在页面底部。
        settingsPage.Content.AutoScroll = false;
        var settingsBody = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true, BackColor = CanvasColor, TabIndex = 0
        };
        var settingsFooter = new Panel
        {
            Dock = DockStyle.Bottom, Height = 37, Padding = new Padding(8, 2, 8, 2),
            BackColor = HeaderColor, TabIndex = 1
        };
        save.AutoSize = false;
        save.Width = 96;
        save.Dock = DockStyle.Right;
        settingsFooter.Controls.Add(save);
        settingsPage.Content.Controls.Add(settingsBody);
        settingsPage.Content.Controls.Add(settingsFooter);

        var settingsLayout = CreateVerticalLayout(2, "settingsLayout");
        settingsBody.Controls.Add(settingsLayout);
        var hotkeyGroup = Group("启停快捷键", settingsLayout, 0, 0);
        trigger.Width = 266;
        trigger.Margin = new Padding(3, 8, 3, 3);
        AddRow(hotkeyGroup, "快捷键", hotkeyDraftLabel, editHotkey, hotkeyDraftState);
        editHotkey.Click += (_, _) => CaptureHotkey();

        var triggerGroup = hotkeyGroup;
        AddRow(triggerGroup, "触发模式", trigger);
        var applyRow = NewFlowRow();
        applyRow.Controls.Add(applyHotkey);
        triggerGroup.Controls.Add(applyRow);
        hotkeyStatus.MaximumSize = new Size(390, 0);
        triggerGroup.Controls.Add(hotkeyStatus);
        var hotkeyTip = new ToolTip();
        hotkeyTip.SetToolTip(trigger, "绑定的鼠标侧键在触发时由连点器接管。运行期间不能修改启停热键。紧急停止：Ctrl+Shift+F12。");

        var persistenceGroup = Group("其他设置", settingsLayout, 0, 1);
        var preferences = NewFlowRow();
        startup.Margin = new Padding(4, 8, 12, 4);
        preferences.Controls.AddRange([startup, openCps]);
        persistenceGroup.Controls.Add(preferences);
        var targetRow = NewFlowRow();
        var captureTarget = new DarkButton { Name = "captureBackgroundTarget", Text = "选择当前前台窗口", AutoSize = true };
        targetRow.Controls.AddRange([backgroundMode, captureTarget]);
        persistenceGroup.Controls.Add(targetRow);
        persistenceGroup.Controls.Add(backgroundTargetLabel);
        captureTarget.Click += async (_, _) =>
        {
            if (AnyBusy) return;
            backgroundTargetLabel.Text = "3 秒内切到目标窗口，并把指针放在目标区域…";
            await Task.Delay(3000);
            if (exiting || IsDisposed || AnyBusy) return;
            try
            {
                var hwnd = BackgroundInputDriver.ForegroundWindow;
                var point = Cursor.Position;
                var driver = new BackgroundInputDriver(hwnd, initialScreenPoint: point);
                driver.ValidatePoint(point.X, point.Y);
                backgroundTarget = driver.Identity;
                var info = WindowTargetResolver.EnumerateVisibleTopLevels().FirstOrDefault(w => w.Hwnd == hwnd);
                backgroundTargetLabel.Text = info is null ? "目标窗口已选定" : $"目标：{info.Title}";
            }
            catch (Exception ex) { backgroundTarget = null; backgroundTargetLabel.Text = "尚未选定后台窗口"; ShowError(ex.Message); }
        };
        var scheduleClick = new DarkButton { Name = "scheduleClick", Text = "定时连点", AutoSize = true };
        var schedulePlayback = new DarkButton { Name = "schedulePlayback", Text = "定时回放", AutoSize = true };
        advancedDetails.Controls.Add(scheduleClick);
        playbackGroup.Controls.Add(schedulePlayback);
        scheduleClick.Click += (_, _) => ScheduleTask(false);
        schedulePlayback.Click += (_, _) => ScheduleTask(true);
        editMacro.Click += (_, _) => EditMacro();
        loadMacro.Click += (_, _) => LoadMacroFile();
        runMacro.Click += (_, _) => ToggleMacro();

        var menu = new ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("开始 / 停止", null, (_, _) => Toggle());
        menu.Items.Add("停止", null, (_, _) => { CancelScheduledStart(); engine.Stop(); playback.Stop(); macro.Stop(); });
        menu.Items.Add("退出", null, (_, _) => { exiting = true; Close(); });
        tray = new NotifyIcon { Text = "鼠标连点器", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };

        start.Click += (_, _) => Toggle();
        stopCurrent.Click += (_, _) => StopCurrentTask();
        position.SelectedIndexChanged += (_, _) => SyncEnable();
        stopMode.SelectedIndexChanged += (_, _) => SyncEnable();
        action.SelectedIndexChanged += (_, _) => SyncEnable();
        trigger.SelectedIndexChanged += (_, _) => { UpdateEstimate(); UpdateHotkeyDraft(); };
        pickPoint.Click += (_, _) => PickPoint();
        testOnce.Click += (_, _) => TestOnce();
        addPoint.Click += (_, _) => { var p = Cursor.Position; AddPoint(new PointSpec(p.X, p.Y)); };
        removePoint.Click += (_, _) => { if (pointList.SelectedIndex >= 0) { points.RemoveAt(pointList.SelectedIndex); pointList.Items.RemoveAt(pointList.SelectedIndex); } };
        clearPoints.Click += (_, _) => { points.Clear(); pointList.Items.Clear(); };
        applyHotkey.Click += (_, _) => ApplyHotkeys(announce: true);
        recordButton.Click += (_, _) => ToggleRecording();
        loadRecording.Click += (_, _) => LoadRecording();
        playButton.Click += (_, _) => TogglePlayback();
        playPause.Click += (_, _) => TogglePlayPause();
        optimizeSave.Click += (_, _) => OptimizeSaveAs();
        editEvents.Click += (_, _) => EditEventWaits();
        libraryButton.Click+=(_,_)=>OpenLibrary();
        startup.Checked=StartupSettings.Enabled;
        startup.CheckedChanged+=(_,_)=>{try{StartupSettings.SetEnabled(startup.Checked);}catch(Exception ex){ShowError(ex.Message);}};
        openCps.Click += (_, _) =>
        {
            if (cpsWindow is null || cpsWindow.IsDisposed) cpsWindow = new CpsMeterForm();
            cpsWindow.Show(this);
            cpsWindow.Activate();
        };
        save.Click += (_, _) =>
        {
            try
            {
                SaveSettings(ReadSettingsForSave());
                MessageBox.Show(this, "设置已保存，下次启动会自动读取。", "设置");
            }
            catch (Exception ex) { ShowError(ex.Message); }
        };
        intervalMin.ValueChanged += (_, _) =>
        {
            if (!loadingSettings && !advancedExpanded && intervalMax.Value == previousInterval)
                intervalMax.Value = intervalMin.Value;
            previousInterval = intervalMin.Value;
            UpdateEstimate();
        };
        intervalMax.ValueChanged += (_, _) => UpdateEstimate();
        refresh.Tick += (_, _) => PollStatus();
        engine.Log += message => logger.Write(message);
        playback.Log += message => logger.Write(message);
        // The worker confirms a pause asynchronously; refresh when request/confirm flips
        // so the status shows "正在暂停…" then "已暂停" only after it really parks.
        playback.PauseStateChanged += () => BeginInvoke(() => RefreshStatus());
        logger.EntryAdded += OnLogEntry;
        FormClosing += OnClosing;
        Shown += (_, _) => { InitializeServices(); InitializeLibrary(); };
        ApplyVisualStyle();
        foreach (var control in Descendants(this))
        {
            if (control is Button styledButton && !styledButton.Name.StartsWith("nav_", StringComparison.Ordinal))
                styledButton.EnabledChanged += (_, _) => ApplyButtonStateStyle(styledButton);
            else if (control is ComboBox or NumericUpDown or ListBox or CheckBox)
            {
                control.EnabledChanged += (_, _) => ApplyEnabledControlStyle(control);
                ApplyEnabledControlStyle(control);
            }
        }
        ActivatePage("click");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.ApplyDarkTitleBar(Handle);
    }

    private void InitializeServices()
    {
        loadingSettings = true;
        try
        {
            var saved = store.Load();
            button.SelectedIndex = (int)saved.Button;
            action.SelectedIndex = (int)saved.Action;
            position.SelectedIndex = (int)saved.Position;
            stopMode.SelectedIndex = (int)saved.Stop;
            trigger.SelectedIndex = (int)saved.Trigger;
            intervalMin.Value = saved.IntervalMinMs;
            intervalMax.Value = saved.IntervalMaxMs;
            count.Value = saved.Count;
            duration.Value = saved.DurationMs;
            radius.Value = saved.RandomOffsetRadius;
            x.Value = Math.Clamp(saved.X, -100000, 100000);
            y.Value = Math.Clamp(saved.Y, -100000, 100000);
            points.AddRange(saved.Points);
            pointList.Items.AddRange(points.Select(p => $"({p.X}, {p.Y})").ToArray());
            pointOrder.SelectedIndex = (int)saved.PointOrder;
            draftCtrl = saved.HoldCtrl;
            draftAlt = saved.HoldAlt;
            draftShift = saved.HoldShift;
            draftVk = saved.HoldKeyVk;
            savedSettings = saved;
        }
        catch (Exception ex)
        {
            button.SelectedIndex = action.SelectedIndex = position.SelectedIndex = 0;
            stopMode.SelectedIndex = trigger.SelectedIndex = pointOrder.SelectedIndex = 0;
            ShowError($"设置读取失败，使用默认值：{ex.Message}");
        }
        loadingSettings = false;
        UpdateHotkeyDraft();
        previousInterval = intervalMin.Value;
        advancedExpanded = action.SelectedIndex != 0 || radius.Value != 0 || intervalMin.Value != intervalMax.Value;
        ApplyHotkeys(announce: false);
        if (savedSettings is not null)
            autoSaveBaseline = ReadSettingsForSave();
        SyncEnable();
        UpdateEstimate();
        logList.Items.AddRange(logger.Snapshot());
        RefreshStatus();
        VisibleChanged += (_, _) => UpdateRefreshTimer();
        Resize += (_, _) => UpdateRefreshTimer();
        UpdateRefreshTimer();
    }

    private ClickSettings ReadSettings()
    {
        return new ClickSettings
        {
            Button = (ClickButton)button.SelectedIndex,
            Action = (ClickAction)action.SelectedIndex,
            Position = (PositionMode)position.SelectedIndex,
            X = (int)x.Value, Y = (int)y.Value,
            Points = [.. points],
            PointOrder = (MultiPointOrder)pointOrder.SelectedIndex,
            RandomOffsetRadius = (int)radius.Value,
            IntervalMinMs = (int)intervalMin.Value,
            IntervalMaxMs = (int)intervalMax.Value,
            Stop = (StopCondition)stopMode.SelectedIndex,
            Count = (int)count.Value,
            DurationMs = (int)duration.Value,
            Trigger = appliedToggle ? TriggerMode.HotkeyToggle : TriggerMode.HoldKey,
            HoldKeyVk = appliedVk,
            HoldCtrl = appliedCtrl,
            HoldAlt = appliedAlt,
            HoldShift = appliedShift
        };
    }

    private ClickSettings ReadSettingsForSave()
    {
        var current = ReadSettings();
        if (toggleReady || triggerHook is not null || savedSettings is not { } saved)
            return current;
        // A registration conflict at startup does not erase the saved binding.
        return current with
        {
            Trigger = saved.Trigger, HoldKeyVk = saved.HoldKeyVk,
            HoldCtrl = saved.HoldCtrl, HoldAlt = saved.HoldAlt, HoldShift = saved.HoldShift
        };
    }

    private void SaveSettings(ClickSettings settings)
    {
        store.Save(settings);
        savedSettings = autoSaveBaseline = settings;
    }

    private bool SaveChangesOnClose(CloseReason reason, bool hide)
    {
        // A file that could not be loaded stays untouched on automatic save.
        if (autoSaveBaseline is not { } previous) return true;
        try
        {
            var current = ReadSettingsForSave();
            if (current with { Points = previous.Points } != previous || !current.Points.SequenceEqual(previous.Points))
                SaveSettings(current);
            return true;
        }
        catch (Exception ex)
        {
            logger.Write($"关闭窗口时保存设置失败：{ex.Message}");
            if (reason != CloseReason.UserClosing) return true;
            if (!Visible) Show();
            var operation = hide ? "隐藏窗口" : "退出";
            return MessageBox.Show(this, $"设置未能保存：{ex.Message}\n\n仍要{operation}吗？",
                "保存设置失败", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                == DialogResult.Yes;
        }
    }

    private bool AnyBusy => scheduleDialogOpen || scheduledStart is not null || engine.IsActive || playback.IsActive || macro.IsActive || recorder is not null;

    private IInputDriver? BackgroundDriver(bool clientCoordinates = false)
    {
        if (!backgroundMode.Checked) return null;
        if (backgroundTarget is not { } target) throw new InvalidOperationException("请先在设置中选择后台目标窗口。");
        return new BackgroundInputDriver(target.Handle, clientCoordinates, expected: target);
    }

    private async void ScheduleTask(bool replay)
    {
        if (AnyBusy) { ShowError("请先停止当前任务或取消定时。"); return; }
        if (!emergencyReady) { ShowError("紧急停止未就绪，无法定时启动。"); return; }
        var settings = ReadSettings();
        var recording = loadedRecording;
        var speed = SelectedSpeed();
        var loops = SelectedLoops();
        bool background = backgroundMode.Checked;
        var target = backgroundTarget;
        try
        {
            if (replay)
            {
                if (recording is null) throw new InvalidOperationException("请先加载录制。");
                RecordingStore.Validate(recording);
                if (!background) PlaybackEngine.EnsureCoordinatesFit(recording);
                if (!background)
                {
                    var mismatch = PlaybackEngine.DescribeEnvironmentMismatch(recording, DeviceDpi > 0 ? DeviceDpi : 96);
                    if (mismatch is not null && MessageBox.Show(this, mismatch, "录制环境不一致", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                }
            }
            else settings.Validate();
            if (background)
            {
                var preflight = (BackgroundInputDriver)BackgroundDriver(replay && recording?.WindowBind is not null)!;
                if (replay)
                    foreach (var entry in recording!.Events.Where(e => RecordingEventType.IsMouse(e.Type)))
                        preflight.ValidatePoint(entry.X, entry.Y);
                else if (settings.Position == PositionMode.Fixed)
                    preflight.ValidatePoint(settings.X, settings.Y);
                else if (settings.Position == PositionMode.MultiPoints)
                    foreach (var point in settings.Points) preflight.ValidatePoint(point.X, point.Y);
            }
        }
        catch (Exception ex) { ShowError(ex.Message); return; }
        using var dialog = new ScheduleDialog();
        DialogResult result;
        scheduleDialogOpen = true;
        try { result = dialog.ShowDialog(this); }
        finally { scheduleDialogOpen = false; }
        if (result != DialogResult.OK) return;
        if (AnyBusy) { ShowError("当前已有任务，请先停止后重新定时。"); return; }
        var source = new CancellationTokenSource();
        scheduledStart = source;
        scheduledDescription = $"定时等待至 {dialog.Due.LocalDateTime:yyyy-MM-dd HH:mm:ss}";
        SyncEnable(); RefreshStatus();
        try
        {
            while (true)
            {
                var remaining = dialog.Due - DateTimeOffset.Now;
                if (remaining <= TimeSpan.Zero) break;
                await Task.Delay(remaining > TimeSpan.FromHours(12) ? TimeSpan.FromHours(12) : remaining, source.Token);
            }
            source.Token.ThrowIfCancellationRequested();
            if (engine.IsActive || playback.IsActive || macro.IsActive || recorder is not null)
                throw new InvalidOperationException("其他任务正在运行，定时任务未启动。");
            scheduledStart = null; scheduledDescription = null;
            IInputDriver? driver = background && target is not null
                ? new BackgroundInputDriver(target.Handle, replay && recording?.WindowBind is not null, expected: target)
                : null;
            if (replay) playback.Start(recording!, speed, loops, driver);
            else engine.Start(settings, driver);
            logger.Write(replay ? "定时回放已开始。" : "定时连点已开始。");
        }
        catch (OperationCanceledException) { logger.Write("已取消定时启动。"); }
        catch (Exception ex) { logger.Write($"定时启动失败：{ex.Message}"); if (!exiting) ShowError($"定时启动失败：{ex.Message}"); }
        finally
        {
            if (scheduledStart == source) { scheduledStart = null; scheduledDescription = null; }
            source.Dispose();
            if (!exiting) { SyncEnable(); RefreshStatus(); }
        }
    }

    private void CancelScheduledStart()
    {
        scheduledStart?.Cancel();
    }

    // Only reads hotkey-related controls; the full click config is validated at
    // Start/Save, so an unrelated bad value (e.g. empty multi-point list) can no
    // longer crash hotkey application.
    private void ApplyHotkeys(bool announce)
    {
        if (AnyBusy)
        {
            ShowError("连点、录制或回放运行期间不能更换热键，请先停止当前任务。");
            return;
        }

        int vk = draftVk;
        bool ctrl = draftCtrl, alt = draftAlt, shift = draftShift;
        bool toggleMode = trigger.SelectedIndex == (int)TriggerMode.HotkeyToggle;

        try
        {
            hotkeys ??= new GlobalHotkeys(Handle);
            // Safety net first; once registered it is never torn down by a toggle failure.
            hotkeys.EnsureEmergency();
            emergencyReady = true;

            if (!ClickSettings.IsAllowedHotkey(vk, ctrl, alt)) throw new InvalidOperationException("不支持该快捷键组合。");
            if (vk == 0x7B && ctrl && shift && !alt) throw new InvalidOperationException("该组合用于紧急停止。");
            if (ctrl && alt && !shift && library is not null && library.Entries.Any(e => e.Hotkey == vk))
                throw new InvalidOperationException("该组合与录制库快捷键冲突。");

            bool mouseSideButton = ClickSettings.IsMouseSideButton(vk);
            if (toggleMode && !mouseSideButton)
            {
                if (!hotkeys.ReplaceToggle(vk, HotkeyModifiers(ctrl, alt, shift), out var error))
                    throw new InvalidOperationException(error);
                toggleReady = true;
                triggerHook?.Dispose();
                triggerHook = null;
            }
            else
            {
                var nextHook = new KeyboardHook
                {
                    PrimaryVk = (uint)vk,
                    RequireCtrl = ctrl,
                    RequireAlt = alt,
                    RequireShift = shift,
                    CanPressSideButton = CanTriggerFromSideButton
                };
                nextHook.Pressed = () =>
                {
                    if (!mouseSideButton) { OnHoldPressed(); return; }
                    BeginInvoke(() =>
                    {
                        if (triggerHook != nextHook || !CanTriggerFromSideButton()) return;
                        if (toggleMode) Toggle();
                        else OnHoldPressed();
                    });
                };
                if (!toggleMode)
                    nextHook.Released = () =>
                    {
                        if (!mouseSideButton) { engine.Stop(); return; }
                        BeginInvoke(() =>
                        {
                            if (triggerHook == nextHook) engine.Stop();
                        });
                    };
                nextHook.Install();
                var oldHook = triggerHook;
                triggerHook = nextHook;
                hotkeys.RemoveToggle();
                toggleReady = false;
                oldHook?.Dispose();
            }

            appliedVk = vk; appliedCtrl = ctrl; appliedAlt = alt; appliedShift = shift; appliedToggle = toggleMode;
            UpdateHotkeyDraft();
            hotkeyError = null;
            if (announce)
                MessageBox.Show(this,
                    $"热键已生效：{(toggleMode ? $"切换热键 {HotkeyName(vk, ctrl, alt, shift)}" : $"长按 {HotkeyName(vk, ctrl, alt, shift)} 运行")}",
                    "热键");
        }
        catch (Exception ex)
        {
            hotkeyError = ex.Message;
            emergencyReady = hotkeys?.EmergencyRegistered == true;
            toggleReady = hotkeys?.ToggleRegistered == true;
            ShowError($"{ex.Message}\n" + (emergencyReady ? "紧急停止 Ctrl+Shift+F12 保持有效。" : "紧急停止未就绪，无法开始任务。"));
        }
        UpdateHotkeyStatus();
        SyncEnable();
        RefreshStatus();
    }

    private static uint HotkeyModifiers(bool ctrl, bool alt, bool shift)
    {
        uint value = 0;
        if (ctrl) value |= 0x0002;
        if (alt) value |= 0x0001;
        if (shift) value |= 0x0004;
        return value;
    }

    private static string FKeyName(int vk) => $"F{vk - 0x70 + 1}";
    internal static string HotkeyName(int vk, bool ctrl, bool alt, bool shift)
        => (ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (shift ? "Shift+" : "")
           + (vk switch
           {
               ClickSettings.MouseSide1 => "鼠标侧键 1",
               ClickSettings.MouseSide2 => "鼠标侧键 2",
               >= 0x70 and <= 0x7B => FKeyName(vk),
               0x20 => "Space",
               _ => ((char)vk).ToString()
           });

    private void UpdateHotkeyDraft()
    {
        bool pending = draftVk != appliedVk || draftCtrl != appliedCtrl || draftAlt != appliedAlt || draftShift != appliedShift
            || (trigger.SelectedIndex == (int)TriggerMode.HotkeyToggle) != appliedToggle;
        hotkeyDraftLabel.Text = HotkeyName(draftVk, draftCtrl, draftAlt, draftShift);
        hotkeyDraftState.Text = pending ? "待应用" : "";
        hotkeyDraftState.ForeColor = WarningColor;
    }

    private void CaptureHotkey()
    {
        if (AnyBusy) return;
        capturingHotkey = true;
        try
        {
            using var capture = new HotkeyCaptureForm();
            if (capture.ShowDialog(this) == DialogResult.OK)
            {
                draftVk = capture.Vk; draftCtrl = capture.Ctrl; draftAlt = capture.Alt; draftShift = capture.Shift;
                UpdateHotkeyDraft();
            }
        }
        finally { capturingHotkey = false; }
    }

    private void OnHoldPressed()
    {
        if (capturingHotkey || engine.IsActive) return;
        Toggle();
    }

    private bool CanTriggerFromSideButton()
        => !capturingHotkey && !exiting && emergencyReady
           && recorder is null && !playback.IsActive && !macro.IsActive;

    private void Toggle()
    {
        if (scheduleDialogOpen) return;
        if (scheduledStart is not null) { CancelScheduledStart(); return; }
        if (engine.IsActive) { engine.Stop(); return; }
        if (exiting) return;
        if (recorder is not null || playback.IsActive || macro.IsActive)
        {
            ShowError("录制、回放或宏进行中，请先停止后再开始连点。");
            return;
        }
        if (!emergencyReady)
        {
            ShowError("紧急停止热键尚未注册成功，为安全起见暂不允许开始，请先修复热键。");
            return;
        }
        try { engine.Start(ReadSettings(), BackgroundDriver()); }
        catch (Exception ex) { ShowError(ex.Message); }
        SyncEnable();
        RefreshStatus();
    }

    private void StopCurrentTask()
    {
        CancelScheduledStart();
        if (recorder is not null)
        {
            // Preserve the established recorder stop/save prompt; a global stop
            // must never discard the active recording.
            ToggleRecording();
            return;
        }
        engine.Stop();
        playback.Stop();
        macro.Stop();
        RefreshStatus();
    }

    private void PickPoint()
    {
        using var overlay = new CrosshairOverlay();
        PointSpec? picked = null;
        overlay.Picked += (px, py) => picked = new PointSpec(px, py);
        overlay.ShowDialog(this);
        if (picked is not null)
        {
            if (position.SelectedIndex != 1 && position.SelectedIndex != 2) position.SelectedIndex = 1;
            x.Value = Math.Clamp(picked.X, -100000, 100000);
            y.Value = Math.Clamp(picked.Y, -100000, 100000);
        }
    }

    private void TestOnce()
    {
        if (AnyBusy) return;
        try
        {
            var settings = ReadSettings();
            settings.Validate();
            var driver = BackgroundDriver() ?? new WindowsInputDriver();
            Point? position = settings.Position == PositionMode.Fixed ? new Point(settings.X, settings.Y) : null;
            try { driver.Click(settings.Button, position); }
            finally { driver.ReleaseAll(); }
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void AddPoint(PointSpec point)
    {
        points.Add(point);
        pointList.Items.Add($"({point.X}, {point.Y})");
        pointList.SelectedIndex = pointList.Items.Count - 1;
    }

    private void SyncEnable()
    {
        int act = action.SelectedIndex, pos = position.SelectedIndex, stop = stopMode.SelectedIndex;
        bool isClick = act == (int)ClickAction.Click;
        bool isPress = act == (int)ClickAction.Press;
        bool isRelease = act == (int)ClickAction.Release;
        bool fixedMode = pos == 1, multiMode = pos == 2;
        bool busy = AnyBusy;

        // Positioning. Release ignores position entirely.
        position.Enabled = !isRelease && !busy;
        x.Enabled = y.Enabled = pickPoint.Enabled = fixedMode && !isRelease;
        testOnce.Enabled = fixedMode && !isRelease && !busy;
        addPoint.Enabled = removePoint.Enabled = clearPoints.Enabled = pointList.Enabled = multiMode && !isRelease;
        // Cycling only matters for repeated clicks; a single press locates once.
        pointOrder.Enabled = multiMode && isClick;
        // Random offset only applies when a position is actually targeted (not follow, not release).
        radius.Enabled = pos != (int)PositionMode.FollowCursor && !isRelease;

        // Pace / stop.
        intervalMin.Enabled = intervalMax.Enabled = isClick;
        stopMode.Enabled = (isClick || isPress);
        count.Enabled = stop == (int)StopCondition.Count && isClick;
        duration.Enabled = stop == (int)StopCondition.Duration && (isClick || isPress);

        // Hotkeys cannot be changed while anything is running.
        editHotkey.Enabled = applyHotkey.Enabled = trigger.Enabled = !busy;
        RefreshVisibility();
        UpdateEstimate();
    }

    private void RefreshVisibility()
    {
        if (fixedCard is not null) fixedCard.Visible = position.SelectedIndex == 1;
        if (pointsCard is not null) pointsCard.Visible = position.SelectedIndex == 2;
        if (countRow is not null) countRow.Visible = stopMode.SelectedIndex == (int)StopCondition.Count;
        if (durationRow is not null) durationRow.Visible = stopMode.SelectedIndex == (int)StopCondition.Duration;
        if (advancedDetails is not null) advancedDetails.Visible = advancedExpanded;
        if (advancedToggle is not null) advancedToggle.Text = advancedExpanded ? "更多设置 ▾" : "更多设置 ▸";
        if (editDetails is not null) editDetails.Visible = editExpanded && loadedRecording is not null;
        if (editToggle is not null) { editToggle.Visible = loadedRecording is not null; editToggle.Text = editExpanded ? "编辑与优化 ▾" : "编辑与优化 ▸"; }
        if (playbackCard is not null) playbackCard.Visible = loadedRecording is not null || playback.IsActive;
        if (recordPathToggle is not null) { recordPathToggle.Visible = loadedRecording is not null; recordPathToggle.Text = recordPathExpanded ? "收起路径 ▾" : "查看路径 ▸"; }
    }

    private void UpdateEstimate()
    {
        int act = action.SelectedIndex;
        if (act == (int)ClickAction.Release)
        {
            estimate.Text = "松开：向所选按键发送一次松开后立即结束；不使用位置、间隔、次数与时长。";
            return;
        }
        if (act == (int)ClickAction.Press)
        {
            estimate.Text = "按下保持：先定位到目标再按下并保持，仅支持手动停止或时长到点，不受间隔与次数控制。";
            return;
        }
        double avg = ((int)intervalMin.Value + (int)intervalMax.Value) / 2.0;
        estimate.Text = $"节奏估算：平均 {avg:F0} ms/次 ≈ {1000 / avg:F1} 次/秒";
    }

    private void UpdateRefreshTimer()
    {
        bool shouldPoll = Visible && WindowState != FormWindowState.Minimized && !exiting;
        if (shouldPoll == refresh.Enabled) return;
        if (!shouldPoll) { refresh.Stop(); return; }
        lastPolledState = null;
        PollStatus();
        refresh.Start();
    }

    private void PollStatus()
    {
        if (cursor.Visible)
        {
            var point = Cursor.Position;
            cursor.Text = $"当前光标：X = {point.X}，Y = {point.Y}";
        }
        var current = (engine.Snapshot, playback.Snapshot, macro.Snapshot, AnyBusy, recorder?.Count ?? -1);
        if (lastPolledState == current) return;
        lastPolledState = current;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var value = engine.Snapshot;
        bool recording = recorder is not null;
        var pb = playback.Snapshot;
        var ms = macro.Snapshot;
        var state = value.State switch
        {
            RunState.Idle => "空闲", RunState.Armed => "准备中", RunState.Running => "运行中",
            RunState.Stopping => "停止中", _ => "错误"
        };
        string clickState = $"状态：{state}　已发送点击：{value.Clicks}　实际间隔：{value.AverageIntervalMs:F2} ms"
            + (value.Clicks > 1 && value.AverageIntervalMs > 0 ? $"　≈ {1000 / value.AverageIntervalMs:F1} 次/秒" : "")
            + (value.Error is null ? "" : $"　错误：{value.Error}");
        string LoopText(PlaybackSnapshot snapshot)
            => snapshot.Infinite ? $"第 {snapshot.Loop} 轮（无限）" : $"第 {snapshot.Loop}/{Math.Max(1, snapshot.LoopsTotal)} 轮";
        if (scheduledStart is not null)
        {
            status.Text = scheduledDescription ?? "定时等待中";
            status.ForeColor = WarningColor;
        }
        else if (recording)
        {
            status.Text = $"状态：录制中　已记录 {recorder!.Count} 个动作";
            status.ForeColor = DangerColor;
        }
        else if (engine.IsActive)
        {
            status.Text = clickState;
            status.ForeColor = value.Error is not null ? DangerColor : AccentColor;
        }
        else if (playback.IsActive || pb.State == RunState.Stopping)
        {
            string pauseState = playback.IsPauseConfirmed ? "已暂停" : playback.IsPauseRequested ? "正在暂停" : "回放中";
            status.Text = $"状态：{pauseState}　{LoopText(pb)}　动作 {pb.Index}/{pb.Total}　速度 {pb.Speed:0.##}×";
            status.ForeColor = playback.IsPaused ? WarningColor : AccentColor;
        }
        else if (macro.IsActive || ms.State == RunState.Stopping)
        {
            status.Text = $"状态：宏运行中　第 {ms.MacroLoop}/{ms.MacroLoops} 轮　{ms.Current ?? "准备中"}";
            status.ForeColor = AccentColor;
        }
        else
        {
            status.Text = clickState;
            status.ForeColor = value.Error is not null ? DangerColor : InkColor;
        }

        // Software stop is always available while the engine runs, independent of hotkeys.
        bool running = engine.IsActive;
        bool anyBusy = AnyBusy;
        refresh.Interval = anyBusy ? 100 : 250;
        stopCurrent.Enabled = anyBusy;
        start.Enabled = running || (emergencyReady && !exiting && recorder is null && !playback.IsActive && !macro.IsActive);
        start.Text = running ? "停止连点" : "开始连点";

        // Recording / playback controls and mutual exclusion.
        bool waiting = scheduledStart is not null;
        bool otherBusy = engine.IsActive || playback.IsActive || recording || waiting;
        recordButton.Text = recording ? "停止录制（会提示保存）" : "开始录制";
        recordButton.Enabled = !waiting && !engine.IsActive && !playback.IsActive && !macro.IsActive;
        loadRecording.Enabled = !waiting && !recording && !engine.IsActive && !playback.IsActive && !macro.IsActive;
        optimizeSave.Enabled = loadRecording.Enabled && loadedRecording is not null;
        // Editing is forbidden while anything runs OR while playback is paused (worker alive).
        editEvents.Enabled = optimizeSave.Enabled;
        thinMoves.Enabled = optimizeSave.Enabled;
        playButton.Text = playback.IsActive ? "停止回放" : "开始回放";
        playButton.Enabled = playback.IsActive || (!waiting && loadedRecording is not null && !engine.IsActive && !recording && !macro.IsActive);
        playSpeed.Enabled = playLoops.Enabled = !waiting && !playback.IsActive && !engine.IsActive && !recording && !macro.IsActive;
        playPause.Enabled = playback.IsActive;
        playPause.Visible = playback.IsActive;
        playPause.Text = playback.IsPaused ? "继续" : "暂停";

        // Macro controls; mutually exclusive with click / record / standalone playback.
        runMacro.Enabled = macro.IsActive || (loadedMacro is not null && !otherBusy);
        editMacro.Enabled = loadMacro.Enabled = !macro.IsActive && !otherBusy;
        editMacro.Text = loadedMacro is null ? "新建宏" : "编辑宏";
        runMacro.Text = macro.IsActive ? "停止宏" : "运行宏";
        runMacro.Visible = loadedMacro is not null || macro.IsActive;
        if (macroDetailsToggle is not null) { macroDetailsToggle.Visible = loadedMacro is not null; macroDetailsToggle.Text = macroDetailsExpanded ? "收起路径 ▾" : "查看路径 ▸"; }
        string recordingInfo = recording
            ? $"录制中，已记录 {recorder!.Count} 个动作。"
            : loadedRecording is null
                ? "开始录制，或打开已有录制。"
                : $"已加载：{Path.GetFileName(loadedRecordingPath ?? "内存")}，{loadedRecording.Events.Count} 个动作，"
                  + $"时长 {(loadedRecording.Events.Count == 0 ? 0 : loadedRecording.Events[^1].TimeMs) / 1000.0:0.##} 秒。";

        // Loop count and within-loop action index are reported separately.
        if (playback.IsActive || pb.State == RunState.Stopping)
        {
            // Only a CONFIRMED pause reads "已暂停"; a mere request shows "正在暂停…".
            string pausePrefix = playback.IsPauseConfirmed
                ? "已暂停（保持按住键不释放，停止会立即清理）　"
                : playback.IsPauseRequested ? "正在暂停…　" : "";
            recordingInfo += $"\n{pausePrefix}{LoopText(pb)}　{pb.Speed:0.##}×　动作 {pb.Index}/{pb.Total}"
                + (pb.Error is null ? "" : $"，错误：{pb.Error}");
        }
        // Terminal outcome (completed / manually stopped / error) and its progress
        // stay visible until the next playback Start clears the snapshot.
        else if (!string.IsNullOrEmpty(pb.Outcome))
            recordingInfo += $"\n{pb.Outcome}（{LoopText(pb)}，动作进度 {pb.Index}/{pb.Total}）"
                + (pb.Error is null ? "" : $"，失败原因：{pb.Error}");
        if (recordPathExpanded && loadedRecording is not null)
            recordingInfo += $"\n路径：{loadedRecordingPath ?? "内存录制"}";
        recordInfo.Text = recordingInfo;
        RefreshVisibility();

        // Macro line: current action, whole-macro loop, inner-loop progress, outcome.
        string macroHead = loadedMacro is null
            ? "新建宏，或打开已有宏。"
            : $"{loadedMacro.Name}　·　{loadedMacro.Steps.Count} 个动作　·　{loadedMacro.Loops} 轮"
              + (macroDetailsExpanded ? $"\n路径：{loadedMacroPath ?? "自动保存宏"}" : "");
        if (macro.IsActive || ms.State is RunState.Stopping)
            macroInfo.Text = macroHead + $"\n第 {ms.MacroLoop}/{ms.MacroLoops} 轮"
                + (string.IsNullOrEmpty(ms.Inner) ? "" : $"　{ms.Inner}")
                + $"　当前：{ms.Current ?? "…"}　{ms.State}";
        else if (!string.IsNullOrEmpty(ms.Outcome))
            macroInfo.Text = macroHead + $"\n{ms.Outcome}（第 {ms.MacroLoop}/{ms.MacroLoops} 轮）"
                + (ms.Error is null ? "" : $"，失败原因：{ms.Error}");
        else macroInfo.Text = macroHead;

        // Re-apply enable matrix when busy flips (also covers async auto-stop).
        bool busy = AnyBusy;
        if (busy != prevBusy) { prevBusy = busy; SyncEnable(); }
        var actionStyle = (running, recording, playback.IsActive, macro.IsActive, loadedRecording is not null, loadedMacro is not null);
        if (lastActionStyle != actionStyle)
        {
            lastActionStyle = actionStyle;
            ApplyActionButtonStyles();
        }
    }

    private void UpdateHotkeyStatus()
    {
        var emergency = emergencyReady ? "紧急停止 Ctrl+Shift+F12 已就绪" : "紧急停止未就绪";
        string triggerText = toggleReady || triggerHook is not null
            ? $"{(appliedToggle ? "切换热键" : "长按")} {HotkeyName(appliedVk, appliedCtrl, appliedAlt, appliedShift)} 已就绪"
            : "启停快捷键未就绪";
        clickHotkey.Text = toggleReady || triggerHook is not null
            ? $"启停：{HotkeyName(appliedVk, appliedCtrl, appliedAlt, appliedShift)}"
            : "启停快捷键未就绪";
        hotkeyStatus.Text = $"热键状态：{triggerText}\n{emergency}"
            + (hotkeyError is null ? "" : $"\n最近应用失败：{hotkeyError}");
    }

    // ---- P1 recording ----------------------------------------------------

    private void ToggleRecording()
    {
        if (scheduledStart is not null) { ShowError("请先取消定时启动。"); return; }
        if (recorder is not null) { StopRecordingAndSave(); return; }
        if (engine.IsActive || playback.IsActive || macro.IsActive) { ShowError("请先停止连点、回放或宏，再开始录制。"); return; }
        // The safety net must be ready before any task is allowed to start, so an
        // emergency-stop registration failure can never leave a recording stranded.
        if (!emergencyReady)
        {
            ShowError("紧急停止热键尚未注册成功，为安全起见暂不允许开始录制，请先修复热键。");
            return;
        }

        const string notice =
            "开始后将记录你的鼠标移动、左/中/右键按下与松开、滚轮，以及键盘按下/松开（可能包含你输入的文字）。\n\n" +
            "· 录制内容只保存在本机，不会上传；\n" +
            "· 运行日志只记录动作数量，不会记录具体按键；\n" +
            "· 启停热键与紧急停止不会被录入。\n\n确定现在开始录制吗？";
        if (MessageBox.Show(this, notice, "录制隐私提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Information)
            != DialogResult.OK) return;

        try
        {
            // Control hotkeys must never end up inside the recording; pass our own
            // window handle so clicks used to operate the controller are dropped.
            var suppressed = new List<InputRecorder.ControlChord>
            {
                new((ushort)appliedVk, appliedCtrl, appliedAlt, appliedShift),
                new(0x7B, true, false, true)
            };
            if (library is not null)
                foreach (int id in recordingHotkeys.Keys)
                    if (id - 1000 >= 0 && id - 1000 < library.Entries.Count)
                        suppressed.Add(new((ushort)library.Entries[id - 1000].Hotkey, true, true, false));
            int dpi = DeviceDpi > 0 ? DeviceDpi : 96;
            recorder = new InputRecorder(suppressed, dpi, SystemInformation.VirtualScreen, Handle);
            recorder.LimitReached += OnRecordLimitReached;
            recorder.Start();
            logger.Write("开始录制键鼠输入（日志不记录具体按键内容）。");
        }
        catch (Exception ex)
        {
            recorder?.Dispose();
            recorder = null;
            ShowError(ex.Message);
        }
        SyncEnable();
        RefreshStatus();
    }

    private void OnRecordLimitReached()
    {
        // Fires from the hook callback (UI thread); defer the modal save dialog off the callback.
        logger.Write($"已达动作上限 {RecordingSchema.MaxEvents}，自动停止录制。");
        BeginInvoke(StopRecordingAndSave);
    }

    private void StopRecordingAndSave()
    {
        if (recorder is null) return;
        var active = recorder;
        recorder = null;
        RecordingDocument doc;
        try { doc = active.Stop("录制"); }
        catch (Exception ex) { active.Dispose(); ShowError(ex.Message); SyncEnable(); RefreshStatus(); return; }
        active.Dispose();

        if (doc.Events.Count == 0)
        {
            logger.Write("录制结束：未捕获到任何动作，未保存。");
            SyncEnable(); RefreshStatus();
            return;
        }

        // Keep the recording in memory no matter what happens to the file dialog or
        // the save itself, so an over-limit/failed save can neither discard the
        // recording nor overwrite an existing file.
        loadedRecording = doc;
        loadedRecordingPath = null;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseClicker", "recordings");
        Directory.CreateDirectory(dir);
        using var dialog = new SaveFileDialog
        {
            Title = "保存录制",
            Filter = "录制文件 (*.json)|*.json",
            InitialDirectory = dir,
            FileName = $"recording-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            logger.Write($"已取消文件保存，{doc.Events.Count} 个动作保留在内存中（可直接回放或重新录制）。");
            SyncEnable(); RefreshStatus();
            return;
        }

        try
        {
            // Save validates + measures UTF-8 size BEFORE writing; on failure the
            // target file is untouched and the in-memory recording stays loaded.
            recordingStore.Save(doc, dialog.FileName);
            // Reload what was just written: save → reload, and validation gates playback.
            loadedRecording = recordingStore.Load(dialog.FileName);
            loadedRecordingPath = dialog.FileName;
            logger.Write($"录制已保存并重新加载：{Path.GetFileName(dialog.FileName)}，{loadedRecording.Events.Count} 个动作，校验通过。");
        }
        catch (Exception ex)
        {
            loadedRecordingPath = null;
            ShowError($"录制文件未保存：{ex.Message}\n内存中的录制仍保留，可直接回放，也可重新录制后另存。");
        }
        SyncEnable(); RefreshStatus();
    }

    private void LoadRecording()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseClicker", "recordings");
        Directory.CreateDirectory(dir);
        using var dialog = new OpenFileDialog { Title = "加载录制", Filter = "录制文件 (*.json)|*.json", InitialDirectory = dir };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            loadedRecording = recordingStore.Load(dialog.FileName);
            loadedRecordingPath = dialog.FileName;
            logger.Write($"已加载录制 {Path.GetFileName(dialog.FileName)}：{loadedRecording.Events.Count} 个动作，校验通过。");
        }
        catch (Exception ex)
        {
            loadedRecording = null;
            loadedRecordingPath = null;
            ShowError($"录制加载失败：{ex.Message}");
        }
        RefreshStatus();
    }

    private void TogglePlayback()
    {
        if (scheduledStart is not null) { CancelScheduledStart(); return; }
        if (playback.IsActive) { playback.Stop(); return; }
        if (engine.IsActive || macro.IsActive || recorder is not null) { ShowError("请先停止连点、宏或录制，再开始回放。"); return; }
        if (loadedRecording is null) { ShowError("请先录制并保存，或加载一个校验通过的录制文件。"); return; }
        if (!emergencyReady)
        {
            ShowError("紧急停止热键尚未注册成功，为安全起见暂不允许开始回放，请先修复热键。");
            return;
        }
        // Hard preflight: an out-of-range coordinate blocks before any input is sent.
        try { if (!backgroundMode.Checked) PlaybackEngine.EnsureCoordinatesFit(loadedRecording); }
        catch (Exception ex) { ShowError(ex.Message); return; }
        // Non-fatal but explicit: a different desktop layout / DPI must not be hidden
        // behind a plain "validation passed" message — ask before replaying.
        int currentDpi = DeviceDpi > 0 ? DeviceDpi : 96;
        string? mismatch = backgroundMode.Checked ? null : PlaybackEngine.DescribeEnvironmentMismatch(loadedRecording, currentDpi);
        if (mismatch is not null &&
            MessageBox.Show(this, mismatch, "录制环境不一致", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning)
            != DialogResult.OK) return;
        try
        {
            var driver = BackgroundDriver(loadedRecording.WindowBind is not null);
            if (driver is BackgroundInputDriver background)
                foreach (var entry in loadedRecording.Events.Where(e => RecordingEventType.IsMouse(e.Type)))
                    background.ValidatePoint(entry.X, entry.Y);
            playback.Start(loadedRecording, SelectedSpeed(), SelectedLoops(), driver);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        SyncEnable(); RefreshStatus();
    }

    private void TogglePlayPause()
    {
        if (!playback.IsActive) return;
        if (playback.IsPaused) { playback.Resume(); logger.Write("继续回放，继续等待剩余时间。"); }
        else { playback.Pause(); logger.Write("已请求暂停，执行器到达暂停点后冻结剩余等待（不释放按住键）。"); }
        RefreshStatus();
    }

    // ---- P2 minimal macro ---------------------------------------------------
    private void EditMacro()
    {
        if (macro.IsActive) { ShowError("宏运行中不能编辑，请先停止。"); return; }
        using var form = new MacroEditorForm(loadedMacro, loadedMacroPath);
        form.ShowDialog(this);
        // AdoptPath is set ONLY while the document the editor currently holds has a
        // saved file the user's flow just confirmed: a successful save/autosave/save-as,
        // or a successful open. 新建 resets it (an unsaved new draft is never adopted),
        // and a cancelled or failed open keeps the state of the document still in the
        // editor — it can never carry over from a previous macro. So the runnable
        // version is replaced ONLY by the file the editor actually showed last.
        if (form.AdoptPath is not null) LoadMacroFrom(form.AdoptPath);
        else RefreshStatus();
    }

    private void LoadMacroFile()
    {
        if (macro.IsActive) { ShowError("宏运行中不能重新加载。"); return; }
        using var dlg = new OpenFileDialog
        {
            Title = "加载宏文件",
            Filter = "宏文件 (*.macro.json;*.json)|*.macro.json;*.json|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        LoadMacroFrom(dlg.FileName);
    }

    private void LoadMacroFrom(string path)
    {
        try
        {
            loadedMacro = macroStore.Load(path);
            loadedMacroPath = path;
            logger.Write($"已加载宏：{loadedMacro.Name}（{loadedMacro.Steps.Count} 个顶层动作，整体 {loadedMacro.Loops} 轮）。");
        }
        catch (Exception ex) { ShowError($"宏加载失败，未发送任何输入：{ex.Message}"); }
        RefreshStatus();
    }

    private void ToggleMacro()
    {
        if (macro.IsActive) { macro.Stop(); return; }
        if (scheduledStart is not null) { ShowError("请先取消定时启动。"); return; }
        // Mutual exclusion: macro / clicking / recording / standalone playback.
        if (engine.IsActive || playback.IsActive || recorder is not null)
        { ShowError("请先停止连点、录制或回放，再开始宏（四类任务互斥）。"); return; }
        if (loadedMacro is null) { ShowError("请先“编辑/新建宏”并保存，或“加载宏”。"); return; }
        if (!emergencyReady)
        { ShowError("紧急停止热键尚未注册成功，为安全起见暂不允许执行宏，请先修复热键。"); return; }

        // Whole-macro + every referenced recording is resolved/validated BEFORE input.
        Dictionary<string, RecordingDocument> refs;
        try
        {
            refs = macroStore.ResolveReferences(loadedMacro, loadedMacroPath, p => recordingStore.Load(p));
        }
        catch (Exception ex) { ShowError($"宏预检失败（缺文件/损坏/引用了宏），未发送任何输入：{ex.Message}"); return; }
        // Hard coordinate gate across macro steps and every referenced recording.
        try { MacroEngine.EnsureCoordinatesFit(loadedMacro, refs); }
        catch (Exception ex) { ShowError(ex.Message); return; }
        // Aggregated, non-fatal DPI / virtual-desktop layout mismatch: ask once; cancel
        // ends the whole macro with zero input (before any macro action runs).
        int macroDpi = DeviceDpi > 0 ? DeviceDpi : 96;
        string? macroMismatch = MacroEngine.DescribeEnvironmentMismatch(loadedMacro, refs, macroDpi);
        if (macroMismatch is not null &&
            MessageBox.Show(this, macroMismatch, "宏环境不一致", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning)
            != DialogResult.OK) return;

        // Foreground window binding: re-resolve the target for THIS run before starting.
        // Zero matches / multiple matches / minimized / changed client size or DPI all
        // end here with zero input; the resolved HWND is passed to the engine for this
        // run only (never persisted).
        WindowTargetInfo? boundTarget = null;
        if (loadedMacro.WindowBind is not null)
        {
            boundTarget = WindowTargetResolver.Resolve(loadedMacro.WindowBind, out string? bindError);
            if (boundTarget is null)
            {
                ShowError($"窗口绑定解析失败，未发送任何输入：{bindError}");
                return;
            }
            logger.Write($"窗口绑定目标已解析：{boundTarget.Describe()}。");
        }
        try { macro.Start(loadedMacro, refs, loadedMacroPath, boundTarget); }
        catch (Exception ex) { ShowError($"宏启动失败，未发送任何输入：{ex.Message}"); }
        SyncEnable(); RefreshStatus();
    }

    // Non-destructive: optimize a COPY and save it to a NEW file. The loaded
    // recording object is never mutated and the original file is never overwritten.
    private void OptimizeSaveAs()
    {
        if (AnyBusy) { ShowError("请先停止当前任务，再优化另存。"); return; }
        if (loadedRecording is null) { ShowError("请先录制或加载一个录制文件。"); return; }
        var source = loadedRecording;
        RecordingOptimizer.OptimizeResult optimized;
        int epsilon = thinMoves.Checked ? RecordingOptimizer.DefaultThinEpsilonPx : 0;
        try
        {
            optimized = RecordingOptimizer.Optimize(source, epsilon);
            RecordingStore.Validate(optimized.Document);
        }
        catch (Exception ex) { ShowError($"优化失败：{ex.Message}"); return; }

        // Preview / result comparison before anything is written (original plan:
        // 调整等待 / 抽稀 -> 预览 -> 另存为新录制). Cancelling here changes nothing.
        using (var preview = new OptimizePreviewForm(source, optimized))
        {
            if (preview.ShowDialog(this) != DialogResult.OK)
            {
                logger.Write("已取消优化另存（预览后未确认），原录制未改动。");
                return;
            }
        }

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseClicker", "recordings");
        Directory.CreateDirectory(dir);
        string baseName = loadedRecordingPath is null ? "recording" : Path.GetFileNameWithoutExtension(loadedRecordingPath);
        using var dialog = new SaveFileDialog
        {
            Title = "优化另存为（不覆盖原录制）",
            Filter = "录制文件 (*.json)|*.json",
            InitialDirectory = dir,
            FileName = $"{baseName}-optimized.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (loadedRecordingPath is not null &&
            string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(loadedRecordingPath),
                StringComparison.OrdinalIgnoreCase))
        {
            ShowError("优化另存必须选择一个新文件，不能覆盖原录制。");
            return;
        }

        try
        {
            recordingStore.Save(optimized.Document, dialog.FileName);
            // Activate the optimized copy for playback; the original file stays intact.
            loadedRecording = recordingStore.Load(dialog.FileName);
            loadedRecordingPath = dialog.FileName;
            logger.Write($"优化另存完成：{optimized.BeforeCount} → {optimized.AfterCount} 个动作"
                + $"（同坐标去重 {optimized.RemovedCount - optimized.ThinnedCount}，轨迹抽稀 {optimized.ThinnedCount}），原录制未改动。");
        }
        catch (Exception ex) { ShowError($"优化另存失败：{ex.Message}\n原录制保持不变。"); }
        RefreshStatus();
    }

    // Open the wait-only event editor. Disabled while running/paused by the enable
    // matrix; double-check here too. The editor works on a copy and only writes a NEW
    // file, so the loaded object / original file are untouched until the user chooses
    // to load the freshly saved copy.
    private void EditEventWaits()
    {
        if (AnyBusy || playback.IsPauseRequested || playback.IsPauseConfirmed)
        { ShowError("运行或暂停期间不能编辑，请先停止或等待回放结束。"); return; }
        if (loadedRecording is null) { ShowError("请先录制或加载一个录制文件。"); return; }

        using var editor = new RecordingEditorForm(loadedRecording, loadedRecordingPath);
        var result = editor.ShowDialog(this);
        if (result == DialogResult.OK && editor.LoadSavedAfterClose && !string.IsNullOrEmpty(editor.SavedPath))
        {
            try
            {
                loadedRecording = recordingStore.Load(editor.SavedPath);
                loadedRecordingPath = editor.SavedPath;
                logger.Write($"已加载编辑后的新文件：{editor.SavedPath}（原录制未改动）。");
            }
            catch (Exception ex) { ShowError($"新文件已保存，但加载失败：{ex.Message}"); }
        }
        RefreshStatus();
    }

    private double SelectedSpeed() => playSpeed.SelectedIndex switch { 0 => 0.5, 2 => 2.0, _ => 1.0 };
    private static readonly int[] LoopChoices = [1, 2, 3, 5, 10, PlaybackEngine.InfiniteLoops];
    private int SelectedLoops() => LoopChoices[Math.Clamp(playLoops.SelectedIndex, 0, LoopChoices.Length - 1)];

    private void OnLogEntry(string line)
    {
        if (!logList.IsHandleCreated) return;
        BeginInvoke(() =>
        {
            logList.Items.Add(line);
            while (logList.Items.Count > 500) logList.Items.RemoveAt(0);
            logList.TopIndex = logList.Items.Count - 1;
        });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == GlobalHotkeys.MessageId)
        {
            if ((int)m.WParam == GlobalHotkeys.EmergencyId)
            {
                CancelScheduledStart();
                // Emergency stop halts every automation path.
                engine.Stop();
                playback.Stop();
                macro.Stop();
                if (recorder is not null) BeginInvoke(StopRecordingAndSave);
            }
            else if (!capturingHotkey && hotkeys?.IsCurrentToggleId((int)m.WParam) == true) Toggle();
            else if(!capturingHotkey && recordingHotkeys.TryGetValue((int)m.WParam,out var path) && !AnyBusy && OwnedForms.Length==0)
                BeginInvoke(()=>LoadLibraryRecording(path,true));
        }
        base.WndProc(ref m);
    }

    private void InitializeLibrary()
    {
        try{library=new RecordingLibrary();RegisterLibraryHotkeys();}catch(Exception ex){ShowError($"录制库加载失败，文件保持原样：{ex.Message}");}
    }
    private void RegisterLibraryHotkeys()
    {
        hotkeys?.RemoveRecordings();recordingHotkeys.Clear();if(library is null || hotkeys is null)return;
        for(int i=0;i<library.Entries.Count;i++){var e=library.Entries[i];if(e.Hotkey==0)continue;int id=1000+i;
            if (!appliedToggle && triggerHook is not null && appliedCtrl && appliedAlt && !appliedShift && appliedVk == e.Hotkey)
            { logger.Write($"录制热键未生效：{Path.GetFileName(e.Path)}：与长按启停快捷键冲突。"); continue; }
            if(hotkeys.RegisterRecording(id,e.Hotkey,out var error))recordingHotkeys[id]=e.Path;else logger.Write($"录制热键未生效：{Path.GetFileName(e.Path)}：{error}");}
    }
    private void OpenLibrary()
    {
        if(AnyBusy){ShowError("请先停止当前任务。");return;}
        if(library is null){InitializeLibrary();if(library is null)return;}
        try{using var form=new RecordingLibraryForm(library);form.ShowDialog(this);RegisterLibraryHotkeys();if(form.SelectedPath is {} path)LoadLibraryRecording(path,form.RunSelected);}catch(Exception ex){ShowError(ex.Message);}
    }
    private void LoadLibraryRecording(string path,bool run)
    {
        if(AnyBusy)return;
        try{loadedRecording=recordingStore.Load(path);loadedRecordingPath=path;RefreshStatus();if(run)TogglePlayback();}catch(Exception ex){ShowError(ex.Message);}
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (canClose) return;
        CancelScheduledStart();
        engine.Stop();
        playback.Stop();
        macro.Stop();
        bool hide = !exiting && e.CloseReason == CloseReason.UserClosing;
        e.Cancel = true;
        if (!SaveChangesOnClose(e.CloseReason, hide))
        {
            exiting = false;
            return;
        }
        if (hide)
        {
            Hide();
            return;
        }
        exiting = true;
        Enabled = false;
        emergencyReady = false;
        logger.EntryAdded -= OnLogEntry;
        hotkeys?.Dispose();
        triggerHook?.Dispose();
        recorder?.Dispose();
        await engine.StopAsync();
        await playback.StopAsync();
        await macro.StopAsync();
        refresh.Stop();
        refresh.Dispose();
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose();
        canClose = true;
        Close();
    }

    private void ShowError(string message) => MessageBox.Show(this, message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static ComboBox Combo(params string[] items)
    {
        var combo = new DarkComboBox { Width = 172 };
        combo.Items.AddRange(items);
        return combo;
    }

    private static NumericUpDown Number(int min, int max, int value)
        => new() { Minimum = min, Maximum = max, Value = value, Width = 72 };

    private static PageView CreatePage(string title, string subtitle)
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill, BackColor = CanvasColor, Name = $"page_{title}"
        };
        var header = new Panel
        {
            Dock = DockStyle.Top, Height = 28, BackColor = CanvasColor,
            Padding = new Padding(12, 0, 12, 0), Name = "pageHeader"
        };
        var headingLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = CanvasColor,
            Margin = Padding.Empty, Padding = Padding.Empty, Tag = "canvas"
        };
        headingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        headingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new Label
        {
            Name = "pageTitle", Text = title, Dock = DockStyle.Fill, AutoSize = false,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ForeColor = InkColor,
            TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
        };
        headingLayout.Controls.Add(heading, 0, 0);
        header.Controls.Add(headingLayout);
        var content = new Panel
        {
            Name = "pageContent", Dock = DockStyle.Fill, AutoScroll = true,
            BackColor = CanvasColor, Padding = new Padding(6, 1, 6, 4)
        };
        page.Controls.Add(content);
        page.Controls.Add(header);
        return new PageView(page, content);
    }

    private static TableLayoutPanel CreateVerticalLayout(int rows, string name)
    {
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Size = Size.Empty,
            Name = name, ColumnCount = 1, RowCount = rows,
            BackColor = CanvasColor, Margin = Padding.Empty, Padding = Padding.Empty, Tag = "canvas"
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < stack.RowCount; row++)
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return stack;
    }

    private static TableLayoutPanel CreateCardStack(int rows) => CreateVerticalLayout(rows, "cardStack");

    private static TableLayoutPanel CreateTwoColumnLayout(float leftPercent, float rightPercent, string name)
    {
        var layout = new TableLayoutPanel
        {
            Name = name, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Size = Size.Empty,
            ColumnCount = 2, RowCount = 1, BackColor = CanvasColor, Margin = Padding.Empty,
            Padding = Padding.Empty, Tag = "canvas"
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, leftPercent));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, rightPercent));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return layout;
    }

    private static FlowLayoutPanel NewFlowRow() => new()
    {
        Name = "contentRow", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
        Margin = new Padding(0, 3, 0, 3), Padding = Padding.Empty, BackColor = SurfaceColor
    };

    private static FlowLayoutPanel Group(string title, TableLayoutPanel parent, int column, int row)
    {
        var card = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Size = Size.Empty,
            ColumnCount = 1, RowCount = 2, BackColor = SurfaceColor,
            Padding = new Padding(4), Margin = new Padding(4), CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = new Label
        {
            Text = title, Name = "sectionHeader", AutoSize = false, Height = 26,
            Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft,
            BackColor = SurfaceColor, ForeColor = HeaderTextColor, Font = SectionFont,
            Padding = Padding.Empty, Margin = new Padding(2, 0, 2, 5)
        };
        var flow = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Size = Size.Empty, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(4), Margin = Padding.Empty, BackColor = SurfaceColor,
            Dock = DockStyle.Top
        };
        if (!string.IsNullOrEmpty(title)) card.Controls.Add(heading, 0, 0);
        else heading.Dispose();
        card.Controls.Add(flow, 0, 1);
        parent.Controls.Add(card, column, row);
        return flow;
    }

    private static FlowLayoutPanel AddRow(FlowLayoutPanel parent, string label, params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 4, 0, 4), Padding = Padding.Empty,
            BackColor = SurfaceColor
        };
        row.Controls.Add(new Label
        {
            Text = label, Width = 86, AutoSize = false, Height = 32,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = MutedColor, BackColor = SurfaceColor
        });
        row.Controls.AddRange(controls);
        parent.Controls.Add(row);
        return row;
    }

    private void ApplyVisualStyle()
    {
        foreach (var control in Descendants(this))
        {
            if (control is not GroupBox && control.Name is not ("pageTitle" or "pageSubtitle" or "sidebarBrand"))
                control.Font = Font;

            switch (control)
            {
                case GroupBox group:
                    group.BackColor = SurfaceColor;
                    group.ForeColor = InkColor;
                    group.Font = SectionFont;
                    break;
                case FlowLayoutPanel flow:
                    if (flow.Name == "navigationTabs") break;
                    flow.BackColor = flow.Name == "clickActionBand" ? ActionBandColor : SurfaceColor;
                    flow.Margin = new Padding(2);
                    break;
                case TableLayoutPanel table:
                    table.BackColor = Equals(table.Tag, "canvas") ? CanvasColor : SurfaceColor;
                    break;
                case Button button:
                    if (!button.Name.StartsWith("nav_", StringComparison.Ordinal))
                        InitializeButtonStyle(button);
                    break;
                case ComboBox combo:
                    combo.BackColor = SurfaceColor;
                    combo.ForeColor = InkColor;
                    break;
                case NumericUpDown number:
                    number.BackColor = SurfaceColor;
                    number.ForeColor = InkColor;
                    number.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ListBox list:
                    list.BackColor = SurfaceColor;
                    list.ForeColor = InkColor;
                    list.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case CheckBox check:
                    check.BackColor = SurfaceColor;
                    check.ForeColor = InkColor;
                    check.FlatStyle = FlatStyle.Flat;
                    check.FlatAppearance.BorderColor = BorderColor;
                    check.FlatAppearance.CheckedBackColor = PrimaryColor;
                    check.FlatAppearance.MouseOverBackColor = HeaderColor;
                    break;
                case Label label:
                    if (label.Name is "sidebarBrand")
                    {
                        label.ForeColor = Color.White;
                    }
                    else if (label.Name is "pageTitle")
                    {
                        label.ForeColor = InkColor;
                    }
                    else if (label.Name is "pageSubtitle")
                    {
                        label.ForeColor = MutedColor;
                    }
                    else if (label.Name == "sectionHeader")
                    {
                        label.ForeColor = HeaderTextColor;
                        label.Font = SectionFont;
                    }
                    else
                        label.ForeColor = MutedColor;
                    break;
            }
        }

        status.ForeColor = InkColor;
        estimate.ForeColor = MutedColor;
        cursor.ForeColor = MutedColor;
        hotkeyStatus.ForeColor = MutedColor;
        recordInfo.ForeColor = MutedColor;
        macroInfo.ForeColor = MutedColor;
        ApplyActionButtonStyles();
        ApplyButtonStateStyle(save);
    }

    private void ApplyActionButtonStyles()
    {
        ApplyButtonStateStyle(start);
        ApplyButtonStateStyle(recordButton);
        ApplyButtonStateStyle(playButton);
        ApplyButtonStateStyle(playPause);
        ApplyButtonStateStyle(editMacro);
        ApplyButtonStateStyle(runMacro);
        ApplyButtonStateStyle(stopCurrent);
    }

    private void ApplyButtonStateStyle(Button button)
    {
        var (fill, emphasized) = button switch
        {
            _ when button == start => (engine.IsActive ? DangerColor : PrimaryColor, true),
            _ when button == recordButton => (recorder is not null ? DangerColor : PrimaryColor, recorder is not null || loadedRecording is null),
            _ when button == playButton => (playback.IsActive ? DangerColor : PrimaryColor, playback.IsActive || loadedRecording is not null),
            _ when button == playPause => (playback.IsActive ? WarningColor : PrimaryColor, playback.IsActive),
            _ when button == editMacro => (PrimaryColor, loadedMacro is null),
            _ when button == runMacro => (macro.IsActive ? DangerColor : PrimaryColor, macro.IsActive || loadedMacro is not null),
            _ when button == stopCurrent => (DangerColor, button.Enabled),
            _ when button == save => (PrimaryColor, true),
            _ => (PrimaryColor, false)
        };
        StyleButton(button, fill, emphasized);
    }

    private static void InitializeButtonStyle(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.MinimumSize = new Size(0, 29);
        button.Margin = new Padding(4, 3, 4, 3);
        StyleButton(button, PrimaryColor, false);
    }

    private static void StyleButton(Button button, Color fill, bool emphasized)
    {
        button.BackColor = !button.Enabled ? DisabledSurfaceColor : emphasized ? fill : SurfaceColor;
        button.ForeColor = !button.Enabled ? MutedColor : emphasized ? Color.White : InkColor;
        var flat = button.FlatAppearance;
        int borderSize = emphasized && button.Enabled ? 0 : 1;
        Color border = emphasized && button.Enabled ? fill : BorderColor;
        Color hover = emphasized ? ControlPaint.Light(fill, 0.12f) : HeaderColor;
        Color pressed = emphasized ? ControlPaint.Dark(fill, 0.08f) : BorderColor;
        if (flat.BorderSize != borderSize) flat.BorderSize = borderSize;
        if (flat.BorderColor != border) flat.BorderColor = border;
        if (flat.MouseOverBackColor != hover) flat.MouseOverBackColor = hover;
        if (flat.MouseDownBackColor != pressed) flat.MouseDownBackColor = pressed;
    }

    private static void ApplyEnabledControlStyle(Control control)
    {
            switch (control)
            {
                case ComboBox combo:
                    combo.BackColor = combo.Enabled ? SurfaceColor : DisabledSurfaceColor;
                    combo.ForeColor = combo.Enabled ? InkColor : MutedColor;
                    break;
                case NumericUpDown number:
                    number.BackColor = number.Enabled ? SurfaceColor : DisabledSurfaceColor;
                    number.ForeColor = number.Enabled ? InkColor : MutedColor;
                    break;
                case ListBox list:
                    list.BackColor = list.Enabled ? SurfaceColor : DisabledSurfaceColor;
                    list.ForeColor = list.Enabled ? InkColor : MutedColor;
                    break;
                case CheckBox check:
                    check.BackColor = check.Enabled ? SurfaceColor : DisabledSurfaceColor;
                    check.ForeColor = check.Enabled ? InkColor : MutedColor;
                    break;
            }
    }

    private static void ApplyRoundedNavigationRegion(Control control)
    {
        var bounds = control.ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        int diameter = Math.Max(12, (int)Math.Round(20f * control.DeviceDpi / 96f));
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        control.Region?.Dispose();
        control.Region = new Region(path);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }
}
