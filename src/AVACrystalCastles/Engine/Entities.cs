namespace AVACrystalCastles.Engine;

/// <summary>The stick (eight ways, as on the screen) and the jump button.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Jump = false);

/// <summary>
/// A castle is a grid of squares seen from above at an angle: X runs down to the right on screen, Y down
/// to the left. Each square has a height; stairs join one height to the next. Positions are in squares,
/// a square's centre at (i + ½, j + ½).
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Top = 64;
    public const int Size = 14;
    /// <summary>A square's size on screen (across, down) and one step of height.</summary>
    public const double TW = 68, TH = 34, HZ = 27;
    /// <summary>Where the castle's top corner sits on screen.</summary>
    public const double OX = Width / 2, OY = 128;

    public static bool Inside(int i, int j) => i >= 0 && i < Size && j >= 0 && j < Size;
}

/// <summary>One square of the castle.</summary>
public struct Square
{
    public bool Walk;
    public int H;
    /// <summary>A stair: the way up (dx, dy), from the square below on the other side, at H − 1, to H.</summary>
    public int UpX, UpY;
    public bool Stair => UpX != 0 || UpY != 0;
}

public sealed class Bear
{
    public double X, Y;
    /// <summary>Height in the air from a jump.</summary>
    public double Lift, Vz;
    public int FaceX = 1, FaceY = 1;
    public bool Alive = true;
    public double Walk;
    /// <summary>Wearing the magic hat: nothing can hurt it, and it can squash anything.</summary>
    public double Hat;
    public bool Airborne => Lift > 0.25;
}

public enum EnemyKind { Grub, Orb, Tree, Witch }

/// <summary>Something that walks the castle square to square.</summary>
public sealed class Enemy
{
    public EnemyKind Kind;
    public double X, Y;
    /// <summary>The square it is heading to, and where it started.</summary>
    public int ToI, ToJ, HomeI, HomeJ;
    /// <summary>A grub eating a gem: the only time it can be squashed without the hat.</summary>
    public double Munch;
    public double Age;
    public bool Dead;

    public int Points => Kind switch { EnemyKind.Grub => 500, EnemyKind.Orb => 300, EnemyKind.Tree => 700, _ => 5000 };
}

public sealed class Swarm
{
    public double X, Y;
    public double Age;
    public bool Active;
}

public enum EffectKind { Score, Sparkle, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Z, Age, Life = 0.9;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
