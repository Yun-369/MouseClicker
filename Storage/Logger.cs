using System.Threading.Channels;

namespace MouseClicker.Storage;

// Basic run log: keeps the latest lines in memory for the UI page and appends to
// a daily file under %LOCALAPPDATA%. Each record is filed by ITS OWN date, so a
// session running across midnight starts writing to the new day's file instead
// of the startup-day one. File writes run on a background channel so the UI
// thread is never blocked by disk IO. EntryAdded may fire on worker threads.
public sealed class Logger
{
    public static readonly Logger Instance = new();

    private readonly object gate = new();
    private readonly List<string> lines = [];
    private readonly string directory;
    private readonly Channel<Entry> queue = Channel.CreateUnbounded<Entry>();
    private int pending;

    private readonly record struct Entry(DateTime When, string Line);

    public event Action<string>? EntryAdded;

    private Logger()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseClicker", "logs"))
    {
    }

    // Isolated instance for the verification project: same behaviour, but the file
    // directory is supplied by the caller (a fresh temp dir), so tests never read,
    // write or delete the user's real %LOCALAPPDATA% logs. The shared Instance
    // keeps using the normal directory and is never touched by tests.
    internal Logger(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
        _ = Task.Run(FlushLoopAsync);
    }

    public void Write(string message) => WriteAt(DateTime.Now, message);

    // Internal entry point that takes the record time; used to verify cross-date
    // filing without changing the system clock.
    internal void WriteAt(DateTime when, string message)
    {
        var line = $"{when:HH:mm:ss.fff}  {message}";
        lock (gate)
        {
            lines.Add(line);
            if (lines.Count > 500) lines.RemoveAt(0);
        }
        Interlocked.Increment(ref pending);
        queue.Writer.TryWrite(new Entry(when, line));
        EntryAdded?.Invoke(line);
    }

    public string[] Snapshot()
    {
        lock (gate) return [.. lines];
    }

    internal string FileFor(DateTime when) => Path.Combine(directory, $"run-{when:yyyyMMdd}.log");

    // Called on process exit: stop accepting and wait briefly for the file to drain.
    public void Shutdown()
    {
        queue.Writer.TryComplete();
        var deadline = DateTime.UtcNow.AddMilliseconds(800);
        while (Volatile.Read(ref pending) > 0 && DateTime.UtcNow < deadline) Thread.Sleep(20);
    }

    private async Task FlushLoopAsync()
    {
        try
        {
            await foreach (var entry in queue.Reader.ReadAllAsync())
            {
                try { File.AppendAllText(FileFor(entry.When), entry.Line + Environment.NewLine); } catch { }
                Interlocked.Decrement(ref pending);
            }
        }
        catch (ChannelClosedException) { }
    }
}
