using MouseClicker.Domain;
using MouseClicker.Platform;

namespace MouseClicker.UI;

// Pick a visible top-level window to bind a macro to. The result records identity
// (process name + title rule + class name) and the client size/DPI AT BINDING TIME —
// deliberately simple rules, no regex, and the HWND is used for this dialog only
// (never saved). Coordinates recorded afterwards are CLIENT-AREA relative.
public sealed class WindowPickerForm : Form
{
    public WindowBinding? Result { get; private set; }

    private readonly ListView list = new()
    {
        View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
        Dock = DockStyle.Fill
    };
    private readonly ComboBox ruleBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, DropDownWidth = 200 };
    private readonly CheckBox matchClassChk = new()
    {
        Text = "同时校验类名（进程重启后 WinForms 类名会变化，跨会话识别建议不勾）",
        AutoSize = true, ForeColor = Color.DimGray
    };

    public WindowPickerForm()
    {
        Text = "选择要绑定的前台窗口";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 480);
        Size = new Size(820, 560);

        list.Columns.Add("进程", 110);
        list.Columns.Add("窗口标题", 320);
        list.Columns.Add("类名", 180);
        list.Columns.Add("客户区", 110);
        list.Name = "windowList";

        ruleBox.Items.AddRange(["完全等于", "包含", "以…开头", "以…结尾"]);
        ruleBox.SelectedIndex = 0;
        ruleBox.Name = "ruleBox";

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 4) };
        top.Controls.Add(new Label { Text = "标题匹配规则：", AutoSize = true, Anchor = AnchorStyles.Left });
        top.Controls.Add(ruleBox);
        top.Controls.Add(new Label
        {
            Text = "（绑定只保存进程名+标题规则+类名，不保存窗口句柄；每次运行时重新查找目标）",
            AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left, Margin = new Padding(12, 6, 0, 0)
        });
        top.Controls.Add(matchClassChk);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "绑定所选窗口", AutoSize = true, DialogResult = DialogResult.None };
        ok.Click += (_, _) => Confirm();
        bottom.Controls.Add(cancel); bottom.Controls.Add(ok);

        var note = new Label
        {
            Text = "绑定后，宏的移动/单击将使用该窗口的客户区坐标；运行时要求窗口可见、未最小化、客户区尺寸与 DPI 和绑定时一致。",
            Dock = DockStyle.Bottom, AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8, 2, 8, 4)
        };

        Controls.Add(list);
        Controls.Add(top);
        Controls.Add(note);
        Controls.Add(bottom);
        list.BringToFront();

        foreach (var w in WindowTargetResolver.EnumerateVisibleTopLevels())
        {
            var item = new ListViewItem(w.ProcessName);
            item.SubItems.Add(w.Title);
            item.SubItems.Add(w.ClassName);
            item.SubItems.Add($"{w.ClientWidth}×{w.ClientHeight}");
            item.Tag = w;
            list.Items.Add(item);
        }
        list.DoubleClick += (_, _) => Confirm();
    }

    private void Confirm()
    {
        if (list.SelectedItems.Count == 0)
        {
            MessageBox.Show(this, "请先在列表中选择一个窗口。", "选择目标窗口");
            return;
        }
        if (list.SelectedItems[0].Tag is not WindowTargetInfo w)
        {
            MessageBox.Show(this, "所选行无效。", "选择目标窗口");
            return;
        }
        string rule = ruleBox.SelectedIndex switch
        {
            1 => WindowTitleRule.Contains,
            2 => WindowTitleRule.StartsWith,
            3 => WindowTitleRule.EndsWith,
            _ => WindowTitleRule.EqualsText
        };
        Result = new WindowBinding
        {
            ProcessName = w.ProcessName,
            TitleRule = rule,
            TitleText = w.Title,
            ClassName = w.ClassName,
            MatchClass = matchClassChk.Checked,
            ClientWidth = w.ClientWidth,
            ClientHeight = w.ClientHeight,
            Dpi = w.Dpi
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
