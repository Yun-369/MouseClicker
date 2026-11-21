namespace MouseClicker.UI;

internal sealed class ScheduleDialog : Form
{
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly NumericUpDown seconds = new() { Minimum = 1, Maximum = 86400, Value = 10, Width = 100 };
    private readonly DateTimePicker date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", ShowUpDown = true, Width = 190 };
    public DateTimeOffset Due { get; private set; }

    public ScheduleDialog()
    {
        Text = "定时启动"; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 190); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        mode.Items.AddRange(["延迟秒数", "指定本地时间"]); mode.SelectedIndex = 0;
        date.Value = DateTime.Now.AddMinutes(1);
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12), WrapContents = false };
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange([seconds, new Label { Text = "秒", AutoSize = true, Margin = new Padding(5, 7, 0, 0) }, date]);
        var ok = new Button { Text = "确认定时", AutoSize = true };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([ok, cancel]);
        content.Controls.AddRange([mode, row, buttons]); Controls.Add(content);
        void UpdateMode()
        {
            seconds.Visible = row.Controls[1].Visible = mode.SelectedIndex == 0;
            date.Visible = mode.SelectedIndex == 1;
        }
        mode.SelectedIndexChanged += (_, _) => UpdateMode();
        UpdateMode();
        ok.Click += (_, _) =>
        {
            Due = mode.SelectedIndex == 0 ? DateTimeOffset.Now.AddSeconds((double)seconds.Value) : new DateTimeOffset(date.Value);
            if (Due <= DateTimeOffset.Now) { MessageBox.Show(this, "请选择未来的时间。"); return; }
            DialogResult = DialogResult.OK; Close();
        };
    }
}
