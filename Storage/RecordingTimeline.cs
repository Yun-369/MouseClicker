using MouseClicker.Domain;

namespace MouseClicker.Storage;

// Pure timeline helpers shared by the recorder and the editor/optimizer.
public static class RecordingTimeline
{
    // Rebuild every retained event's DelayMs (gap from the previous retained event)
    // from its ORIGINAL absolute TimeMs. Use this after dropping events (e.g. the
    // control chord) so the surviving user actions keep their exact occurrence time:
    // a removed span simply folds into the next retained event's gap. The first
    // retained event's gap is measured from t=0. Returns a NEW list; input untouched.
    public static List<RecordedEvent> RebuildDelaysFromTime(IReadOnlyList<RecordedEvent> retained)
    {
        var result = new List<RecordedEvent>(retained.Count);
        int previousTime = 0;
        foreach (var e in retained)
        {
            int delay = Math.Max(0, e.TimeMs - previousTime);
            previousTime = e.TimeMs;
            result.Add(delay == e.DelayMs ? e : e with { DelayMs = delay });
        }
        return result;
    }

    // Rebuild TimeMs as the cumulative DelayMs and return a NEW list. Used after an
    // edit that changes waits, so DelayMs and TimeMs stay consistent.
    public static List<RecordedEvent> RebuildTimeFromDelays(IReadOnlyList<RecordedEvent> retained)
    {
        var result = new List<RecordedEvent>(retained.Count);
        int time = 0;
        foreach (var e in retained)
        {
            time = checked(time + Math.Max(0, e.DelayMs));
            result.Add(time == e.TimeMs ? e : e with { TimeMs = time });
        }
        return result;
    }
}
