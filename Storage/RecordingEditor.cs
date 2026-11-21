using MouseClicker.Domain;

namespace MouseClicker.Storage;

// Non-destructive event editing helpers. Every edit returns a NEW document (the
// loaded object and original file stay untouched), recomputes the timeline and
// runs the same full validation as load/save. Pair operations understand both
// keyboard and mouse down/up events, including repeated nested presses.
public static class RecordingEditor
{
    // Outcome of a paired operation: the new document, whether the pair was really
    // kept/removed, the affected partner index (-1 when none) and how many events the
    // operation touched. The UI must use Paired to decide what it tells the user.
    public sealed record PairEditResult(RecordingDocument Document,bool Paired,int PartnerIndex,int AffectedCount);

    public static RecordingDocument Replace(RecordingDocument source,int index,RecordedEvent value)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (index < 0 || index >= source.Events.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "动作序号超出范围。");
        var events=source.Events.ToList();events[index]=value;return Rebuild(source,events);
    }

    // Pair-aware replace: writes the edited event at index and, ONLY when the value
    // stays in the SAME pair kind (keyboard down stays keyboard down, mouse up stays
    // mouse up), syncs the identity fields (Vk/Scan/Extended or the button name) onto
    // the other members of the hold group - the paired release and/or the auto-repeat
    // presses of the same hold. The other members keep their own type, timing and
    // coordinates. Changing family (keyboard<->mouse), changing direction (down<->up)
    // or replacing with move/wheel CANNOT preserve the pairing, so only the selected
    // event is written and the result reports Paired=false (the caller must say so
    // instead of claiming a paired edit). An unpaired event degrades to Replace.
    public static PairEditResult ReplacePair(RecordingDocument source,int index,RecordedEvent value)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(value);
        if (index < 0 || index >= source.Events.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "动作序号超出范围。");
        var events=source.Events.ToList();
        var original=events[index];
        var group=FindGroup(events,index);
        int partner=-1;
        if(original.Type==value.Type && IsPairKind(original) && group.Count>1)
        {
            bool keyboard=IsKeyboard(original);
            foreach(var i in group)
            {
                if(i==index) continue;
                events[i]=keyboard
                    ? events[i] with{Vk=value.Vk,Scan=value.Scan,Extended=value.Extended}
                    : events[i] with{Button=value.Button};
                if(partner<0) partner=i;
            }
        }
        events[index]=value;
        bool paired=partner>=0;
        return new PairEditResult(Rebuild(source,events),paired,paired?partner:-1,paired?group.Count:1);
    }

    // Index of the partner that a paired operation would affect for events[index], or
    // -1 when the event is not a press/release or has no partner at all. Pairing is
    // HOLD-GROUP based: a run of consecutive same-identity presses (the real press plus
    // its keyboard auto-repeat downs) is ONE hold, and its single release belongs to
    // that whole group. The identity is Vk + Extended for keys and the button name for
    // mouse buttons; the hardware scan code is deliberately NOT part of the identity so
    // that a legacy/edited half with Scan=0 still pairs with its counterpart (Extended
    // already separates main Enter from numpad Enter).
    public static int FindPairIndex(RecordingDocument source,int index)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (index < 0 || index >= source.Events.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "动作序号超出范围。");
        var group=FindGroup(source.Events,index);
        if(group.Count<2) return -1;
        bool down=IsDown(source.Events[index]);
        // Prefer the opposite half (the release for a press, the press for a release);
        // fall back to another member (an auto-repeat of the same hold).
        int preferred=-1;
        foreach(var i in group)
        {
            if(i==index) continue;
            if(down==IsUp(source.Events[i])) { preferred=i; break; }
            if(preferred<0) preferred=i;
        }
        return preferred;
    }

    // Every index of the hold group that contains events[index]: the press(es) of one
    // hold (real press + auto-repeats) and its release when one exists. Used by the
    // editor to describe exactly what a paired operation will touch.
    public static IReadOnlyList<int> FindHoldGroup(RecordingDocument source,int index)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (index < 0 || index >= source.Events.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "动作序号超出范围。");
        return FindGroup(source.Events,index);
    }

    public static RecordingDocument Insert(RecordingDocument source,int index,RecordedEvent value,bool paired)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (index < 0 || index > source.Events.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "插入位置超出范围。");
        var events=source.Events.ToList();events.Insert(index,value);
        if(paired && value.Type is RecordingEventType.KeyDown or RecordingEventType.MouseDown)
            events.Insert(index+1,value with{Type=value.Type==RecordingEventType.KeyDown?RecordingEventType.KeyUp:RecordingEventType.MouseUp,DelayMs=50});
        return Rebuild(source,events);
    }
    // Paired delete removes the WHOLE hold group (the press, its auto-repeats and the
    // release) so no half of a hold can be left behind; when the group has no release
    // (or an orphan release has no press) only the selected event is removed and the
    // result reports Paired=false so the UI can say exactly that. Deleting the last
    // remaining event is still refused by the document validation.
    public static PairEditResult Delete(RecordingDocument source,int index,bool paired)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (index < 0 || index >= source.Events.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "动作序号超出范围。");

        var events=source.Events.ToList();
        int partner=-1;
        var affected=new List<int>{index};
        if(paired)
        {
            var group=FindGroup(events,index);
            bool hasDown=group.Any(i=>IsDown(events[i]));
            bool hasUp=group.Any(i=>IsUp(events[i]));
            if(hasDown && hasUp)
            {
                affected=group.ToList();
                partner=group.First(i=>i!=index);
            }
        }
        // Remove the higher indexes first so the lower ones stay valid.
        foreach(var i in affected.OrderByDescending(i=>i)) events.RemoveAt(i);
        bool pairedDone=affected.Count>1;
        return new PairEditResult(Rebuild(source,events),pairedDone,pairedDone?partner:-1,affected.Count);
    }

    // The hold group of events[index]. Auto-repeat rule: consecutive same-identity
    // presses with no release between them belong to ONE hold (a second physical press
    // of the same key/button without releasing it first is not a real input pattern),
    // so they are NOT treated as nested presses. Events of other identities in between
    // are skipped (holding A while tapping B does not end A's hold).
    private static List<int> FindGroup(IReadOnlyList<RecordedEvent> events, int index)
    {
        var selected = events[index];
        if (!IsPairKind(selected)) return [];
        var group = new List<int>();
        if (IsDown(selected))
        {
            int start = index;
            for (int i = index - 1; i >= 0; i--)
            {
                var candidate = events[i];
                if (!SameIdentity(selected, candidate)) continue;
                if (IsDown(candidate)) { start = i; continue; }
                break; // a same-identity release ends the previous hold
            }
            for (int i = start; i < events.Count; i++)
            {
                var candidate = events[i];
                if (!SameIdentity(selected, candidate)) continue;
                group.Add(i);
                if (IsUp(candidate)) break; // the release closes this hold
            }
        }
        else
        {
            int first = -1;
            for (int i = index - 1; i >= 0; i--)
            {
                var candidate = events[i];
                if (!SameIdentity(selected, candidate)) continue;
                if (IsDown(candidate)) { first = i; continue; }
                break; // an earlier release belongs to an earlier hold
            }
            if (first < 0) group.Add(index);            // orphan release: only itself
            else for (int i = first; i <= index; i++)
                if (SameIdentity(selected, events[i])) group.Add(i);
        }
        return group;
    }

    // Pair identity: a candidate can only join a hold group of its OWN family and
    // direction kind — keyboard candidates must be KeyDown/KeyUp sharing Vk+Extended,
    // mouse candidates must be MouseDown/MouseUp sharing the button name. Move and
    // wheel events NEVER pair, even when edited or legacy data left a matching Button
    // field on them; otherwise a paired delete/replace would silently take unrelated
    // move/wheel events with it. Scan is excluded on purpose (see FindPairIndex);
    // the Extended flag is what separates main Enter from numpad Enter.
    private static bool SameIdentity(RecordedEvent selected, RecordedEvent candidate)
    {
        if (IsKeyboard(selected))
            return IsKeyboard(candidate)
                && selected.Vk == candidate.Vk && selected.Extended == candidate.Extended;
        if (IsMouseButton(selected))
            return IsMouseButton(candidate)
                && string.Equals(selected.Button, candidate.Button, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static bool IsKeyboard(RecordedEvent e) => e.Type is RecordingEventType.KeyDown or RecordingEventType.KeyUp;
    private static bool IsMouseButton(RecordedEvent e) => e.Type is RecordingEventType.MouseDown or RecordingEventType.MouseUp;
    private static bool IsPairKind(RecordedEvent e) => IsKeyboard(e) || IsMouseButton(e);

    private static bool IsDown(RecordedEvent e) => e.Type is RecordingEventType.KeyDown or RecordingEventType.MouseDown;
    private static bool IsUp(RecordedEvent e) => e.Type is RecordingEventType.KeyUp or RecordingEventType.MouseUp;
    private static RecordingDocument Rebuild(RecordingDocument source,List<RecordedEvent> events)
    {
        List<RecordedEvent> rebuilt;
        try { rebuilt=RecordingTimeline.RebuildTimeFromDelays(events); }
        catch (OverflowException)
        {
            // Same limit as load/save: the cumulative timeline must fit the schema.
            throw new InvalidDataException("编辑后录制累计时间超出支持范围，已保持上一版。");
        }
        var result=source with{Events=rebuilt};RecordingStore.Validate(result);return result;
    }
    public static RecordingDocument BindToWindow(RecordingDocument source,MouseClicker.Platform.WindowTargetInfo target,WindowBinding binding)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(binding);
        if(source.WindowBind is not null)throw new InvalidDataException("此录制已绑定窗口，请先解除绑定。");
        RecordingStore.Validate(source);
        if (target.ClientWidth != binding.ClientWidth || target.ClientHeight != binding.ClientHeight || target.Dpi != binding.Dpi)
            throw new InvalidDataException("目标窗口当前尺寸或 DPI 与绑定信息不一致，未转换。");
        if(source.Dpi!=binding.Dpi)throw new InvalidDataException("录制 DPI 与目标窗口不一致，请在相同缩放下转换。");
        var events=new List<RecordedEvent>();
        foreach(var e in source.Events)
        {
            if(!RecordingEventType.IsMouse(e.Type)){events.Add(e);continue;}
            if(!MouseClicker.Platform.WindowTargetResolver.ScreenToClientPoint(target.Hwnd,e.X,e.Y,out int x,out int y)||x<0||y<0||x>=binding.ClientWidth||y>=binding.ClientHeight)
                throw new InvalidDataException("录制含目标客户区之外的鼠标事件，未转换。请把目标放回录制时的位置。");
            events.Add(e with{X=x,Y=y});
        }
        var result=source with{Events=events,WindowBind=binding.Clone()};RecordingStore.Validate(result);return result;
    }
    public static RecordingDocument Unbind(RecordingDocument source,MouseClicker.Platform.WindowTargetInfo target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if(source.WindowBind is null)return source;
        RecordingStore.Validate(source);
        if (target.ClientWidth != source.WindowBind.ClientWidth || target.ClientHeight != source.WindowBind.ClientHeight || target.Dpi != source.WindowBind.Dpi)
            throw new InvalidDataException("目标窗口当前尺寸或 DPI 与绑定信息不一致，未解除绑定。");
        var events=source.Events.Select(e=>{if(!RecordingEventType.IsMouse(e.Type))return e;if(!MouseClicker.Platform.WindowTargetResolver.ClientToScreenPoint(target.Hwnd,e.X,e.Y,out int x,out int y))throw new InvalidDataException("坐标转换失败。");return e with{X=x,Y=y};}).ToList();
        var vs=System.Windows.Forms.SystemInformation.VirtualScreen;
        var result = source with{WindowBind=null,Events=events,Dpi=target.Dpi,VirtualLeft=vs.Left,VirtualTop=vs.Top,VirtualWidth=vs.Width,VirtualHeight=vs.Height};
        RecordingStore.Validate(result);
        return result;
    }
    public static int TotalDurationMs(RecordingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int total = 0;
        foreach (var e in document.Events) total = checked(total + Math.Max(0, e.DelayMs));
        return total;
    }

    // Set the gap before events[index] to newDelayMs. Throws on an out-of-range index,
    // negative/overflow value or a result that fails document validation.
    public static RecordingDocument SetDelay(RecordingDocument document, int index, int newDelayMs)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (index < 0 || index >= document.Events.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "动作序号超出范围。");
        if (newDelayMs < 0) throw new ArgumentException("等待时间不能为负。");
        if (newDelayMs > RecordingSchema.MaxDelayMs)
            throw new ArgumentException($"单个等待不能超过 {RecordingSchema.MaxDelayMs} ms。");

        var events = new List<RecordedEvent>(document.Events)
        {
            [index] = document.Events[index] with { DelayMs = newDelayMs }
        };
        try { events = RecordingTimeline.RebuildTimeFromDelays(events); }
        catch (OverflowException)
        {
            // Same limit as load/save: the cumulative timeline must fit the schema.
            throw new InvalidDataException("编辑后录制累计时间超出支持范围，已保持上一版。");
        }

        var edited = document with { Events = events };
        // Same gate used on load/save: an edited timeline must be fully valid.
        RecordingStore.Validate(edited);
        return edited;
    }
}
