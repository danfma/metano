using System.Collections.Generic;
using System.Linq;
using Metano.Annotations;

namespace SampleQueryableArrays;

/// <summary>
/// Each query lifts the source <see cref="IEnumerable{User}"/> to
/// <see cref="IQueryable{User}"/> via <c>AsQueryable()</c> so the LINQ
/// chain resolves to <c>System.Linq.Queryable</c> methods. Those take
/// <c>Expression&lt;Func&lt;…&gt;&gt;</c> parameters, which the
/// transpiler treats as a queryable opt-in: each lambda body is
/// captured as a runtime <c>QueryableMeta</c> tree alongside the
/// closure, so a custom provider can read the predicate shape and
/// translate it (e.g. to a SQL WHERE clause) instead of running the
/// closure verbatim.
/// </summary>
[Transpile]
public static class UserQueries
{
    public static IEnumerable<User> Adults(IEnumerable<User> users) =>
        users.AsQueryable().Where(u => u.Age >= 18);

    public static IEnumerable<User> ActiveAdults(IEnumerable<User> users) =>
        users.AsQueryable().Where(u => u.Age >= 18 && u.Active);

    public static IEnumerable<User> AdultsAtLeast(IEnumerable<User> users, int minAge) =>
        users.AsQueryable().Where(u => u.Age >= minAge);

    public static IEnumerable<string> AdultNames(IEnumerable<User> users) =>
        users.AsQueryable().Where(u => u.Age >= 18).Select(u => u.Name);
}
