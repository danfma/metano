using Metano.Annotations;
using SampleIssueTracker.Issues.Domain;
using SampleIssueTracker.SharedKernel;

namespace SampleIssueTracker.Issues.Application;

[Erasable]
public static class IssueQueries
{
    public static IReadOnlyList<Issue> OpenIssues(IReadOnlyList<Issue> issues) =>
        issues
            .Where(issue => !issue.IsClosed)
            .OrderByDescending(issue => issue.Priority)
            .ThenBy(issue => issue.Title)
            .ToList();

    public static Dictionary<IssueStatus, int> StatusCounts(IReadOnlyList<Issue> issues) =>
        issues
            .GroupBy(issue => issue.Status)
            .ToDictionary(group => group.Key, group => group.Count());

    public static IReadOnlyList<Issue> IssuesForAssignee(
        IReadOnlyList<Issue> issues,
        UserId assigneeId
    ) =>
        issues
            .Where(issue => issue.AssigneeId == assigneeId)
            .OrderBy(issue => issue.Status)
            .ThenByDescending(issue => issue.Priority)
            .ToList();

    public static IReadOnlyList<Issue> ReadyForReview(IReadOnlyList<Issue> issues, int limit) =>
        issues
            .Where(issue => issue.Status is IssueStatus.InProgress or IssueStatus.InReview)
            .Where(issue => issue.Priority is IssuePriority.High or IssuePriority.Urgent)
            .Take(limit)
            .ToList();
}
