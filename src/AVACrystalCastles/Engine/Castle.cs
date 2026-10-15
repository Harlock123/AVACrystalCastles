namespace AVACrystalCastles.Engine;

/// <summary>
/// A castle: its squares and heights, its stairs, the gems on its walkways, and where the bear starts.
/// Each level's castle is built from its number, so a level is the same castle every time.
/// </summary>
public sealed class Castle
{
    public const int Size = Field.Size;
    public Square[,] Squares { get; } = new Square[Size, Size];
    public bool[,] Gems { get; } = new bool[Size, Size];
    public (int I, int J) Start { get; private set; }
    public string Name { get; private set; } = "";

    private static readonly string[] Names =
    [
        "THE AMBER KEEP", "BRAMBLE TOWERS", "THE GLASS CITADEL", "THE WITCH'S ROOST",
        "MOSSWATER HALL", "THE TWIN SPIRES", "THORNHOLD", "THE WITCH'S ROOST",
        "SILVERMERE", "THE SUNKEN STAIRS", "CANDLEFORD", "THE WITCH'S ROOST",
    ];

    public ref Square this[int i, int j] => ref Squares[i, j];

    public int GemCount { get { int n = 0; foreach (bool g in Gems) if (g) n++; return n; } }

    /// <summary>The castle for a level: an octagonal floor, a few pits, raised plateaus stacked up to three high, and stairs enough to reach every square.</summary>
    public static Castle Build(int level)
    {
        var c = new Castle { Name = Names[(level - 1) % Names.Length] };
        var rng = new Random(level * 71 + 3);
        for (int i = 0; i < Size; i++)
            for (int j = 0; j < Size; j++)
                c.Squares[i, j].Walk = i + j >= 3 && i + j <= 2 * Size - 5 && Math.Abs(i - j) <= Size - 4;
        c.Start = (Size / 2, Size - 3);
        // Plateaus, piled up where they overlap.
        int plateaus = 3 + Math.Min(level, 4);
        for (int k = 0; k < plateaus; k++)
        {
            int w = 3 + rng.Next(3), h = 3 + rng.Next(3);
            int i0 = 1 + rng.Next(Size - w - 1), j0 = 1 + rng.Next(Size - h - 3);
            for (int i = i0; i < i0 + w; i++)
                for (int j = j0; j < j0 + h; j++)
                    if (c.Squares[i, j].Walk) c.Squares[i, j].H = Math.Min(3, c.Squares[i, j].H + 1);
        }
        // Pits.
        int pits = 2 + rng.Next(3);
        for (int k = 0; k < pits; k++)
        {
            int i0 = 1 + rng.Next(Size - 3), j0 = 1 + rng.Next(Size - 5);
            for (int i = i0; i < i0 + 1 + rng.Next(2); i++)
                for (int j = j0; j < j0 + 1 + rng.Next(2); j++) c.Squares[i, j].Walk = false;
        }
        // The bear's corner is flat, open ground.
        var (si, sj) = c.Start;
        for (int i = si - 1; i <= si + 1; i++)
            for (int j = sj - 1; j <= sj + 1; j++)
                if (Field.Inside(i, j)) { c.Squares[i, j].Walk = true; c.Squares[i, j].H = 0; }

        // Stairs wherever the bear can't yet get to a square one step up or down from one it can.
        for (int pass = 0; pass < 200; pass++)
        {
            var reach = c.Reachable(c.Start);
            (int I, int J, int Ni, int Nj)? best = null;
            foreach (var (i, j) in Cells())
            {
                if (!reach[i, j]) continue;
                foreach (var (dx, dy) in Ways)
                {
                    int ni = i + dx, nj = j + dy;
                    if (!Field.Inside(ni, nj) || reach[ni, nj] || !c.Squares[ni, nj].Walk || c.Squares[ni, nj].Stair || c.Squares[i, j].Stair) continue;
                    if (Math.Abs(c.Squares[ni, nj].H - c.Squares[i, j].H) != 1) continue;
                    // Take the first found that isn't beside another stair, so stairs spread out.
                    if (best == null || rng.Next(3) == 0) best = (i, j, ni, nj);
                }
            }
            if (best is not { } s) break;
            ref var b = ref c.Squares[s.Ni, s.Nj];
            int ha = c.Squares[s.I, s.J].H;
            if (b.H > ha) { b.UpX = s.Ni - s.I; b.UpY = s.Nj - s.J; }
            else { b.H = ha; b.UpX = s.I - s.Ni; b.UpY = s.J - s.Nj; }
        }
        // What still can't be reached is a gap in the walls.
        var final = c.Reachable(c.Start);
        foreach (var (i, j) in Cells())
        {
            if (!final[i, j]) c.Squares[i, j].Walk = false;
            c.Gems[i, j] = final[i, j] && !c.Squares[i, j].Stair && (i, j) != c.Start;
        }
        return c;
    }

    public static readonly (int X, int Y)[] Ways = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    public static IEnumerable<(int I, int J)> Cells()
    {
        for (int i = 0; i < Size; i++)
            for (int j = 0; j < Size; j++) yield return (i, j);
    }

    /// <summary>The height of a square's edge on the given side: a stair is high on its upper side, low on its lower, and can't be crossed sideways.</summary>
    public double Edge(int i, int j, int dx, int dy)
    {
        var s = Squares[i, j];
        if (!s.Stair) return s.H;
        if (dx == s.UpX && dy == s.UpY) return s.H;
        if (dx == -s.UpX && dy == -s.UpY) return s.H - 1;
        return double.NaN;
    }

    /// <summary>Whether one may walk from a square to its neighbour: both walkable and level where they meet.</summary>
    public bool CanStep(int i, int j, int ni, int nj)
    {
        if (!Field.Inside(i, j) || !Field.Inside(ni, nj)) return false;
        if (!Squares[i, j].Walk || !Squares[ni, nj].Walk) return false;
        int dx = ni - i, dy = nj - j;
        if (Math.Abs(dx) + Math.Abs(dy) != 1) return false;
        double a = Edge(i, j, dx, dy), b = Edge(ni, nj, -dx, -dy);
        return !double.IsNaN(a) && !double.IsNaN(b) && Math.Abs(a - b) < 0.01;
    }

    /// <summary>The ground's height at a point: a stair climbs across its square.</summary>
    public double Z(double x, double y)
    {
        int i = Math.Clamp((int)Math.Floor(x), 0, Size - 1), j = Math.Clamp((int)Math.Floor(y), 0, Size - 1);
        var s = Squares[i, j];
        if (!s.Stair) return s.H;
        double fx = x - i, fy = y - j;
        double t = s.UpX > 0 ? fx : s.UpX < 0 ? 1 - fx : s.UpY > 0 ? fy : 1 - fy;
        return s.H - 1 + Math.Clamp(t, 0, 1);
    }

    public bool[,] Reachable((int I, int J) from)
    {
        var seen = new bool[Size, Size];
        var q = new Queue<(int, int)>();
        seen[from.I, from.J] = true;
        q.Enqueue(from);
        while (q.Count > 0)
        {
            var (i, j) = q.Dequeue();
            foreach (var (dx, dy) in Ways)
            {
                int ni = i + dx, nj = j + dy;
                if (!Field.Inside(ni, nj) || seen[ni, nj] || !CanStep(i, j, ni, nj)) continue;
                seen[ni, nj] = true;
                q.Enqueue((ni, nj));
            }
        }
        return seen;
    }

    /// <summary>The first step from a square towards the nearest square that passes the test, and how far that is; or none.</summary>
    public ((int X, int Y) Step, int Distance)? Toward(int i0, int j0, Func<int, int, bool> goal)
    {
        var first = new (int, int)[Size, Size];
        var dist = new int[Size, Size];
        var seen = new bool[Size, Size];
        var q = new Queue<(int, int)>();
        seen[i0, j0] = true;
        q.Enqueue((i0, j0));
        while (q.Count > 0)
        {
            var (i, j) = q.Dequeue();
            if ((i != i0 || j != j0) && goal(i, j)) return (first[i, j], dist[i, j]);
            foreach (var (dx, dy) in Ways)
            {
                int ni = i + dx, nj = j + dy;
                if (!Field.Inside(ni, nj) || seen[ni, nj] || !CanStep(i, j, ni, nj)) continue;
                seen[ni, nj] = true;
                dist[ni, nj] = dist[i, j] + 1;
                first[ni, nj] = i == i0 && j == j0 ? (dx, dy) : first[i, j];
                q.Enqueue((ni, nj));
            }
        }
        return null;
    }
}
