namespace Metano.Compiler;

/// <summary>
/// Parses the repeated <c>--asset</c> CLI flag values produced by the
/// MSBuild target into <see cref="AssetSpec"/> instances. Each value is
/// either a bare absolute path (<c>SRC</c>) or a <c>SRC=DEST</c> pair
/// where <c>DEST</c> is interpreted by the host as a path relative to
/// <see cref="TranspileOptions.OutputDir"/>.
/// </summary>
public static class AssetFlagParser
{
    public static IReadOnlyList<AssetSpec> Parse(string[]? rawValues)
    {
        if (rawValues is null || rawValues.Length == 0)
            return [];

        var assets = new List<AssetSpec>(rawValues.Length);
        foreach (var raw in rawValues)
        {
            var value = raw?.Trim();
            if (string.IsNullOrEmpty(value))
                continue;

            var splitIndex = value.IndexOf('=');
            if (splitIndex < 0)
            {
                assets.Add(new AssetSpec(value));
                continue;
            }

            var source = value[..splitIndex].Trim();
            var destination = value[(splitIndex + 1)..].Trim();
            if (string.IsNullOrEmpty(source))
                continue;
            assets.Add(
                new AssetSpec(
                    source,
                    RelativeDestination: string.IsNullOrEmpty(destination) ? null : destination
                )
            );
        }
        return assets;
    }
}
