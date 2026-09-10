using CCP.Kernel.Infrastructure.Persistence;
using Npgsql;

namespace CCP.Kernel.UnitTests.Persistence;

/// <summary>
/// The limits that keep one bad query from taking the database with it.
/// <para>
/// <b>This is the kind of code that looks right and does nothing.</b> A
/// misspelled keyword, a value in the wrong unit, or an <c>Options</c> string
/// PostgreSQL silently ignores all produce a connection string that connects
/// perfectly and enforces none of what it claims — and there is no error to
/// notice, because the failure only appears the day a query runs away.
/// </para>
/// </summary>
public sealed class ConnectionHardeningTests
{
    private const string Plain =
        "Host=localhost;Port=5432;Database=ccp;Username=ccp;Password=secret";

    [Fact]
    public void TheStatementTimeoutIsSentToTheServer()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionHardening.Apply(Plain));

        // Milliseconds. PostgreSQL reads a bare number as milliseconds, so "60"
        // would be a sixty-millisecond timeout that fails every query — the kind
        // of mistake that is invisible in a code review and catastrophic in
        // production.
        Assert.Contains("statement_timeout=60000", builder.Options, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one that actually causes outages: an abandoned open transaction holds
    /// its locks and stops <c>VACUUM</c> reclaiming anything newer than itself.
    /// </summary>
    [Fact]
    public void TheIdleInTransactionTimeoutIsSentToTheServer()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionHardening.Apply(Plain));

        Assert.Contains(
            "idle_in_transaction_session_timeout=60000",
            builder.Options,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The client must give up <i>after</i> the server, not before. The other
    /// way round, the application stops waiting and the statement carries on
    /// burning the database's CPU on a connection nobody is listening to.
    /// </summary>
    [Fact]
    public void TheClientWaitsSlightlyLongerThanTheServer()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionHardening.Apply(Plain));

        Assert.True(builder.CommandTimeout > ConnectionHardening.StatementTimeoutSeconds);
    }

    [Fact]
    public void TheConnectTimeoutFailsFast()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionHardening.Apply(Plain));

        Assert.Equal(ConnectionHardening.ConnectTimeoutSeconds, builder.Timeout);
    }

    /// <summary>
    /// A deployment that says what it means keeps what it said. These are floors,
    /// not decisions taken away from whoever runs the Platform.
    /// </summary>
    [Fact]
    public void WhatTheDeploymentAlreadySetIsLeftAlone()
    {
        string explicitly = Plain + ";Timeout=45;Command Timeout=120;Options=-c work_mem=64MB";

        var builder = new NpgsqlConnectionStringBuilder(ConnectionHardening.Apply(explicitly));

        Assert.Equal(45, builder.Timeout);
        Assert.Equal(120, builder.CommandTimeout);
        Assert.Equal("-c work_mem=64MB", builder.Options);
    }

    /// <summary>
    /// The migrator's exemption, and it is not a nicety.
    /// <para>
    /// Creating an index on a large table is minutes of honest work. A schema
    /// change cancelled half-way through by a timeout meant for web requests is
    /// a far worse outcome than the one the timeout prevents — and the advisory
    /// lock the migrator waits on is itself a blocking statement, so a second
    /// instance queuing behind a long migration would be cut off and would then
    /// serve requests against a half-migrated schema.
    /// </para>
    /// </summary>
    [Fact]
    public void TheMigratorGetsNoServerSideTimeouts()
    {
        var builder = new NpgsqlConnectionStringBuilder(
            ConnectionHardening.Apply(Plain, serverSideTimeouts: false));

        Assert.True(string.IsNullOrEmpty(builder.Options));

        // It still fails fast on connecting. Waiting is only excused once the
        // migration is actually under way.
        Assert.Equal(ConnectionHardening.ConnectTimeoutSeconds, builder.Timeout);
    }

    /// <summary>
    /// Whatever else it does, it must still connect to the same database with
    /// the same credentials. A hardening step that quietly dropped the password
    /// would be a very confusing outage.
    /// </summary>
    [Fact]
    public void TheConnectionItselfIsUnchanged()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionHardening.Apply(Plain));

        Assert.Equal("localhost", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("ccp", builder.Database);
        Assert.Equal("ccp", builder.Username);
        Assert.Equal("secret", builder.Password);
    }

    /// <summary>
    /// Applied twice — which happens, because the host hardens once for the
    /// application and once for the migrator — it must not double anything up.
    /// </summary>
    [Fact]
    public void ApplyingItTwiceChangesNothingTheSecondTime()
    {
        string once = ConnectionHardening.Apply(Plain);
        string twice = ConnectionHardening.Apply(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void AnEmptyConnectionStringIsRefused() =>
        Assert.Throws<ArgumentException>(() => ConnectionHardening.Apply("  "));
}
