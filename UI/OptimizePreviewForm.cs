using MouseClicker.Domain;
using MouseClicker.Storage;

namespace MouseClicker.UI;

// 优化另存前的预览/结果对比窗口（原始方案：调整等待时间 / 按距离抽稀移动点 → 预览 →
// 另存为新录制）。这里只比较内存中的结果：原录制对象与原文件都不会被改动，确认后才
// 进入“另存为”。能力说明：显示动作数与总时长的前后对比、去重与抽稀各自移除了多少、
// 以及被移除移动点的样本；不提供逐帧轨迹图形预览，也不做平滑/插值。
public sealed class OptimizePreviewForm : Form
{
    public OptimizePreviewForm(RecordingDocument source, RecordingOptimizer.OptimizeResult result)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(result);
        Text = "优化预览（结果对比，另存前确认）";
        Size = new Size(760, 560);
        MinimumSize = new Size(640, 460);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        int before = RecordingEditor.TotalDurationMs(source);
        int after = RecordingEditor.TotalDurationMs(result.Document);
        int dedup = result.RemovedCount - result.ThinnedCount;
        int delta = after - before;
        string deltaText = delta == 0 ? "无变化" : (delta > 0 ? $"+{delta}" : delta.ToString());
        var removed = RecordingOptimizer.RemovedEvents(source, result.Document);

        var summary = new Label
        {
            Dock = DockStyle.Top, AutoSize = false, Height = 96, Name = "previewSummary",
            Padding = new Padding(12, 10, 12, 6), Font = new Font("Microsoft YaHei UI", 9.5f),
            Text = $"动作数：{result.BeforeCount} → {result.AfterCount}"
                + $"（移除 {result.RemovedCount}：同坐标去重 {dedup}，RDP 轨迹抽稀 {result.ThinnedCount}）\n"
                + $"总时长：{before} → {after} ms（{deltaText}）\n"
                + "按住拖拽期间的移动一律原样保留；每个保留动作的发生时间与总时长不变。\n"
                + "确认后另存为新文件；原录制对象与原文件不会被修改。"
        };

        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, Name = "removedList" };
        list.Columns.Add("被移除的移动点", 150);
        list.Columns.Add("时间(ms)", 100);
        list.Columns.Add("坐标", 160);
        const int MaxRows = 200;
        foreach (var e in removed.Take(MaxRows))
        {
            var item = new ListViewItem("mouse_move");
            item.SubItems.Add(e.TimeMs.ToString());
            item.SubItems.Add($"({e.X},{e.Y})");
            list.Items.Add(item);
        }
        if (removed.Count > MaxRows) list.Items.Add(new ListViewItem($"…其余 {removed.Count - MaxRows} 个未列出") { ForeColor = Color.DimGray });
        if (removed.Count == 0) list.Items.Add(new ListViewItem("（本次没有移除任何移动点）") { ForeColor = Color.DimGray });

        var note = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = 26, ForeColor = Color.DimGray,
            Padding = new Padding(12, 4, 12, 0),
            Text = $"移除明细：共 {removed.Count} 个移动点（最多列出 {MaxRows} 个）。抽稀容差由主界面的“轨迹抽稀”勾选决定。"
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel, Name = "cancelButton" };
        var ok = new Button { Text = "继续另存…", AutoSize = true, DialogResult = DialogResult.OK, Name = "okButton" };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(list);
        Controls.Add(note);
        Controls.Add(summary);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
