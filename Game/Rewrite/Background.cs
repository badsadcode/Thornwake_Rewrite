using System;
using System.Threading;
using System.Threading.Tasks;

namespace Thornwake.Rewrite;

/// <summary>
/// Background work (terrain tiles, tree cells, horizon, weather and wind maps) on the thread pool at below-normal
/// priority. Tom's build 45 benchmark: with one tile builder per core plus the tree field's, the main thread was pushed
/// off the CPU mid-frame (a 1,024-triangle collision piece, normally ~0.7 ms, took up to 40 ms; mesh and object commits
/// spiked in the same frames). The main and render threads must always win.
/// </summary>
public static class Background
{
    /// <summary>Tile builders running at once: leaves cores for the main thread, the render thread and Godot's own.</summary>
    public static int TileJobs => Math.Clamp(Environment.ProcessorCount - 2, 1, 12);

    /// <summary>Tree field cell builders running at once.</summary>
    public static int CellJobs => Math.Clamp(Environment.ProcessorCount / 4, 1, 6);

    public static Task<T> Run<T>(Func<T> work) => Task.Run(() =>
    {
        var thread = Thread.CurrentThread;
        var priority = thread.Priority;
        try { thread.Priority = ThreadPriority.BelowNormal; } catch (Exception) { /* not supported here: run as is */ }
        try { return work(); }
        finally { try { thread.Priority = priority; } catch (Exception) { } }
    });
}
