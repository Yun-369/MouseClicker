using System.ComponentModel;
using System.Drawing.Design;
using System.Runtime.InteropServices;
using System.Windows.Forms.Design;
using MouseClicker.Domain;
using MouseClicker.Engine;
using MouseClicker.Platform;
using MouseClicker.Storage;
namespace MouseClicker.UI;

public sealed class MacroEditorForm : Form
{
    private MacroDocument draft;
    private string? filePath;
    public string? SavedPath { get; private set; }
    public string? AdoptPath { get; private set; }
    private readonly TextBox nameBox=new(){Width=180,Name="nameBox"};
    private readonly NumericUpDown loopsBox=new(){Minimum=1,Maximum=MacroSchema.MaxLoopTimes,Width=90,Name="loopsBox"};
    private readonly ListView top=MakeList("topList"), child=MakeList("childList");
    private readonly PropertyGrid properties=new(){Dock=DockStyle.Fill,HelpVisible=true,ToolbarVisible=false,Name="stepGrid"};
    private readonly Label binding=new(){AutoSize=true,MaximumSize=new Size(900,0),Name="bindStatus"};
    // Save outcomes live on their OWN label so a save failure can never overwrite the
    // window-binding description (and vice versa) - both stay readable at all times.
    private readonly Label status=new(){AutoSize=true,MaximumSize=new Size(900,0),Name="saveStatus",ForeColor=Color.DimGray};
    private ListView active;
    private MacroStep? selectedLoop;
    private bool loading,dirty;
    private string? lastSaveError;   // reason of the most recent failed save on THIS document
    // Deep copy of the last KNOWN-GOOD document state (successful load or save).
    // Never shared with the editable draft. Used by 放弃修改 when the on-disk file
    // itself cannot be read anymore (deleted/corrupted): the recovery then restores
    // this snapshot in memory and DETACHES the document from the invalid file.
    private MacroDocument? lastGood;
    private readonly GridCommitFilter commitFilter;
    private static ListView MakeList(string name)=>new(){Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,HideSelection=false,MultiSelect=true,AllowDrop=true,Name=name};
    public MacroEditorForm(MacroDocument? document=null,string? path=null)
    {
        draft=document?.Clone() ?? MacroDocument.CreateNew();filePath=path;SavedPath=path;active=top;
        lastGood=document?.Clone();   // the passed-in document is a known-good saved state
        Text="基础宏编辑器";Size=new Size(1000,760);MinimumSize=new Size(800,600);StartPosition=FormStartPosition.CenterParent;
        var header=new FlowLayoutPanel(){Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(8)};
        header.Controls.AddRange([new Label(){Text="名称",AutoSize=true},nameBox,new Label(){Text="整体轮数",AutoSize=true},loopsBox]);
        Button(header,"新建",()=>{if(!Discard())return;draft=MacroDocument.CreateNew();filePath=null;SavedPath=null;AdoptPath=null;lastSaveError=null;lastGood=null;RefreshDocument();dirty=false;status.ForeColor=Color.DimGray;status.Text="新宏（尚未保存）";});
        Button(header,"打开",Open);Button(header,"保存",()=>Save(false));Button(header,"另存",()=>Save(true));
        Button(header,"绑定窗口",Bind);Button(header,"解除绑定",()=>ChangeBinding(null));Button(header,"取点（3秒）",async()=>await Pick());
        var info=new FlowLayoutPanel(){Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(8)};info.Controls.Add(binding);info.Controls.Add(status);
        var outer=new SplitContainer(){Dock=DockStyle.Fill,Orientation=Orientation.Horizontal};
        var lists=new SplitContainer(){Dock=DockStyle.Fill};lists.Panel1.Controls.Add(BuildList(top,false));lists.Panel2.Controls.Add(BuildList(child,true));
        outer.Panel1.Controls.Add(lists);outer.Panel2.Controls.Add(properties);
        Controls.Add(outer);Controls.Add(info);Controls.Add(header);
        Shown+=(_,_)=>{outer.SplitterDistance=outer.Height/2;lists.SplitterDistance=lists.Width/2;};
        foreach(var v in new[]{top,child})
        {
            v.Columns.Add("动作",110);v.Columns.Add("参数",350);v.SelectedIndexChanged+=(_,_)=>SelectSteps(v);
            v.ItemDrag+=(_,e)=>v.DoDragDrop(e.Item!,DragDropEffects.Move);
            v.DragOver+=(_,e)=>e.Effect=e.Data?.GetDataPresent(typeof(ListViewItem))==true?DragDropEffects.Move:DragDropEffects.None;
            v.DragDrop+=(_,e)=>Drop(v,e);
        }
        properties.PropertyValueChanged+=(_,_)=>{dirty=true;UpdateRows();AutoSave();};
        nameBox.TextChanged+=(_,_)=>{if(!loading){draft.Name=nameBox.Text;dirty=true;AutoSave();}};
        loopsBox.ValueChanged+=(_,_)=>{if(!loading){draft.Loops=(int)loopsBox.Value;dirty=true;AutoSave();}};
        commitFilter=new GridCommitFilter(this);
        Application.AddMessageFilter(commitFilter);
        FormClosing+=(_,e)=>{CommitPendingGridEdit();if(!Discard())e.Cancel=true;};RefreshDocument();dirty=false;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) Application.RemoveMessageFilter(commitFilter);
        base.Dispose(disposing);
    }

    // The PropertyGrid CANCELS a half-typed value when focus leaves it (verified:
    // the edit closes and the old value stays). Every commit path therefore goes
    // through here: the message filter commits on any mouse-down anywhere (so
    // clicking a list row, a toolbar button or 保存 keeps the typed value), and the
    // FormClosing hook commits before the close prompt. Sending VK_RETURN to the
    // focused value editor is exactly what pressing Enter does.
    internal void CommitPendingGridEdit()
    {
        var edit=FindOpenEdit(properties);
        if (edit is null) return;
        // Quiet settle: a convertible value is committed as-is; anything the grid
        // would reject (e.g. letters in an int row) is first REPLACED with the row's
        // current valid value and then committed. Replacing - instead of cancelling -
        // matters: the grid keeps its pending text internally and would pop its
        // conversion error dialog on the next selection change even after an Esc.
        // Committing the old value consumes that state silently, the row keeps its
        // value and no dialog can ever appear.
        if (!PendingEditCanCommit(edit))
        {
            var safe=properties.SelectedGridItem?.Value?.ToString() ?? "";
            GridNative.SetText(edit.Handle,safe);
        }
        GridNative.SendReturn(edit.Handle);
        edit=FindOpenEdit(properties);
        if (edit is not null)   // belt & braces: same treatment if anything stayed open
        {
            var safe=properties.SelectedGridItem?.Value?.ToString() ?? "";
            GridNative.SetText(edit.Handle,safe);
            GridNative.SendReturn(edit.Handle);
        }
    }
    private bool PendingEditCanCommit(TextBox edit)
    {
        var type=properties.SelectedGridItem?.PropertyDescriptor?.PropertyType;
        if (type==typeof(int)) return int.TryParse(edit.Text, out _);
        return true;   // string/bool rows have no conversion failure to fear
    }
    internal string SafeRowValue()
        => properties.SelectedGridItem?.Value?.ToString() ?? "";
    // The PropertyGrid hosts exactly one VISIBLE TextBox while a value is being
    // edited (toolbar disabled), so the open editor is found by visibility — the
    // Win32 focus flag is not reliable for the hosted editor.
    private static TextBox? FindOpenEdit(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is TextBox { Visible: true } tb) return tb;
            var hit = FindOpenEdit(c);
            if (hit is not null) return hit;
        }
        return null;
    }
    private static class GridNative
    {
        [DllImport("user32.dll")] private static extern nint SendMessageW(nint h, uint m, nuint w, nint l);
        public static void SendReturn(nint h)
        {
            SendMessageW(h, 0x0100, 0x0D, 0);                          // WM_KEYDOWN VK_RETURN
            SendMessageW(h, 0x0101, 0x0D, unchecked((nint)0xC0000001)); // WM_KEYUP
        }
        public static void SetText(nint h, string text)
        {
            var ptr=Marshal.StringToHGlobalUni(text);
            try { SendMessageW(h, 0x000C, nuint.Zero, ptr); }           // WM_SETTEXT
            finally { Marshal.FreeHGlobal(ptr); }
        }
    }
    private sealed class GridCommitFilter(MacroEditorForm owner) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_KEYDOWN = 0x0100;
            const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207,
                      WM_NCLBUTTONDOWN = 0x00A1, WM_NCRBUTTONDOWN = 0x00A4, WM_NCMBUTTONDOWN = 0x00A7;
            bool isMouseDown = m.Msg is (WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN
                or WM_NCLBUTTONDOWN or WM_NCRBUTTONDOWN or WM_NCMBUTTONDOWN);
            bool isEnterOrTab = m.Msg == WM_KEYDOWN && m.WParam.ToInt64() is 0x0D or 0x09;
            if (!isMouseDown && !isEnterOrTab) return false;

            var target = Control.FromHandle(m.HWnd);
            if (target is null) return false;

            // ENTER/TAB commits the grid's value editor directly (the grid's own path).
            // An unconvertible text would make the grid pop its conversion error dialog
            // here - replace the pending text with the row's current valid value BEFORE
            // the key is dispatched, so the commit is silent and the row keeps its value.
            if (isEnterOrTab)
            {
                if (target is TextBox && owner.properties.Contains(target)
                    && !owner.PendingEditCanCommit((TextBox)target))
                    GridNative.SetText(target.Handle, owner.SafeRowValue());
                return false;
            }

            // Only clicks on THIS editor may commit the pending value:
            //  * a click inside the grid's own value editor (its TextBox) must NOT commit -
            //    it places the caret / selects text, so the edit has to stay open;
            //  * a click on another row of the grid or anywhere else on this form commits
            //    first, because the grid cancels a half-typed value when it loses focus;
            //  * clicks in other windows (modal dialogs, other applications) are ignored.
            if (target is TextBox && owner.properties.Contains(target)) return false;
            if (owner.Contains(target)) owner.CommitPendingGridEdit();
            return false;
        }
    }

    private Control BuildList(ListView v,bool children)
    {
        var panel=new Panel(){Dock=DockStyle.Fill};var bar=new FlowLayoutPanel(){Dock=DockStyle.Top,AutoSize=true};
        foreach(var type in children?MacroStepType.AllowedInLoop:MacroStepType.All){var t=type;Button(bar,"加"+MacroStepType.Display(t),()=>Add(v,t));}
        Button(bar,"删除",()=>Delete(v));Button(bar,"复制",()=>Duplicate(v));Button(bar,"上移",()=>MoveStep(v,-1));Button(bar,"下移",()=>MoveStep(v,1));Button(bar,"浏览录制",()=>Browse(v));
        panel.Controls.Add(v);panel.Controls.Add(bar);return panel;
    }
    private static void Button(Control p,string text,Action a){var b=new Button(){Text=text,AutoSize=true};b.Click+=(_,_)=>a();p.Controls.Add(b);}
    private List<MacroStep>? Steps(ListView v)=>v==top?draft.Steps:selectedLoop?.Children;
    private void Fill(ListView v,List<MacroStep>? steps,IEnumerable<MacroStep>? selected=null)
    {
        var selection=selected?.ToHashSet() ?? [];loading=true;v.BeginUpdate();v.Items.Clear();
        if(steps is not null)foreach(var s in steps){var i=new ListViewItem(MacroStepType.Display(s.Type)){Tag=s};i.SubItems.Add(MacroEngine.Describe(s));v.Items.Add(i);i.Selected=selection.Contains(s);}
        v.EndUpdate();loading=false;
    }
    private void RefreshDocument(){loading=true;nameBox.Text=draft.Name;loopsBox.Value=Math.Clamp(draft.Loops,1,MacroSchema.MaxLoopTimes);loading=false;selectedLoop=null;properties.SelectedObject=null;Fill(top,draft.Steps);Fill(child,null);binding.Text=draft.WindowBind?.Describe() ?? "未绑定：屏幕坐标。多选同类型动作可批量编辑，拖动可在同一列表内排序。";}
    private void SelectSteps(ListView v)
    {
        if(loading)return;CommitPendingGridEdit();active=v;var selected=v.SelectedItems.Cast<ListViewItem>().Select(i=>(MacroStep)i.Tag!).ToArray();
        if(v==top){selectedLoop=selected.Length==1 && selected[0].Type==MacroStepType.Loop?selected[0]:null;Fill(child,selectedLoop?.Children);}
        properties.SelectedObjects=selected.Select(s=>(object)new StepView(s)).ToArray();
    }
    private void UpdateRows(){foreach(var v in new[]{top,child})foreach(ListViewItem i in v.Items){var s=(MacroStep)i.Tag!;i.SubItems[0].Text=MacroStepType.Display(s.Type);i.SubItems[1].Text=MacroEngine.Describe(s);}}
    private void Add(ListView v,string type){var steps=Steps(v);if(steps is null){MessageBox.Show(this,"请先选中有限循环。");return;}var s=new MacroStep(){Type=type,WaitMs=100,Key="A",Text="文本"};if(type==MacroStepType.Loop)s.Children.Add(new MacroStep(){WaitMs=100});steps.Add(s);dirty=true;Fill(v,steps,[s]);SelectSteps(v);AutoSave();}
    private void Delete(ListView v){var steps=Steps(v);if(steps is null)return;foreach(var s in v.SelectedItems.Cast<ListViewItem>().Select(i=>(MacroStep)i.Tag!).ToArray())steps.Remove(s);dirty=true;Fill(v,steps);SelectSteps(v);AutoSave();}
    private void Duplicate(ListView v){var steps=Steps(v);if(steps is null)return;var copies=v.SelectedItems.Cast<ListViewItem>().Select(i=>((MacroStep)i.Tag!).Clone()).ToArray();steps.AddRange(copies);dirty=true;Fill(v,steps,copies);SelectSteps(v);AutoSave();}
    private void MoveStep(ListView v,int delta){var steps=Steps(v);if(steps is null || v.SelectedIndices.Count!=1)return;int i=v.SelectedIndices[0],j=i+delta;if(j<0||j>=steps.Count)return;var item=steps[i];steps.RemoveAt(i);steps.Insert(j,item);dirty=true;Fill(v,steps,[item]);SelectSteps(v);AutoSave();}
    // Drag reorder inside ONE list only: a foreign ListView item (or a drop outside any
    // row) is refused, so cross-list moves can never silently re-parent a step.
    // Drag reorder inside ONE list only: a foreign ListView item (or a drop outside any
    // row) is refused, so cross-list moves can never silently re-parent a step. The
    // reorder itself lives in MoveStepTo so the semantics are testable without the
    // OLE drag gesture.
    private void Drop(ListView v,DragEventArgs e){if(e.Data?.GetData(typeof(ListViewItem)) is not ListViewItem item || item.ListView!=v)return;var p=v.PointToClient(new Point(e.X,e.Y));var target=v.GetItemAt(p.X,p.Y);if(target is null)return;MoveStepTo(v,(MacroStep)item.Tag!,target.Index);}
    // Move one step to targetIndex (the row index it was dropped on) and re-select it;
    // execution order is exactly the list order.
    internal void MoveStepTo(ListView v,MacroStep step,int targetIndex)
    {
        var steps=Steps(v);if(steps is null||!steps.Contains(step))return;
        int j=Math.Clamp(targetIndex,0,steps.Count-1);
        steps.Remove(step);steps.Insert(j,step);
        dirty=true;Fill(v,steps,[step]);SelectSteps(v);AutoSave();
    }
    private void Browse(ListView v){if(v.SelectedItems.Count!=1 || v.SelectedItems[0].Tag is not MacroStep {Type:MacroStepType.RefRecording} s)return;using var d=new OpenFileDialog(){Filter="录制 JSON|*.json"};if(d.ShowDialog(this)==DialogResult.OK){s.RefPath=MacroStore.RelativizeToMacro(d.FileName,filePath);dirty=true;UpdateRows();properties.Refresh();AutoSave();}}
    // 修改后自动保存：宏已有文件路径时，每次提交的修改立即写回该文件（不弹窗）。
    // 失败只写进状态行，dirty 保持，草稿与原文件都不动 —— 关闭/新建/打开被拦下时
    // 会进入 ResolveSaveFailure 的恢复流程（重试/另存/放弃/取消）。新宏（尚未有路径）
    // 无法自动落盘，仍由保存/关闭流程处理。保存成功会覆盖掉上一次的失败提示；
    // 窗口绑定描述在独立的 binding 标签上，永远不被保存消息覆盖。
    private void AutoSave()
    {
        if (filePath is null || !dirty) return;
        try
        {
            new MacroStore().Save(draft, filePath);
            dirty=false;SavedPath=filePath;AdoptPath=filePath;lastSaveError=null;
            lastGood=draft.Clone();   // the just-written state is the new known-good
            status.ForeColor=Color.DimGray;status.Text="已自动保存："+filePath;
        }
        catch(Exception ex)
        {
            lastSaveError=ex.Message;
            status.ForeColor=Color.Firebrick;
            status.Text="自动保存失败："+ex.Message+"（未保存的修改仍在编辑器中；关闭/新建/打开时会提供处理选项）";
        }
    }

    // Confirmation for "discard unsaved changes". The default is the real modal
    // Yes/No prompt; the verification project substitutes the answer so the
    // FormClosing/cancel wiring can be asserted without a modal dialog.
    internal Func<DialogResult>? ConfirmDiscard { get; set; }
    // Recovery prompt used when a FAILED autosave blocks close/new/open. The argument
    // is the full situation description (last autosave error + any follow-up failure);
    // the product default is the SaveRecoveryForm modal. The verification project
    // scripts the answers (Retry / SaveAs / Discard / Cancel).
    internal Func<string,RecoveryChoice>? ConfirmRecovery { get; set; }
    // Seam for the file dialogs so the recovery "另存" path and 打开 can be driven
    // headless; null = real SaveFileDialog/OpenFileDialog. Returning null means cancel.
    internal Func<string?,string?>? PickSavePath { get; set; }
    internal Func<string?>? PickOpenPath { get; set; }
    // Seams for the two MessageBoxes so failures can be asserted headless.
    internal Action<string>? ReportOpenError { get; set; }
    internal Action<string>? ReportPickError { get; set; }
    // Confirmation for the binding-switch prompt (切换坐标模式会清空坐标); null = real
    // MessageBox. PickDelayMs replaces the fixed 3s wait in the same way.
    internal Func<DialogResult>? ConfirmRebind { get; set; }
    internal int PickDelayMs { get; set; } = 3000;
    private bool Discard()
    {
        if (!dirty) return true;
        if (filePath is null)
        {
            var answer=ConfirmDiscard?.Invoke() ?? MessageBox.Show(this,"有尚未保存的修改，确定放弃吗？","基础宏",MessageBoxButtons.YesNo);
            return answer==DialogResult.Yes;
        }
        AutoSave();
        if (!dirty) return true;        // 自动保存成功：与旧行为一致，直接放行
        return ResolveSaveFailure();    // 自动保存失败：恢复流程，绝不无限拦人
    }

    // Recovery loop for a failed autosave that is blocking a close/new/open. Runs until
    // the draft is saved, the user explicitly discards it, or cancels the operation.
    // The original file is never damaged by a failing save (MacroStore writes a temp
    // file first and only then replaces), so 放弃修改 can always restore it.
    private bool ResolveSaveFailure()
    {
        string? extra=null;
        while(true)
        {
                string describe=(string.IsNullOrEmpty(extra)?"":extra+"\r\n")
                    +"自动保存失败："+(lastSaveError??"未知原因")+"\r\n未保存的修改仍保留在编辑器中。放弃修改时优先读取原文件，无法读取时使用最后已知有效的内存快照并解除关联。";
            var choice=ConfirmRecovery?.Invoke(describe) ?? ShowRecoveryDialog(describe);
            switch(choice)
            {
                case RecoveryChoice.Retry:
                    AutoSave();
                    if(!dirty) return true;
                    extra=null;break;   // still failing: ask again
                case RecoveryChoice.SaveAs:
                    var target=AskSaveTarget();
                    if(target is null){extra="已取消另存。";break;}
                    var (ok,err)=SaveTo(target);
                    if(ok) return true;
                    extra="另存失败："+err;break;   // 另存不是唯一出口：回到选项
                case RecoveryChoice.Discard:
                    return ReloadLastSaved();
                default:
                    return false;       // 取消操作：留在编辑器，草稿与 dirty 原样保留
            }
        }
    }
    private RecoveryChoice ShowRecoveryDialog(string describe)
    {
        using var f=new SaveRecoveryForm(filePath!,describe);
        f.ShowDialog(this);
        return f.Choice;
    }
    // 放弃未保存修改。首选：把草稿恢复为原文件中的最后保存版本（它是最后一次
    // 成功写入的有效内容），dirty 清零，随后的关闭/新建/打开不再被拦，原文件
    // 字节不变。回退：原文件已删除或损坏、无法读取时，恢复内存中最后一次成功
    // 加载/保存的深拷贝快照，并解除与该无效文件的关联（filePath/SavedPath/
    // AdoptPath 全部清空）—— 绝不重建、覆盖或"修复"用户原文件，主界面也不会
    // 误采用缺失或损坏的文件；文档回到未保存状态，需要落盘时用"另存"。
    private bool ReloadLastSaved()
    {
        try
        {
            draft=new MacroStore().Load(filePath!);
            RefreshDocument();
            dirty=false;lastSaveError=null;SavedPath=filePath;   // AdoptPath 不动：没有产生新的保存结果
            lastGood=draft.Clone();
            status.ForeColor=Color.DimGray;
            status.Text="已放弃未保存的修改，恢复为文件中的最后保存版本。";
            return true;
        }
        catch(Exception fileEx)
        {
            if(lastGood is not null)
            {
                // 以原宏路径为基准，把恢复副本中的相对录制引用解析成绝对路径
                //（顶层动作与有限循环子动作都遍历）。否则解除关联后的跨目录
                // 另存会退回 AppContext.BaseDirectory 作为解析基准，引用丢失。
                // lastGood 本体保持不变（在克隆上操作）。
                string originalPath = filePath!;
                var restored = lastGood.Clone();
                void Walk(List<MacroStep> steps)
                {
                    foreach(var s in steps)
                    {
                        if(s.Type==MacroStepType.RefRecording && !string.IsNullOrWhiteSpace(s.RefPath))
                            s.RefPath=MacroStore.ResolvePath(s.RefPath!,originalPath);
                        if(s.Type==MacroStepType.Loop && s.Children is not null) Walk(s.Children);
                    }
                }
                Walk(restored.Steps);
                draft=restored;
                RefreshDocument();
                dirty=false;lastSaveError=null;
                filePath=null;SavedPath=null;AdoptPath=null;
                status.ForeColor=Color.DimGray;
                status.Text="已放弃未保存的修改，恢复为内存中最后一次成功加载/保存的版本（原文件不可读："+fileEx.Message+"）。录制引用已按原宏位置解析为绝对路径；文档当前未保存，需要落盘请用另存。";
                return true;
            }
            status.ForeColor=Color.Firebrick;
            status.Text="放弃修改失败（无法读取原文件，也没有可用的内存快照）："+fileEx.Message;
            return false;   // 留在编辑器，草稿与 dirty 原样保留
        }
    }
    private string? AskSaveTarget()
    {
        if(PickSavePath is not null)return PickSavePath(filePath);
        using var d=new SaveFileDialog(){Filter="基础宏|*.macro.json",FileName=Path.GetFileName(filePath) ?? "新建宏.macro.json"};
        return d.ShowDialog(this)==DialogResult.OK?d.FileName:null;
    }
    private string? AskOpenPath()
    {
        if(PickOpenPath is not null)return PickOpenPath();
        using var d=new OpenFileDialog(){Filter="宏 JSON|*.json"};
        return d.ShowDialog(this)==DialogResult.OK?d.FileName:null;
    }
    private void Open()
    {
        if(!Discard())return;
        string? name=AskOpenPath();
        if(name is null)return;   // 取消打开：当前草稿与它的路径状态原样保留
        try
        {
            draft=new MacroStore().Load(name);
            filePath=name;RefreshDocument();dirty=false;lastSaveError=null;
            // 打开成功：当前文档明确对应这份已存在的有效文件，主界面随后采用它。
            SavedPath=name;AdoptPath=name;
            lastGood=draft.Clone();
            status.ForeColor=Color.DimGray;status.Text="已打开："+name;
        }
        catch(Exception ex){ShowOpenError(ex.Message);}   // 打开失败：draft/路径/归属全部保持不变
    }
    private void ShowOpenError(string msg){if(ReportOpenError is null)MessageBox.Show(this,msg);else ReportOpenError(msg);}
    private void Save(bool saveAs)
    {
        CommitPendingGridEdit();   // quiet: commits valid input, reverts invalid input
        nameBox.Focus();if(!ValidateChildren())return;string? target=filePath;
        if(saveAs||target is null)target=AskSaveTarget();
        if(target is null)return;
        var (ok,error)=SaveTo(target);
        // No modal here: the state line reports the outcome, the editor stays usable.
        status.ForeColor=ok?Color.DimGray:Color.Firebrick;
        status.Text=ok?($"已保存：{target}（关闭编辑器后主界面采用此版本）"):($"保存失败：{error}");
    }
    // Save WITHOUT any dialog or message box: validation failure and write failure both
    // return (false, reason) and leave the draft, the previous file and dirty state as
    // they were. Used by the 保存/另存 buttons (which add the dialogs), by the recovery
    // loop and by the verification project. Cross-directory targets repoint recording
    // references to the SAME files; same-directory saves keep the relative references
    // untouched.
    internal (bool ok,string? error) SaveTo(string target)
    {
        try
        {
            var copy=filePath is null || !MacroStore.SameDirectory(filePath,target)?MacroStore.RewriteReferencesForNewDir(draft,filePath,target):draft.Clone();
            new MacroStore().Save(copy,target);
            draft=copy;filePath=target;SavedPath=target;AdoptPath=target;lastSaveError=null;
            lastGood=copy.Clone();
            RefreshDocument();dirty=false;
            return (true,null);
        }
        catch(Exception ex){return (false,ex.Message);}
    }
    private void Bind(){using var d=new WindowPickerForm();if(d.ShowDialog(this)==DialogResult.OK && d.Result is not null)ChangeBinding(d.Result);}
    internal void ChangeBinding(WindowBinding? b)
    {
        var answer=ConfirmRebind?.Invoke() ?? MessageBox.Show(this,"切换坐标模式会清空移动/单击坐标，需要重新取点。继续？","窗口绑定",MessageBoxButtons.OKCancel);
        if(answer!=DialogResult.OK)return;   // 取消绑定确认：不改动、不保存、不动 dirty
        draft.WindowBind=b;draft.ClearAllMoveClickCoordinates();
        dirty=true;RefreshDocument();AutoSave();   // 绑定+清坐标是一次完整状态，一次写盘
    }
    internal async Task Pick()
    {
        if(active.SelectedItems.Count!=1 || active.SelectedItems[0].Tag is not MacroStep s || s.Type is not (MacroStepType.Move or MacroStepType.Click))return;
        Enabled=false;try{await Task.Delay(PickDelayMs);if(IsDisposed)return;var p=Cursor.Position;if(draft.WindowBind is not null){var target=WindowTargetResolver.Resolve(draft.WindowBind,out var error);if(target is null)throw new InvalidDataException(error);if(!WindowTargetResolver.ScreenToClientPoint(target.Hwnd,p.X,p.Y,out int x,out int y))throw new InvalidDataException("取点失败。");if(x<0||y<0||x>=draft.WindowBind.ClientWidth||y>=draft.WindowBind.ClientHeight)throw new InvalidDataException("取点在客户区之外。");p=new Point(x,y);}s.X=p.X;s.Y=p.Y;dirty=true;UpdateRows();properties.Refresh();AutoSave();}catch(Exception ex){if(!IsDisposed){if(ReportPickError is null)MessageBox.Show(this,ex.Message);else ReportPickError(ex.Message);}}finally{if(!IsDisposed)Enabled=true;}
    }
    private sealed class StepView(MacroStep step):CustomTypeDescriptor
    {
        public override object GetPropertyOwner(PropertyDescriptor? pd)=>step;
        public override PropertyDescriptorCollection GetProperties()=>GetProperties(null);
        public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
        {
            string[] names=step.Type switch{MacroStepType.Move=>["X","Y"],MacroStepType.Click=>["X","Y","Button"],MacroStepType.Wait=>["WaitMs"],MacroStepType.Loop=>["Times"],MacroStepType.RefRecording=>["RefPath","RefLoops"],MacroStepType.Key=>["Key","Ctrl","Alt","Shift"],MacroStepType.Text=>["Text"],_=>[]};
            var labels=new Dictionary<string,string>{{"X","横坐标"},{"Y","纵坐标"},{"Button","鼠标键：left / middle / right"},{"WaitMs","等待（毫秒）"},{"Times","循环次数"},{"RefPath","录制文件路径"},{"RefLoops","录制回放轮数"},{"Key","主键：A / ENTER / TAB / F1 等"},{"Text","输入文本"}};
            return new PropertyDescriptorCollection(TypeDescriptor.GetProperties(step).Cast<PropertyDescriptor>().Where(p=>names.Contains(p.Name)).Select(p=>{
                var attrs=new List<Attribute>{new DisplayNameAttribute(labels.GetValueOrDefault(p.Name,p.Name))};
                if(p.Name=="Text")attrs.Add(new EditorAttribute(typeof(MultilineTextEditor),typeof(UITypeEditor)));
                return TypeDescriptor.CreateProperty(typeof(MacroStep),p,attrs.ToArray());
            }).ToArray());
        }
    }
}

// Modal multiline editor for the text action: the standard PropertyGrid string row is
// single-line, but the text action explicitly supports CRLF (Enter) and Tab, so the
// value must be editable across lines. OK commits, Cancel keeps the previous value.
public sealed class MultilineTextEditor:UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context)=>UITypeEditorEditStyle.Modal;
    public override object? EditValue(ITypeDescriptorContext? context,IServiceProvider provider,object? value)
    {
        if(provider?.GetService(typeof(IWindowsFormsEditorService)) is not IWindowsFormsEditorService svc)return value;
        using var form=new MultilineTextForm(value as string ?? "");
        return svc.ShowDialog(form)==DialogResult.OK?form.TextValue:value;
    }
}

public sealed class MultilineTextForm:Form
{
    // AcceptsReturn + AcceptsTab are BOTH required: Enter must insert a line break
    // (never trigger 确定) and Tab must insert a tab character, exactly as the window
    // text promises. Ctrl+Enter is therefore the keyboard shortcut for 确定 (with Tab
    // captured by the editor, Tab could not move focus to the button any more).
    private readonly TextBox box=new(){Multiline=true,AcceptsReturn=true,AcceptsTab=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=new Font("Consolas",10f),Name="textBox"};
    public string TextValue=>box.Text;
    public MultilineTextForm(string initial)
    {
        Text="编辑文本（Enter 换行，Tab 插入制表符）";Width=560;Height=380;StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;
        box.Text=initial;
        var hint=new Label(){Dock=DockStyle.Bottom,Height=22,AutoSize=false,ForeColor=Color.DimGray,
            Text="Enter=换行　Tab=制表符　Ctrl+Enter=确定　Esc=取消",Name="hint",TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(6,0,0,0)};
        var buttons=new FlowLayoutPanel(){Dock=DockStyle.Bottom,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8)};
        var ok=new Button(){Text="确定",AutoSize=true,DialogResult=DialogResult.OK,Name="okButton"};
        var cancel=new Button(){Text="取消",AutoSize=true,DialogResult=DialogResult.Cancel,Name="cancelButton"};
        buttons.Controls.Add(cancel);buttons.Controls.Add(ok);
        Controls.Add(box);Controls.Add(hint);Controls.Add(buttons);
        AcceptButton=ok;CancelButton=cancel;
        ActiveControl=box;
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(keyData==(Keys.Control|Keys.Enter))
        {
            DialogResult=DialogResult.OK;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }
}

// What the user chose when a failed autosave blocked close/new/open. The product
// shows SaveRecoveryForm; the verification project scripts these values through
// MacroEditorForm.ConfirmRecovery.
internal enum RecoveryChoice { Retry, SaveAs, Discard, Cancel }

// Modal recovery prompt: the autosave failed, the ORIGINAL file still holds the last
// successfully saved content and the draft is kept in the editor. Four real exits:
// retry the write, save the draft elsewhere, restore the file version (放弃修改), or
// stay in the editor and cancel the pending close/new/open.
internal sealed class SaveRecoveryForm:Form
{
    public RecoveryChoice Choice { get; private set; }=RecoveryChoice.Cancel;
    public SaveRecoveryForm(string path,string describe)
    {
        Text="自动保存失败";FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
        StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;Size=new Size(640,340);
        var text=new Label(){Dock=DockStyle.Fill,AutoSize=true,MaximumSize=new Size(600,0),Padding=new Padding(14),
            Text=describe+"\r\n\r\n重试保存：再试一次写回原文件。\r\n另存为：把当前草稿保存为其他文件。\r\n放弃修改：优先恢复为文件中的最后保存版本；原文件不可读时恢复为内存中最后一次成功加载/保存的快照并解除关联，然后继续。\r\n取消操作：留在编辑器，修正后自行重试。"};
        var buttons=new FlowLayoutPanel(){Dock=DockStyle.Bottom,AutoSize=true,Padding=new Padding(10)};
        Button Mk(string label,RecoveryChoice choice)
        {
            var b=new Button(){Text=label,AutoSize=true};
            b.Click+=(_,_)=>{Choice=choice;DialogResult=DialogResult.OK;Close();};
            buttons.Controls.Add(b);return b;
        }
        Mk("重试保存",RecoveryChoice.Retry);Mk("另存为",RecoveryChoice.SaveAs);Mk("放弃修改",RecoveryChoice.Discard);
        CancelButton=Mk("取消操作",RecoveryChoice.Cancel);
        Controls.Add(text);Controls.Add(buttons);
    }
}
