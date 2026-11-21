using System.Text.Json;
namespace MouseClicker.Storage;
public sealed record RecordingEntry(string Path,int Hotkey=0);
public sealed class RecordingLibrary
{
    public string FilePath{get;}
    public List<RecordingEntry> Entries{get;private set;}=[];
    public RecordingLibrary(string? filePath=null)
    {
        FilePath=filePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MouseClicker","library.json");
        if(File.Exists(FilePath))
        {
            try { Entries=JsonSerializer.Deserialize<List<RecordingEntry>>(File.ReadAllText(FilePath)) ?? []; }
            catch(JsonException ex) { throw new InvalidDataException("录制库索引不是有效的 JSON；原文件未改动。", ex); }
            ValidateEntries();
        }
    }
    public void Add(string path)
    {
        path=Path.GetFullPath(path);
        new RecordingStore().Load(path);
        if(Entries.Any(e=>string.Equals(e.Path,path,StringComparison.OrdinalIgnoreCase)))return;
        Entries.Add(new(path));
        try { Save(); }
        catch { Entries.RemoveAt(Entries.Count-1); throw; }
    }
    public void Save()
    {
        ValidateEntries();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        // Use a unique sibling so a failed save never removes an unrelated
        // user-created file named `library.json.tmp`.
        string temp = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json=JsonSerializer.Serialize(Entries,new JsonSerializerOptions{WriteIndented=true});
            File.WriteAllText(temp,json);
            File.Move(temp,FilePath,true);
        }
        finally { try { if(File.Exists(temp))File.Delete(temp); } catch { } }
    }

    private void ValidateEntries()
    {
        for(int i=0;i<Entries.Count;i++)
        {
            var e=Entries[i];
            if(string.IsNullOrWhiteSpace(e.Path))throw new InvalidDataException($"录制库第 {i+1} 项路径为空。");
            try { _=Path.GetFullPath(e.Path); }
            catch(Exception ex) when(ex is ArgumentException or NotSupportedException or PathTooLongException)
            { throw new InvalidDataException($"录制库第 {i+1} 项路径无效。", ex); }
            if(e.Hotkey!=0 && (e.Hotkey<0x70||e.Hotkey>0x7B))throw new InvalidDataException("录制热键只能是 F1–F12。");
        }
        if(Entries.GroupBy(e=>e.Path,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new InvalidDataException("录制库包含重复文件。");
        if(Entries.Where(e=>e.Hotkey!=0).GroupBy(e=>e.Hotkey).Any(g=>g.Count()>1))throw new InvalidDataException("录制库热键重复。");
    }
}
