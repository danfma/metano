namespace Conformance;

// Wave 10 group A: the other half of a partial class whose initializer a
// constructor parameter shadows (#795, W10-1b).

public partial class FsPartialShadow
{
    private readonly int _x = FsFlat.Twice();

    public int X => _x;
}
