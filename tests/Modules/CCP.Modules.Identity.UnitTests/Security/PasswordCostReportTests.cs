using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.UnitTests.Security;

/// <summary>
/// What the password parameters cost, measured rather than assumed.
/// <para>
/// <b>"Argon2id parameters are defaults, not measured" was open for twenty
/// phases</b> (#22), and the reason was never laziness: the answer depends on
/// hardware nobody had chosen. It still does. What was missing was somewhere for
/// the measurement to happen at all.
/// </para>
/// <para>
/// So what is testable here is the <i>reporting decision</i> — that a
/// configuration which hashes too cheaply is called out, and that the Platform
/// does not quietly change its own security parameters in response. The number
/// itself belongs to whatever machine is running.
/// </para>
/// </summary>
public sealed class PasswordCostReportTests
{
    /// <summary>
    /// The production parameters are slow enough to be worth having.
    /// <para>
    /// This runs on whatever hardware the suite runs on, which is the point: a
    /// developer laptop and a CI runner are both real answers, and either being
    /// under the floor is worth knowing. The threshold is deliberately generous
    /// — it is a floor, not a target.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDefaultParametersAreNotCheap()
    {
        TimeSpan cost = Build(new Argon2Options()).Measure();

        Assert.True(
            cost >= PasswordCostReport.Floor,
            $"Hashing took {cost.TotalMilliseconds:0}ms with the default parameters, below the "
            + $"{PasswordCostReport.Floor.TotalMilliseconds:0}ms floor. Cheap here is cheap for "
            + "somebody working through a stolen password table.");
    }

    /// <summary>
    /// A configuration that hashes too cheaply is said out loud.
    /// <para>
    /// The integration suite deliberately runs reduced parameters so that a test
    /// creating a user per case does not take a minute, and those are exactly
    /// the numbers that must never reach a deployment by being copied.
    /// </para>
    /// </summary>
    [Fact]
    public void CheapParametersAreWarnedAbout()
    {
        var log = new RecordingLogger();

        Build(new Argon2Options { MemoryKib = 8192, Iterations = 1, Parallelism = 1 }, log)
            .Report();

        Assert.Contains(log.Warnings, message => message.Contains("floor", StringComparison.Ordinal));
    }

    /// <summary>
    /// And ordinary parameters are reported without alarming anybody.
    /// </summary>
    [Fact]
    public void OrdinaryParametersAreReportedQuietly()
    {
        var log = new RecordingLogger();

        Build(new Argon2Options(), log).Report();

        Assert.Empty(log.Warnings);
    }

    /// <summary>
    /// <b>It reports and does not tune.</b>
    /// <para>
    /// Password hashing cost is a security parameter. A Platform that raised its
    /// own would change how long every sign-in takes on a schedule nobody chose,
    /// with no record of what it used to be — and a machine that happened to be
    /// busy during startup would pick a number wrong for every hour after it.
    /// </para>
    /// </summary>
    [Fact]
    public void MeasuringChangesNothing()
    {
        var settings = new Argon2Options { MemoryKib = 8192, Iterations = 1, Parallelism = 1 };

        Build(settings).Report();

        Assert.Equal(8192, settings.MemoryKib);
        Assert.Equal(1, settings.Iterations);
        Assert.Equal(1, settings.Parallelism);
    }

    // --- Fixtures -----------------------------------------------------------

    private static PasswordCostReport Build(
        Argon2Options settings, RecordingLogger? log = null)
        => new(
            new Argon2PasswordHasher(Options.Create(settings)),
            Options.Create(settings),
            log ?? new RecordingLogger());

    private sealed class RecordingLogger : ILogger<PasswordCostReport>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (logLevel >= LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
