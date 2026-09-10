using Npgsql;

namespace CCP.Kernel.Infrastructure.Persistence;

/// <summary>
/// The limits that keep one bad query from taking the database with it.
/// <para>
/// <b>Applied to the connection string rather than to each DbContext.</b> There
/// are twenty-two places in the Platform that call <c>UseNpgsql</c> — eleven
/// module registrations, eleven design-time and repository factories — and a
/// rule that has to be repeated twenty-two times is a rule that is missing from
/// at least one of them. Putting it in the string the composition root resolves
/// means every context, every factory and every raw <see cref="NpgsqlConnection"/>
/// inherits it without knowing about it.
/// </para>
/// <para>
/// <b>Nothing already specified is overwritten.</b> A deployment that says what
/// it means keeps what it said; these are floors, not decisions taken away from
/// the operator.
/// </para>
/// </summary>
public static class ConnectionHardening
{
    /// <summary>
    /// How long a single statement may run before PostgreSQL cancels it.
    /// <para>
    /// Sixty seconds. No request the Platform serves is a legitimate minute of
    /// database work; a statement that reaches this is a missing index, a
    /// runaway join or a lock nobody expected. Without it, one such query holds
    /// a connection until the process is restarted, and enough of them exhaust
    /// the pool and stop the whole Platform on behalf of one endpoint.
    /// </para>
    /// </summary>
    public const int StatementTimeoutSeconds = 60;

    /// <summary>
    /// How long a session may sit inside an open transaction doing nothing.
    /// <para>
    /// This is the one that actually causes outages. An abandoned open
    /// transaction holds its locks indefinitely <i>and</i> stops
    /// <c>VACUUM</c> reclaiming any row version newer than it — so the tables
    /// bloat, the planner's estimates rot, and in the worst case the transaction
    /// id wraparound protection starts refusing writes across the entire
    /// database. The cause is usually a client that crashed mid-transaction, and
    /// nothing on the client side can clean it up, by definition.
    /// </para>
    /// </summary>
    public const int IdleInTransactionTimeoutSeconds = 60;

    /// <summary>
    /// How long to wait for a connection before giving up.
    /// <para>
    /// Ten seconds rather than Npgsql's fifteen. A database that has not
    /// answered in ten seconds is not about to; waiting longer turns one slow
    /// dependency into a queue of held request threads.
    /// </para>
    /// </summary>
    public const int ConnectTimeoutSeconds = 10;

    /// <summary>
    /// Returns the connection string with the Platform's limits applied.
    /// </summary>
    /// <param name="connectionString">The resolved connection string.</param>
    /// <param name="serverSideTimeouts">
    /// Whether to ask PostgreSQL to enforce the statement and idle-transaction
    /// timeouts. <b>False for the migrator</b>: creating an index on a large
    /// table is legitimately a long statement, and a schema change killed
    /// half-way through by a timeout meant for web requests is a far worse
    /// outcome than the one the timeout prevents.
    /// </param>
    public static string Apply(string connectionString, bool serverSideTimeouts = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        if (!WasSet(builder, "Timeout"))
        {
            builder.Timeout = ConnectTimeoutSeconds;
        }

        if (!WasSet(builder, "Command Timeout"))
        {
            // Slightly longer than the server's statement timeout, so the server
            // cancels the query and reports why. The other way round, the client
            // gives up first and the query keeps running on a connection nobody
            // is listening to any more.
            builder.CommandTimeout = StatementTimeoutSeconds + 5;
        }

        if (serverSideTimeouts && !WasSet(builder, "Options"))
        {
            // Server-side, and that is the point: a client-side command timeout
            // stops the application waiting, and does nothing whatsoever about
            // the statement, which carries on burning the database's CPU. Only
            // PostgreSQL can actually stop it.
            builder.Options =
                $"-c statement_timeout={StatementTimeoutSeconds * 1000} "
                + $"-c idle_in_transaction_session_timeout={IdleInTransactionTimeoutSeconds * 1000}";
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether the supplied string actually named this keyword.
    /// <para>
    /// <b>Not <c>ContainsKey</c>.</b> A strongly-typed
    /// <see cref="System.Data.Common.DbConnectionStringBuilder"/> answers true
    /// from <c>ContainsKey</c> for every keyword it knows about, set or not — so
    /// a guard written that way short-circuits every time and applies none of
    /// these limits, while reading exactly like a guard that works. That is what
    /// happened here, and it was caught by a test asserting the values rather
    /// than by reading the code.
    /// </para>
    /// <para>
    /// <c>ShouldSerialize</c> is the one that reports what the caller set.
    /// </para>
    /// </summary>
    private static bool WasSet(NpgsqlConnectionStringBuilder builder, string keyword) =>
        builder.ShouldSerialize(keyword);
}
