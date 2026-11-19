using System.Xml.Linq;

namespace Metano.Tests.Build;

/// <summary>Models the shared targets of a release predating the #706 contracts.</summary>
internal static class BuildCommonTargetsFixture
{
    private const string FileName = "Metano.Build.Common.targets";

    public static string PathIn(string directory) => Path.Combine(directory, FileName);

    public static XDocument PreContractCopy(string directory, bool withoutCompositionHook = false)
    {
        var common = XDocument.Load(PathIn(directory));
        common.Root!.Elements("PropertyGroup").Elements("_MetanoBuildCommonHash").Remove();

        common
            .Root.Elements("Target")
            .Where(target => (string?)target.Attribute("Name") == "_MetanoCheckCommonTargets")
            .Remove();

        var prepare = TargetOf(common, "_MetanoPrepare");

        prepare.SetAttributeValue(
            "DependsOnTargets",
            ((string)prepare.Attribute("DependsOnTargets")!).Replace(
                "_MetanoCheckCommonTargets;",
                ""
            )
        );

        if (withoutCompositionHook)
            RemoveCompositionHook(common);

        return common;
    }

    private static void RemoveCompositionHook(XDocument common)
    {
        TargetOf(common, "_MetanoComposeArguments").Remove();

        var keys = TargetOf(common, "_MetanoWriteToolchainKeys");
        keys.SetAttributeValue("DependsOnTargets", "_MetanoResolveTargets");

        keys.Elements("ItemGroup")
            .Elements("_MetanoToolchainLine")
            .Where(line =>
                ((string?)line.Attribute("Include"))?.StartsWith(
                    "arguments=",
                    StringComparison.Ordinal
                ) == true
            )
            .Remove();
    }

    private static XElement TargetOf(XDocument common, string name) =>
        common.Root!.Elements("Target").Single(target => (string?)target.Attribute("Name") == name);
}
