using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>Global registry of all live hooks, used for diagnostics and host shutdown.</summary>
public static class HookManager
{
    /// <summary>Number of currently applied hooks.</summary>
    public static int AppliedCount
    {
        get { lock (HookState.Active) return HookState.Active.Count; }
    }

    /// <summary>
    ///     Returns a one-line summary of the applied hooks ("Type.Method(slot) ..."),
    ///     truncated to at most <paramref name="limit"/> entries.
    /// </summary>
    public static string Describe(int limit = 12)
    {
        List<Hook> snapshot;
        lock (HookState.Active) snapshot = new(HookState.Active);
        if (snapshot.Count == 0) return "no hooks applied";

        var text = new StringBuilder();
        for (var i = 0; i < snapshot.Count && i < limit; i++)
        {
            if (i > 0) text.Append("; ");
            text.Append(snapshot[i].Label);
        }
        if (snapshot.Count > limit) text.Append($"; ... {snapshot.Count} total");
        return text.ToString();
    }

    /// <summary>Undoes every applied hook (host shutdown / tests).</summary>
    public static void UndoAll()
    {
        List<Hook> snapshot;
        lock (HookState.Active) snapshot = new(HookState.Active);
        foreach (var hook in snapshot)
        {
            try { hook.Dispose(); }
            catch (Exception e) { HookLog.Fail("hook:undo-all", e); }
        }
    }
}
