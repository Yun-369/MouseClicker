using MouseClicker.Domain;
using MouseClicker.Storage;

namespace MouseClicker.UI;

// Event editor showing index / type / gap / cumulative time / detail. Event fields,
// pair operations, binding conversion and waits are edited on a working copy; each
// change rebuilds and validates the timeline. Saving always writes a NEW file
// ("另存"); the loaded recording and the original file are never overwritten.
public sealed class RecordingEditorForm : Form
{
    private readonly RecordingDocument original;
    private readonly string? originalPath;
    private RecordingDocument draft;
    private readonly ListView list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        GridLines = true,
        Dock = DockStyle.Fill
    };
    private readonly NumericUpDown delayBox = new()
    {
        Minimum = 0, Maximum = RecordingSchema.MaxDelayMs, Increment = 10, Width = 110
    };
    private readonly Label totalLabel = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly Label hintLabel = new()
    {
        Text = "编辑结果另存；成对操作以“一次按住”为单位（含自动重复的按下与配对松开）。绑定转换前请把窗口放回录制时位置。",
        AutoSize = true, ForeColor = Color.DimGray
    };
    private bool dirty;

    public string? SavedPath { get; private set; }
    public bool LoadSavedAfterClose { get; private set; }

    public RecordingEditorForm(RecordingDocument document, string? originalPath = null)
    {
        original = document ?? throw new ArgumentNullException(nameof(document));
        this.originalPath = originalPath;
        draft = document;
        Text = "录制事件编辑（另存为新文件）";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 460);
        Size = new Size(720, 540);

        list.Columns.Add("序号", 56);
        list.Columns.Add("类型", 96);
        list.Columns.Add("相邻等待(ms)", 104);
        list.Columns.Add("累计(ms)", 92);
        list.Columns.Add("坐标 / 按键 / 滚轮", 320);
        list.SelectedIndexChanged += (_, _) => LoadSelectedIntoEditor();

        var editorBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 6, 8, 6), WrapContents = true
        };
        editorBar.Controls.Add(new Label { Text = "选中事件等待(ms)：", AutoSize = true, Anchor = AnchorStyles.Left });
        editorBar.Controls.Add(delayBox);
        var apply = new Button { Text = "应用并重新计算", AutoSize = true };
        apply.Click += (_, _) => ApplyDelay();
        editorBar.Controls.Add(apply);
        editorBar.Controls.Add(hintLabel);
        AddEditButton(editorBar,"编辑事件",()=>EditEvent(false,false));
        AddEditButton(editorBar,"插入事件",()=>EditEvent(true,false));
        AddEditButton(editorBar,"插入按键对",()=>EditEvent(true,true));
        AddEditButton(editorBar,"删除事件",()=>DeleteEvent(false));
        AddEditButton(editorBar,"删除按键对",()=>DeleteEvent(true));
        AddEditButton(editorBar,"绑定并转换坐标",BindRecording);
        AddEditButton(editorBar,"解除绑定转屏幕坐标",UnbindRecording);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 84 };
        var totals = new Panel { Dock = DockStyle.Top, Height = 28 };
        totalLabel.SetBounds(10, 4, 680, 22);
        totals.Controls.Add(totalLabel);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
            Padding = new Padding(8)
        };
        var close = new Button { Text = "关闭", AutoSize = true, DialogResult = DialogResult.Cancel };
        var saveAs = new Button { Text = "另存为新文件…", AutoSize = true };
        saveAs.Click += (_, _) => SaveAsNew();
        buttons.Controls.Add(close);
        buttons.Controls.Add(saveAs);

        bottom.Controls.Add(totals);
        bottom.Controls.Add(buttons);

        Controls.Add(list);
        Controls.Add(editorBar);
        Controls.Add(bottom);
        CancelButton = close;
        FormClosing += HandleFormClosing;

        Reload();
    }

    private void Reload()
    {
        list.BeginUpdate();
        list.Items.Clear();
        for (int i = 0; i < draft.Events.Count; i++)
        {
            var e = draft.Events[i];
            var item = new ListViewItem((i + 1).ToString());
            item.SubItems.Add(DescribeType(e.Type));            item.SubItems.Add(e.DelayMs.ToString());
            item.SubItems.Add(e.TimeMs.ToString());
            item.SubItems.Add(DescribeDetail(e));
            list.Items.Add(item);
        }
        list.EndUpdate();
        RefreshTotals();
        if (list.Items.Count > 0) list.Items[0].Selected = true;
    }

    private static void AddEditButton(Control panel,string text,Action action){var b=new Button(){Text=text,AutoSize=true};b.Click+=(_,_)=>action();panel.Controls.Add(b);}
    private void EditEvent(bool insert,bool paired)
    {
        int index=list.SelectedIndices.Count>0?list.SelectedIndices[0]:draft.Events.Count;
        if(!insert && index>=draft.Events.Count)return;
        var value=insert?new RecordedEvent(){Type=paired?RecordingEventType.KeyDown:RecordingEventType.MouseMove,Vk=0x41,DelayMs=100,Button="left"}:draft.Events[index];
        using var dialog=new RecordedEventDialog(value);if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try
        {
            if(insert)
            {
                draft=RecordingEditor.Insert(draft,index,dialog.Result,paired);
            }
            else
            {
                var original=draft.Events[index];
                int partnerIndex=RecordingEditor.FindPairIndex(draft,index);
                bool sameKind=dialog.Result.Type==original.Type;
                bool syncPair=false;
                if(partnerIndex>=0 && sameKind)
                {
                    // The replacement keeps the pair kind: the other members of the hold
                    // group CAN be updated together, so offer it explicitly.
                    var other=draft.Events[partnerIndex];
                    string otherLabel=other.Type==original.Type
                        ?"同一次按住里还有自动重复的按下事件"
                        :"有配对的"+DescribeType(other.Type)+"事件";
                    var choice=MessageBox.Show(this,
                        $"选中的是{DescribeType(original.Type)}事件，{otherLabel}。\n\n"
                        +"是：同步更新配对事件的按键/按钮，保持一次按住前后一致；\n"
                        +"否：只修改当前事件（配对可能不一致）；\n"
                        +"取消：放弃修改。",
                        "成对修改",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
                    if(choice==DialogResult.Cancel)return;
                    syncPair=choice==DialogResult.Yes;
                }
                else if(partnerIndex>=0)
                {
                    // Family/direction change (or move/wheel): the pair CANNOT be kept,
                    // so do not offer a "sync" that could not work - say what happens.
                    var choice=MessageBox.Show(this,
                        $"这次修改会把事件类型改为{DescribeType(dialog.Result.Type)}，原有的配对（{DescribeType(draft.Events[partnerIndex].Type)}）将不再成立。\n\n"
                        +"确定：只修改当前事件；\n取消：放弃修改。",
                        "成对修改",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning);
                    if(choice!=DialogResult.OK)return;
                }
                if(syncPair)
                {
                    var result=RecordingEditor.ReplacePair(draft,index,dialog.Result);
                    draft=result.Document;
                    if(!result.Paired)
                        MessageBox.Show(this,"配对未能同步，已只修改当前事件。","成对修改",MessageBoxButtons.OK,MessageBoxIcon.Information);
                }
                else draft=RecordingEditor.Replace(draft,index,dialog.Result);
            }
            dirty=true;Reload();
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message);}
    }
    private void DeleteEvent(bool paired)
    {
        if(list.SelectedIndices.Count==0)return;
        try
        {
            int index=list.SelectedIndices[0];
            if(paired)
            {
                var group=RecordingEditor.FindHoldGroup(draft,index);
                bool hasDown=group.Any(i=>draft.Events[i].Type is RecordingEventType.KeyDown or RecordingEventType.MouseDown);
                bool hasUp=group.Any(i=>draft.Events[i].Type is RecordingEventType.KeyUp or RecordingEventType.MouseUp);
                if(hasDown && hasUp && group.Count>2 &&
                    MessageBox.Show(this,
                        $"这次按住包含 {group.Count} 条事件（自动重复的按下 + 松开），成对删除会一起删除。继续？",
                        "删除事件",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)
                    return;
            }
            var result=RecordingEditor.Delete(draft,index,paired);
            draft=result.Document;dirty=true;Reload();
            if(paired && !result.Paired)
                MessageBox.Show(this,"未找到配对的按下/松开事件，已只删除当前事件。","删除事件",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message);}
    }
    private void BindRecording()
    {
        using var picker=new WindowPickerForm();if(picker.ShowDialog(this)!=DialogResult.OK || picker.Result is null)return;
        if(MessageBox.Show(this,"将按目标窗口当前位置转换原录制的屏幕坐标。请确认它与录制时的位置一致。原文件不会被改写。","转换坐标",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        try{var target=Platform.WindowTargetResolver.Resolve(picker.Result,out var error);if(target is null)throw new InvalidDataException(error);draft=RecordingEditor.BindToWindow(draft,target,picker.Result);dirty=true;Reload();}catch(Exception ex){MessageBox.Show(this,ex.Message);}
    }
    private void UnbindRecording()
    {
        if(draft.WindowBind is null)return;
        try{var target=Platform.WindowTargetResolver.Resolve(draft.WindowBind,out var error);if(target is null)throw new InvalidDataException(error);draft=RecordingEditor.Unbind(draft,target);dirty=true;Reload();}catch(Exception ex){MessageBox.Show(this,ex.Message);}
    }

    private void RefreshTotals()
    {
        int before = RecordingEditor.TotalDurationMs(original);
        int after = RecordingEditor.TotalDurationMs(draft);
        int delta = after - before;
        string deltaText = delta == 0 ? "无变化" : (delta > 0 ? $"+{delta}" : delta.ToString());
        totalLabel.Text = $"总时长：{before} → {after} ms（{deltaText} ms）；动作数 {draft.Events.Count}；{(draft.WindowBind is null ? "屏幕坐标" : "绑定客户区坐标")}";
    }

    private void LoadSelectedIntoEditor()
    {
        if (list.SelectedIndices.Count == 0) return;
        int i = list.SelectedIndices[0];
        delayBox.Value = Math.Clamp(draft.Events[i].DelayMs, 0, RecordingSchema.MaxDelayMs);
    }

    private void ApplyDelay()
    {
        if (list.SelectedIndices.Count == 0) { MessageBox.Show("请先在列表中选择一个动作。"); return; }
        int i = list.SelectedIndices[0];
        try
        {
            draft = RecordingEditor.SetDelay(draft, i, (int)delayBox.Value);
            dirty = true;
            Reload();
            if (i < list.Items.Count) list.Items[i].Selected = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"修改无效，已保持上一版：{ex.Message}", "等待编辑",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveAsNew()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "把编辑后的录制另存为新文件",
            Filter = "录制文件 (*.json)|*.json",
            FileName = SuggestName(original.Name)
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return; // cancelled: keep draft & original
        if (!string.IsNullOrEmpty(originalPath) &&
            string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(originalPath),
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "编辑结果必须另存为新文件，不能覆盖原录制。请换个文件名。",
                "另存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            // Prepare (validate + exact UTF-8 size) runs before any write; Save is atomic.
            new RecordingStore().Save(draft, dlg.FileName);
            SavedPath = dlg.FileName;
            dirty = false;
            var use = MessageBox.Show(this,
                $"已另存为新文件：\n{dlg.FileName}\n\n原录制对象与原文件均未改动。是否立即加载这个新文件用于回放？",
                "另存成功", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            LoadSavedAfterClose = use == DialogResult.Yes;
            if (LoadSavedAfterClose) { DialogResult = DialogResult.OK; Close(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败，未覆盖任何文件，内存中的编辑仍保留：{ex.Message}",
                "另存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!dirty || LoadSavedAfterClose) return;
        var choice = MessageBox.Show(this,
            "当前录制还有未另存的编辑，是否先另存为新文件？",
            "未保存的编辑", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel)
        {
            e.Cancel = true;
            return;
        }
        if (choice == DialogResult.Yes)
        {
            e.Cancel = true;
            SaveAsNew();
            return;
        }
        dirty = false;
    }

    private static string SuggestName(string name)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return $"{name}-编辑-{stamp}.json";
    }

    private static string DescribeType(string t) => t switch
    {
        var x when x == RecordingEventType.MouseMove => "鼠标移动",
        var x when x == RecordingEventType.MouseDown => "鼠标按下",
        var x when x == RecordingEventType.MouseUp => "鼠标松开",
        var x when x == RecordingEventType.MouseWheel => "滚轮",
        var x when x == RecordingEventType.KeyDown => "键盘按下",
        var x when x == RecordingEventType.KeyUp => "键盘松开",
        _ => t
    };

    private static string DescribeDetail(RecordedEvent e)
    {
        switch (e.Type)
        {
            case var t when t == RecordingEventType.MouseMove:
                return $"({e.X},{e.Y})";
            case var t when t is RecordingEventType.MouseDown or RecordingEventType.MouseUp:
                return $"({e.X},{e.Y})　键：{e.Button ?? "?"}{(e.Extended ? "　扩展" : "")}";
            case var t when t == RecordingEventType.MouseWheel:
                return $"({e.X},{e.Y})　Delta={e.Delta}";
            case var t when t is RecordingEventType.KeyDown or RecordingEventType.KeyUp:
                string key = ((Keys)e.Vk).ToString();
                return $"VK=0x{e.Vk:X2}（{key}）　扫描码=0x{e.Scan:X2}{(e.Extended ? "　扩展键" : "")}";
            default:
                return "";
        }
    }
}
