using System.Security.Cryptography;
using System.Text;
using Metano.Compiler.Analysis;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Caching;

/// <summary>
/// Computes the cache-invalidation key for a single file group
/// (PR 3b / ADR-0023): the SHA-256 of the per-type signature
/// hashes for every type in the group <em>plus every type the
/// group transitively depends on</em>, sorted by FQN for stable
/// order.
///
/// <para>
/// Combined with the global cache key from PR 3a
/// (target language + configuration fingerprint + reference
/// fingerprints), the group closure hash is the exact unit of
/// invalidation the per-group skip path needs: change a type
/// inside the closure and the hash flips; change a type outside
/// the closure and the hash stays.
/// </para>
/// </summary>
public static class GroupClosureHasher
{
    /// <summary>
    /// Builds <c>{ FQN → signature hash }</c> for every transpilable
    /// type in the compilation. Intended to be computed once per run
    /// and re-used across <see cref="HashGroupClosure"/> calls (the
    /// number of groups is small, but their closures overlap heavily;
    /// caching the per-type hash avoids hashing a popular base class
    /// dozens of times).
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildSignatureHashIndex(
        IEnumerable<INamedTypeSymbol> transpilableTypes
    )
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var symbol in transpilableTypes)
        {
            var fqn = IrTypeDependencyGraph.QualifiedName(symbol);
            // Multiple groups can ask about the same type; hash once.
            if (!index.ContainsKey(fqn))
                index[fqn] = IrTypeSignatureHasher.Hash(symbol);
        }
        return index;
    }

    /// <summary>
    /// Hashes the closure of <paramref name="groupTypeFqns"/> using
    /// the dependency graph and the pre-computed per-type signature
    /// index. The closure is "every group type plus every transitive
    /// dependent" — that is the set of types whose change would
    /// require regenerating this group's output files.
    /// </summary>
    /// <param name="groupTypeFqns">FQNs of types declared in the
    /// group (the ones <c>GroupTypesByFile</c> placed together).</param>
    /// <param name="graph">Dependency graph from PR 1.</param>
    /// <param name="signatureHashes">Pre-computed
    /// <c>FQN → SHA-256(sig)</c> map from
    /// <see cref="BuildSignatureHashIndex"/>.</param>
    public static string HashGroupClosure(
        IEnumerable<string> groupTypeFqns,
        IrTypeDependencyGraph graph,
        IReadOnlyDictionary<string, string> signatureHashes
    )
    {
        var closure = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fqn in groupTypeFqns)
        {
            if (closure.Add(fqn))
            {
                foreach (var dep in graph.TransitiveDependenciesOf(fqn))
                    closure.Add(dep);
            }
        }

        var sb = new StringBuilder();
        foreach (var fqn in closure.OrderBy(s => s, StringComparer.Ordinal))
        {
            sb.Append(fqn).Append('=');
            // Types outside the transpilable set (BCL, foreign assemblies)
            // do not have entries here; the global reference fingerprint
            // from PR 3a already covers their churn.
            if (signatureHashes.TryGetValue(fqn, out var hash))
                sb.Append(hash);
            sb.Append('\0');
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
