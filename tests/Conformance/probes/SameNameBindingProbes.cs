using Metano.Annotations;

namespace Conformance.Renamed
{
    [Name("Shoot")]
    public class Sprout
    {
        public string Who() => "Renamed.Sprout";
    }
}

namespace Conformance.Bud
{
    public class Shoot
    {
        public string Who() => "Bud.Shoot";
    }
}

namespace Conformance.Zz
{
    public class Shoot
    {
        public string Who() => "Zz.Shoot";
    }
}

namespace Conformance
{
    /// <summary>
    /// #743 review M3: a <c>[Name("Shoot")]</c> type referenced by its C# name imports its
    /// own module, not the last registered <c>Shoot</c>.
    /// </summary>
    public static class SameNameRenamedProbes
    {
        public static string RenamedTypeByCSharpName() => new Renamed.Sprout().Who();
    }
}

namespace Conformance.AliasUse
{
    using BudShoot = Conformance.Bud.Shoot;

    /// <summary>
    /// #743 review M4: a <c>using</c> alias of one of three <c>Shoot</c> types imports
    /// the alias target, not the last registered <c>Shoot</c>.
    /// </summary>
    public static class SameNameAliasProbes
    {
        public static string AliasTarget() => new BudShoot().Who();
    }
}
