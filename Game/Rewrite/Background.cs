using System;
using System.Threading.Tasks;

namespace Thornwake.Rewrite;

/// <summary>
/// Background work (terrain tiles, tree cells, horizon, weather and wind maps) on the thread pool, with a limit on how
/// many run at once. Tom's build 45 benchmark: with one tile builder per core plus the tree field's (31 + 16 on his 32
/// threads), the main thread was pushed off the CPU mid-frame (a ~0.7 ms collision piece took up to 40 ms). Build 46
/// also ran them below normal priority; build 47 drops that: Windows schedules strictly by priority, so on a 4–6 core
/// PC the builders could starve behind the game's own threads (seen here under a CPU renderer: the tree field never
/// finished). Fewer builders already leave cores for the main and render threads.
/// </summary>
public static class Background
{
    /// <summary>Tile builders running at once: leaves cores for the main thread, the render thread and Godot's own.</summary>
    public static int TileJobs => Math.Clamp(Environment.ProcessorCount - 2, 1, 12);

    /// <summary>Tree field cell builders running at once.</summary>
    public static int CellJobs => Math.Clamp(Environment.ProcessorCount / 4, 2, 6);

    public static Task<T> Run<T>(Func<T> work) => Task.Run(work);
}
