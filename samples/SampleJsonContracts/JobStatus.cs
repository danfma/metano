namespace System.Runtime.CompilerServices
{
    // The closed-hierarchy marker the .NET 11 BCL ships; the sample targets .NET 10.
    internal sealed class IsClosedTypeAttribute : Attribute;
}

namespace SampleJsonContracts
{
    /// <summary>
    /// A closed family (spec 010 FR-C5, #738): in TypeScript <c>JobStatus</c> is the
    /// union of its cases, narrowed by the read-only <c>kind</c> discriminant. The
    /// hand-written consumer is <c>test/closed-family.test.ts</c>.
    /// </summary>
    public closed record JobStatus
    {
        public static string Family => "jobs";

        public JobStatus Self() => this;
    }

    public sealed record JobQueued : JobStatus;

    public sealed record JobRunning(int Percent) : JobStatus;

    public closed record JobFinished : JobStatus;

    public sealed record JobCompleted(double Seconds) : JobFinished;

    [Metano.Annotations.Name("JobError")]
    public sealed record JobFailed(string Error) : JobFinished;

    public static class JobStatuses
    {
        public static JobFinished? AsFinished(object value) => value as JobFinished;

        public static string Describe(JobStatus status) =>
            status switch
            {
                JobQueued => "queued",
                JobRunning running => $"running {running.Percent}",
                JobCompleted completed => $"done in {completed.Seconds}",
                JobFailed failed => $"failed: {failed.Error}",
            };
    }
}
