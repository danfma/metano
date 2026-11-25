namespace System.Runtime.CompilerServices
{
    // The closed-hierarchy marker the .NET 11 BCL ships; the probes target .NET 10.
    internal sealed class IsClosedTypeAttribute : Attribute;
}

namespace Conformance
{
    /// <summary>A closed record family with a closed intermediate level (spec 010 FR-C1).</summary>
    public closed record Job;

    public sealed record JobQueued : Job;

    public sealed record JobRunning(int Percent) : Job;

    public closed record JobFinished : Job;

    public sealed record JobDone(int Seconds) : JobFinished;

    public sealed record JobFailed(string Error) : JobFinished;

    /// <summary>A closed record whose cases are nested in it (the #726 shape).</summary>
    public closed record Take
    {
        public sealed record Fresh : Take;

        public sealed record Reused(int From) : Take;
    }

    /// <summary>A generic closed record.</summary>
    public closed record Grove<T>;

    public sealed record GroveLeaf<T>(T Value) : Grove<T>;

    public sealed record GroveBranch<T>(Grove<T> Left, Grove<T> Right) : Grove<T>;

    /// <summary>Exhaustive switches over closed types, with no catch-all arm (spec 010 FR-C2).</summary>
    public static class ClosedHierarchyProbes
    {
        public static string ExhaustiveSwitch()
        {
            Job[] jobs =
            [
                new JobQueued(),
                new JobRunning(40),
                new JobDone(3),
                new JobFailed("disk"),
            ];

            return string.Join("|", jobs.Select(Describe))
                + "|"
                + string.Join(",", jobs.Select(Stage));
        }

        public static string NestedCases()
        {
            Take[] takes = [new Take.Fresh(), new Take.Reused(7)];

            return string.Join("|", takes.Select(Label))
                + "|"
                + (takes[0] is Take.Fresh)
                + "|"
                + (takes[1] is Take.Fresh);
        }

        public static string GenericClosed()
        {
            Grove<int> tree = new GroveBranch<int>(
                new GroveLeaf<int>(1),
                new GroveBranch<int>(new GroveLeaf<int>(2), new GroveLeaf<int>(3))
            );

            return Sum(tree) + "|" + Leaves(tree);
        }

        public static string IsPatterns()
        {
            Job done = new JobDone(9);
            Job queued = new JobQueued();

            return (done is JobFinished)
                + "|"
                + (queued is JobFinished)
                + "|"
                + (done is JobDone { Seconds: > 5 })
                + "|"
                + done.Equals(new JobDone(9));
        }

        private static string Describe(Job job) =>
            job switch
            {
                JobQueued => "queued",
                JobRunning running => "running " + running.Percent,
                JobDone done => "done " + done.Seconds,
                JobFailed failed => "failed " + failed.Error,
            };

        private static string Stage(Job job) =>
            job switch
            {
                JobQueued => "waiting",
                JobRunning => "busy",
                JobFinished => "over",
            };

        private static string Label(Take take) =>
            take switch
            {
                Take.Fresh => "fresh",
                Take.Reused reused => "reused " + reused.From,
            };

        private static int Sum(Grove<int> node) =>
            node switch
            {
                GroveLeaf<int> leaf => leaf.Value,
                GroveBranch<int> branch => Sum(branch.Left) + Sum(branch.Right),
            };

        private static int Leaves<T>(Grove<T> node) =>
            node switch
            {
                GroveLeaf<T> => 1,
                GroveBranch<T>(var left, var right) => Leaves(left) + Leaves(right),
            };
    }
}
