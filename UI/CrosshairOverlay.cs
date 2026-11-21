namespace MouseClicker.UI;

// Fullscreen translucent layer for picking a point: shows live physical pixel
// coordinates, left-click confirms, Esc or right-click cancels.
public sealed class CrosshairOverlay : Form
{
    public event Action<int, int>? Picked;

    private readonly Label info = new()
    {
        // Child controls reject translucent back colors; use opaque dark instead.
        AutoSize = true, BackColor = Color.FromArgb(25, 25, 25), ForeColor = Color.White,
        Padding = new Padding(8), Font = new Font("Microsoft YaHei UI", 10)
    };

    public CrosshairOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        var bounds = SystemInformation.VirtualScreen;
        Bounds = new Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        // Translucency comes from the form-level Opacity; BackColor must stay opaque.
        BackColor = Color.Black;
        Opacity = 0.35;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        Controls.Add(info);
        MouseMove += (_, _) =>
        {
            var p = Cursor.Position;
            info.Text = $"X = {p.X}，Y = {p.Y}　（左键取点，Esc / 右键取消）";
            info.Location = new Point(p.X - Left + 18, p.Y - Top + 18);
        };
        MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                var p = Cursor.Position;
                Picked?.Invoke(p.X, p.Y);
            }
            Close();
        };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }
}
