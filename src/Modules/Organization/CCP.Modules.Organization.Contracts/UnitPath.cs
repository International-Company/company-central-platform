namespace CCP.Modules.Organization.Contracts;

/// <summary>
/// The shape of a materialized unit path, for the modules that read one.
/// <para>
/// <b>The format belongs to Organization, so the parsing does too.</b> Documents
/// and Configuration both need a caller's ancestry as ids, and both were about to
/// split the string themselves — which means the separator would be written down
/// in three places and Organization could change it while two of them carried on
/// compiling. Silent, and only visible as unit-scoped rules quietly reaching
/// nobody.
/// </para>
/// </summary>
public static class UnitPath
{
    /// <summary>
    /// Turns <c>/{id}/{id}/…</c> into ids, nearest last.
    /// <para>
    /// The whole ancestry is already in the string, which is what the
    /// materialized path is for: asking Organization to walk the tree would be a
    /// query per level, on every request, for something the format makes free.
    /// </para>
    /// <para>
    /// A segment that will not parse is skipped rather than thrown over. A
    /// corrupted path should cost somebody a unit-scoped rule, not the ability
    /// to use the Platform at all.
    /// </para>
    /// </summary>
    /// <param name="path">
    /// The path, or null when the caller has no employee record — which is
    /// normal rather than exceptional: service accounts and contractors sign in
    /// and have no place in the organization. They get an empty chain, so unit
    /// rules do not reach them, which is the safe reading and the correct one.
    /// </param>
    public static IReadOnlyList<Guid> ParseChain(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return [];
        }

        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<Guid> chain = new(segments.Length);

        foreach (string segment in segments)
        {
            if (Guid.TryParse(segment, out Guid id))
            {
                chain.Add(id);
            }
        }

        return chain;
    }
}
