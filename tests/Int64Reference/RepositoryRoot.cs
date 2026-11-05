namespace Int64Reference;

/// <summary>The repository root: the nearest directory above the working directory that holds <c>Metano.slnx</c>.</summary>
internal static class RepositoryRoot
{
    private const string SolutionFile = "Metano.slnx";

    public static string Find()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (
            directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFile))
        )
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"No {SolutionFile} above '{Directory.GetCurrentDirectory()}'; run from the repository."
            );
    }
}
