using MouseClicker.Domain;

namespace MouseClicker.Storage;

// Non-destructive recording optimization. Optimize() returns a NEW document and
// never mutates the source; the caller saves it to a new file ("另存"), leaving the
// loaded recording and the original file byte-for-byte untouched.
//
// Rule (conservative, never changes WHEN a kept action happens):
//   * Same-coordinate de-dup: drop a mouse_move on the same point as the previous
//     KEPT event (no-op jitter / repeated identical moves).
//   * Optional trajectory thinning (epsilonPx > 0): inside each contiguous run of
//     mouse_move, a Ramer-Douglas-Peucker pass drops only moves that stay within
//     epsilonPx of the kept line. The run endpoints, the first move after a press
//     and the last move before a release (drag start / drag endpoint) are FORCED
//     kept, so corners and drag geometry survive.
//   * A removed event's DelayMs folds FORWARD into the next kept event, so every
//     kept action keeps its original TimeMs. The final event is never dropped; a
//     trailing same-spot move carries the tail wait instead of delaying a release.
//   * Rebuild TimeMs as the cumulative DelayMs: total duration and every kept
//     action's time are preserved and DelayMs/TimeMs stay consistent.
// Mouse buttons, wheel and key events are always kept.
public static class RecordingOptimizer
{
    // Conservative default for the UI: points within 2 physical pixels of the kept
    // polyline are treated as straight-line jitter.
    public const int DefaultThinEpsilonPx = 2;
    public const int MaxThinEpsilonPx = 200;

    public sealed record OptimizeResult(RecordingDocument Document, int BeforeCount, int RemovedCount, int ThinnedCount)
    {
        public int AfterCount => Document.Events.Count;
    }

    // De-dup only (no trajectory thinning). Preserves prior behaviour.
    public static OptimizeResult Optimize(RecordingDocument source) => Optimize(source, 0);

    public static OptimizeResult Optimize(RecordingDocument source, int epsilonPx)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Events.Count == 0) throw new ArgumentException("录制为空，没有可优化的动作。");
        if (epsilonPx < 0) throw new ArgumentException("抽稀容差不能为负。");
        if (epsilonPx > MaxThinEpsilonPx) throw new ArgumentException($"抽稀容差不能超过 {MaxThinEpsilonPx} 像素。");

        var src = source.Events;
        int n = src.Count;
        var remove = new bool[n];

        // A move made WHILE a mouse button is held is part of a drag. Playback has no
        // movement interpolation, so thinning a drag would change the path/speed; such
        // moves are kept verbatim. Track the held-button count across the document.
        var inDrag = new bool[n];
        int held = 0;
        for (int i = 0; i < n; i++)
        {
            var t = src[i].Type;
            if (t == RecordingEventType.MouseDown) held++;
            inDrag[i] = t == RecordingEventType.MouseMove && held > 0;
            if (t == RecordingEventType.MouseUp) held = Math.Max(0, held - 1);
        }

        // ---- Stage A: same-coordinate de-dup (never touches drag moves / final event). ------
        int lastX = 0, lastY = 0;
        bool haveLast = false;
        for (int i = 0; i < n; i++)
        {
            var e = src[i];
            bool isMove = e.Type == RecordingEventType.MouseMove;
            if (isMove && !inDrag[i] && i < n - 1 && haveLast && e.X == lastX && e.Y == lastY)
                remove[i] = true;
            if (!remove[i] && (isMove || e.Type is RecordingEventType.MouseDown or RecordingEventType.MouseUp
                    or RecordingEventType.MouseWheel))
            { lastX = e.X; lastY = e.Y; haveLast = true; }
        }
        int dedupCount = 0;
        for (int i = 0; i < n; i++) if (remove[i]) dedupCount++;

        // ---- Stage B: optional RDP trajectory thinning inside move runs. ----------
        int thinnedCount = 0;
        if (epsilonPx > 0)
        {
            int run = 0;
            while (run < n)
            {
                if (src[run].Type != RecordingEventType.MouseMove) { run++; continue; }
                int end = run;
                while (end + 1 < n && src[end + 1].Type == RecordingEventType.MouseMove) end++;

                // Eligible geometry points = moves in this run not already de-duped.
                var pts = new List<int>();
                for (int k = run; k <= end; k++) if (!remove[k]) pts.Add(k);
                if (pts.Count >= 3)
                {
                    var keepPoint = new bool[pts.Count];
                    bool Forced(int p) =>
                        p == 0 || p == pts.Count - 1 || inDrag[pts[p]] || IsDragAnchor(pts[p]);
                    for (int p = 0; p < pts.Count; p++) keepPoint[p] = Forced(p);

                    // RDP each sub-range between consecutive forced anchors.
                    for (int s = 0; s < pts.Count - 1;)
                    {
                        int e = s + 1;
                        while (e < pts.Count - 1 && !Forced(e)) e++;
                        if (Forced(e)) RdpKeep(src, pts, s, e, keepPoint, epsilonPx);
                        s = e;
                    }

                    for (int p = 0; p < pts.Count; p++)
                        if (!keepPoint[p] && !remove[pts[p]]) { remove[pts[p]] = true; thinnedCount++; }
                }
                run = end + 1;
            }
        }

        // ---- Single forward fold: removed DelayMs -> next kept event. -------------
        var kept = new List<RecordedEvent>(n);
        int carry = 0;
        for (int i = 0; i < n; i++)
        {
            if (remove[i]) { carry += src[i].DelayMs; continue; }
            kept.Add(src[i] with { DelayMs = src[i].DelayMs + carry });
            carry = 0;
        }
        // Defensive: a leftover carry can only land on a trailing move, which is
        // force-kept; never extend a preceding press/release/wheel/key.
        if (carry > 0 && kept.Count > 0 && kept[^1].Type == RecordingEventType.MouseMove)
            kept[^1] = kept[^1] with { DelayMs = kept[^1].DelayMs + carry };
        if (kept.Count == 0) throw new InvalidOperationException("优化后没有任何动作，已取消。");

        int time = 0;
        for (int i = 0; i < kept.Count; i++)
        {
            time += kept[i].DelayMs;
            kept[i] = kept[i] with { TimeMs = time };
        }

        var result = source with { Events = kept, Name = source.Name + "（优化）" };
        return new OptimizeResult(result, n, dedupCount + thinnedCount, thinnedCount);

        // A move is a drag anchor when it leaves a press or reaches a release.
        bool IsDragAnchor(int idx) =>
            (idx > 0 && src[idx - 1].Type == RecordingEventType.MouseDown) ||
            (idx + 1 < n && src[idx + 1].Type == RecordingEventType.MouseUp);
    }

    // The events the optimization dropped, in source order, for the preview/result
    // comparison. Only mouse_move events are ever removed, and a kept event keeps its
    // original TimeMs, so a removed move is a source move whose (X,Y,TimeMs) is not in
    // the optimized move multiset. Pure function: neither document is modified.
    public static List<RecordedEvent> RemovedEvents(RecordingDocument source, RecordingDocument optimized)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(optimized);
        var remaining = new Dictionary<(int x, int y, int t), int>();
        foreach (var e in optimized.Events)
        {
            if (e.Type != RecordingEventType.MouseMove) continue;
            var key = (e.X, e.Y, e.TimeMs);
            remaining[key] = remaining.GetValueOrDefault(key) + 1;
        }
        var removed = new List<RecordedEvent>();
        foreach (var e in source.Events)
        {
            if (e.Type != RecordingEventType.MouseMove) continue;
            var key = (e.X, e.Y, e.TimeMs);
            if (remaining.TryGetValue(key, out int count) && count > 0) { remaining[key] = count - 1; continue; }
            removed.Add(e);
        }
        return removed;
    }

    // RDP over pts positions [s,e]: keep the interior point farthest from chord
    // (s,e) when its perpendicular distance exceeds epsilon, then split & repeat.
    private static void RdpKeep(IReadOnlyList<RecordedEvent> src, List<int> pts,
        int s, int e, bool[] keep, double epsilon)
    {
        var stack = new Stack<(int a, int b)>();
        stack.Push((s, e));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            var A = src[pts[a]];
            var B = src[pts[b]];
            double max = -1;
            int imax = -1;
            for (int k = a + 1; k < b; k++)
            {
                double d = Perpendicular(A.X, A.Y, B.X, B.Y, src[pts[k]].X, src[pts[k]].Y);
                if (d > max) { max = d; imax = k; }
            }
            if (imax >= 0 && max > epsilon)
            {
                keep[imax] = true;
                stack.Push((a, imax));
                stack.Push((imax, b));
            }
        }
    }

    // Perpendicular distance of point P from segment AB (physical pixels, double).
    private static double Perpendicular(int ax, int ay, int bx, int by, int px, int py)
    {
        double dx = bx - ax, dy = by - ay;
        double len2 = dx * dx + dy * dy;
        if (len2 == 0) return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
        double t = ((px - ax) * dx + (py - ay) * dy) / len2;
        t = Math.Clamp(t, 0, 1);
        double cx = ax + t * dx, cy = ay + t * dy;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }
}
