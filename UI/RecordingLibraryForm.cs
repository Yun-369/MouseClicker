using System.Runtime.InteropServices;
using MouseClicker.Storage;
namespace MouseClicker.UI;

// A deliberately narrow seam so the r9 acceptance can exercise policy outcomes
// without changing the user's real per-volume recycle-bin settings.
internal readonly record struct RecyclePolicyRead(
    bool KeyExists, bool ValueExists, Microsoft.Win32.RegistryValueKind? ValueKind, object? Value);

public sealed class RecordingLibraryForm:Form
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint,System.Text.StringBuilder buffer,int size);

    // "删除到回收站" keeps its RECOVERABLE promise: when the volume's recycle bin is
    // CONFIRMED disabled (Explorer policy NukeOnDelete=1) the same delete API
    // degrades into a PERMANENT delete, so the operation is refused BEFORE any
    // change. If the state cannot be determined the operation is refused too
    // (conservative) - either way the file and the library entry stay untouched.
    private static (bool refuse,string reason) RecycleUnavailableReason(string path) =>
        RecycleUnavailableReason(path, ReadRecyclePolicy);

    // The injected reader is used only by the focused acceptance check. Production
    // always uses the registry reader below; read failures remain fail-closed.
    internal static (bool refuse,string reason) RecycleUnavailableReason(
        string path, Func<string,RecyclePolicyRead> readPolicy)
    {
        try
        {
            string root=Path.GetPathRoot(Path.GetFullPath(path))??"";
            if(root.Length==0)return(true,"无法确定文件所在卷。");
            var sb=new System.Text.StringBuilder(260);
            if(!GetVolumeNameForVolumeMountPoint(root,sb,sb.Capacity))return(true,"无法查询文件所在卷的标识。");
            string vn=sb.ToString();
            int i=vn.IndexOf('{'),j=vn.LastIndexOf('}');
            if(i<0||j<=i)return(true,"卷标识格式无法识别。");
            string guid=vn.Substring(i,j-i+1);
            var policy=readPolicy(guid);
            if(!policy.KeyExists)
                return(true,"无法确认回收站状态：该卷的回收站策略注册表键不存在。");
            if(!policy.ValueExists)
                return(true,"无法确认回收站状态：NukeOnDelete 策略值不存在。");
            if(policy.ValueKind!=Microsoft.Win32.RegistryValueKind.DWord || policy.Value is not int n)
                return(true,"无法确认回收站状态：NukeOnDelete 的注册表类型或值无效。");
            return n switch
            {
                0 => (false,""),
                1 => (true,$"卷 {guid} 的回收站已确认关闭（策略 NukeOnDelete=1），继续操作会导致永久删除。"),
                _ => (true,$"无法确认回收站状态：无法识别 NukeOnDelete 值 {n}。")
            };
        }
        catch(Exception ex){return(true,"无法确认回收站状态：读取策略失败："+ex.Message);}
    }

    private static RecyclePolicyRead ReadRecyclePolicy(string guid)
    {
        using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\"+guid);
        if(key is null)return new(false,false,null,null);
        bool hasValue=key.GetValueNames().Any(name=>
            string.Equals(name,"NukeOnDelete",StringComparison.OrdinalIgnoreCase));
        if(!hasValue)return new(true,false,null,null);
        var kind=key.GetValueKind("NukeOnDelete");
        var value=key.GetValue("NukeOnDelete",null,Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
        return new(true,true,kind,value);
    }
    private readonly RecordingLibrary library;
    private readonly Func<string,RecyclePolicyRead> recyclePolicyReader;
    private readonly Action<IWin32Window?,string,string> refusalPrompt;
    private readonly Func<IWin32Window?,string,string,bool> confirmationPrompt;
    private readonly ListView list=new(){Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false};
    private readonly ComboBox hotkey=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=170};
    public string? SelectedPath{get;private set;}
    public bool RunSelected{get;private set;}
    public RecordingLibraryForm(RecordingLibrary library):this(library,ReadRecyclePolicy,
        static(owner,message,title)=>MessageBox.Show(owner,message,title,MessageBoxButtons.OK,MessageBoxIcon.Warning),
        static(owner,message,title)=>MessageBox.Show(owner,message,title,MessageBoxButtons.YesNo)==DialogResult.Yes){}
    internal RecordingLibraryForm(RecordingLibrary library,Func<string,RecyclePolicyRead> recyclePolicyReader,
        Action<IWin32Window?,string,string> refusalPrompt,Func<IWin32Window?,string,string,bool> confirmationPrompt)
    {
        this.library=library;this.recyclePolicyReader=recyclePolicyReader;this.refusalPrompt=refusalPrompt;this.confirmationPrompt=confirmationPrompt;Text="录制库";Size=new Size(850,520);StartPosition=FormStartPosition.CenterParent;
        list.Columns.Add("录制",200);list.Columns.Add("文件",430);list.Columns.Add("独立热键",140);
        hotkey.Items.Add("无独立热键");for(int i=1;i<=12;i++)hotkey.Items.Add($"Ctrl+Alt+F{i}");hotkey.SelectedIndex=0;
        var bar=new FlowLayoutPanel(){Dock=DockStyle.Top,AutoSize=true};
        Add(bar,"添加文件",Import);Add(bar,"加载",()=>Choose(false));Add(bar,"立即回放",()=>Choose(true));Add(bar,"编辑",Edit);Add(bar,"重命名文件",Rename);Add(bar,"导出副本",Export);Add(bar,"移出列表",Remove);Add(bar,"删除到回收站",Trash);bar.Controls.Add(hotkey);Add(bar,"设置热键",SetHotkey);
        Controls.Add(list);Controls.Add(bar);list.SelectedIndexChanged+=(_,_)=>hotkey.SelectedIndex=Entry is {} e && e.Hotkey!=0?e.Hotkey-0x70+1:0;Reload();
    }
    private RecordingEntry? Entry=>list.SelectedItems.Count==1?list.SelectedItems[0].Tag as RecordingEntry:null;
    private void Add(Control p,string text,Action action){var b=new Button(){Text=text,AutoSize=true};b.Click+=(_,_)=>{try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"录制库");}};p.Controls.Add(b);}
    private void Reload(){list.Items.Clear();foreach(var e in library.Entries){string name;try{name=new RecordingStore().Load(e.Path).Name;}catch{name="文件不可用";}var i=new ListViewItem(name){Tag=e};i.SubItems.Add(e.Path);i.SubItems.Add(e.Hotkey==0?"":$"Ctrl+Alt+F{e.Hotkey-0x70+1}");list.Items.Add(i);}}
    private void Import(){using var d=new OpenFileDialog(){Filter="录制 JSON|*.json",Multiselect=true};if(d.ShowDialog(this)!=DialogResult.OK)return;foreach(var path in d.FileNames)library.Add(path);Reload();}
    private void Choose(bool run){if(Entry is not {} e)return;new RecordingStore().Load(e.Path);SelectedPath=e.Path;RunSelected=run;DialogResult=DialogResult.OK;}
    private void Edit(){if(Entry is not {} e)return;using var editor=new RecordingEditorForm(new RecordingStore().Load(e.Path),e.Path);editor.ShowDialog(this);if(editor.SavedPath is {} path)library.Add(path);Reload();}
    private void Rename()
    {
        if(Entry is not {} e)return;string name=Microsoft.VisualBasic.Interaction.InputBox("输入新的文件名（不含路径）","重命名",Path.GetFileNameWithoutExtension(e.Path));if(string.IsNullOrWhiteSpace(name))return;
        if(name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || name is "." or "..")throw new InvalidDataException("文件名无效。");
        if(MessageBox.Show(this,"重命名会让仍使用旧路径的宏引用失效。确认继续吗？","重命名录制",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        string path=Path.Combine(Path.GetDirectoryName(e.Path)!,name+".json");if(string.Equals(path,e.Path,StringComparison.OrdinalIgnoreCase))return;
        File.Move(e.Path,path,false);int index=library.Entries.IndexOf(e);library.Entries[index]=e with{Path=path};
        try{library.Save();}catch{library.Entries[index]=e;File.Move(path,e.Path);throw;}Reload();
    }
    private void Export(){if(Entry is not {} e)return;using var d=new SaveFileDialog(){Filter="录制 JSON|*.json",FileName=Path.GetFileName(e.Path)};if(d.ShowDialog(this)!=DialogResult.OK)return;
        // An export onto the source file itself is an overwrite, not a copy — refuse
        // it explicitly instead of silently rewriting the original recording.
        if(string.Equals(Path.GetFullPath(d.FileName),Path.GetFullPath(e.Path),StringComparison.OrdinalIgnoreCase)){MessageBox.Show(this,"导出副本不能与源文件是同一个文件。请换一个文件名。","导出副本");return;}
        new RecordingStore().Save(new RecordingStore().Load(e.Path),d.FileName);}
    private void Remove()
    {
        if(Entry is not {} e)return;
        int index=library.Entries.IndexOf(e);
        if(index<0)return;
        library.Entries.RemoveAt(index);
        try { library.Save(); }
        catch { library.Entries.Insert(index,e); throw; }
        Reload();
    }
    private void Trash()
    {
        if(Entry is not {} e)return;
        var(refuse,why)=RecycleUnavailableReason(e.Path,recyclePolicyReader);
        if(refuse)
        {
            refusalPrompt(this,
                "无法保证“删除到回收站”的可恢复性，本次操作已取消：\n"+why
                +"\n\n录制文件与录制库条目均未改动。",
                "删除到回收站");
            return;
        }
        if(!confirmationPrompt(this,$"将此录制移到回收站？\n{e.Path}","删除录制"))return;
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(e.Path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        int index=library.Entries.IndexOf(e);
        library.Entries.RemoveAt(index);
        try{library.Save();}
        catch{
            // The file IS already in the recycle bin — the index entry is restored so
            // the list stays truthful (the next reload shows the file as unavailable)
            // and the user gets an error that says exactly what happened.
            library.Entries.Insert(index,e);Reload();
            throw new InvalidDataException("文件已移入回收站，但录制库索引保存失败：列表可能与磁盘不一致，请稍后重试或重新打开录制库。");
        }
        Reload();
    }
    private void SetHotkey(){if(Entry is not {} e)return;int index=library.Entries.IndexOf(e);library.Entries[index]=e with{Hotkey=hotkey.SelectedIndex==0?0:0x70+hotkey.SelectedIndex-1};try{library.Save();}catch{library.Entries[index]=e;throw;}Reload();}
}
