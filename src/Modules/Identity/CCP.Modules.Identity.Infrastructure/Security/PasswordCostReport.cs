using System.Diagnostics;
using System.Globalization;
using CCP.Modules.Identity.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Measures what the password parameters actually cost, on the hardware they are
/// actually running on.
/// <para>
/// <b>"Argon2id parameters are defaults, not measured" was a register entry for
/// twenty phases</b> (#22), and the reason it stayed open is that the answer
/// depends on a machine nobody had chosen yet. It still does. What was missing
/// was not the measurement but somewhere for it to happen: the parameters are a
/// guess until a running Platform says how long they take, and no deployment
/// could tell you without somebody logging in with a stopwatch.
/// </para>
/// <para>
/// <b>It reports and does not tune.</b> Password hashing cost is a security
/// parameter, and a Platform that quietly raised its own would change how long
/// every sign-in takes, on a schedule nobody chose, with no record of what it
/// used to be. Worse, a machine that happened to be busy during startup would
/// pick a number that is wrong for every hour after it. The decision stays with
/// whoever owns the deployment; this gives them the figure the decision needs.
/// </para>
/// </summary>
public sealed class PasswordCostReport(
    IPasswordHasher hasher,
    IOptions<Argon2Options> options,
    ILogger<PasswordCostReport> logger)
{
    /// <summary>
    /// The window a password hash should land in.
    /// <para>
    /// <b>Below the floor is the one that matters.</b> Too fast means the
    /// parameters are cheaper than they look, and cheap for the Platform is
    /// cheap for somebody working through a stolen password table — which is the
    /// entire thing these parameters exist to make expensive.
    /// </para>
    /// <para>
    /// Too slow is a different and smaller problem: sign-in feels bad and a burst
    /// of them costs real CPU. Worth saying, not worth alarming about.
    /// </para>
    /// </summary>
    public static readonly TimeSpan Floor = TimeSpan.FromMilliseconds(100);

    public static readonly TimeSpan Ceiling = TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// Hashes a throwaway password and says how long it took.
    /// <para>
    /// Twice, and the second one counts. The first pays for the JIT and for the
    /// first allocation of sixty-four mebibytes, neither of which a real sign-in
    /// pays — reporting it would overstate the cost and send somebody to lower
    /// parameters that were fine.
    /// </para>
    /// </summary>
    public TimeSpan Measure()
    {
        const string sample = "a-password-nobody-has-and-nothing-stores";

        hasher.Hash(sample);

        long started = Stopwatch.GetTimestamp();

        hasher.Hash(sample);

        return Stopwatch.GetElapsedTime(started);
    }

    /// <summary>
    /// Measures and writes the result where an operator will see it.
    /// <para>
    /// At startup rather than on demand, because a figure nobody asks for is a
    /// figure nobody has. It is one hash: the cost of knowing is the cost of a
    /// single sign-in, once per process.
    /// </para>
    /// </summary>
    public void Report()
    {
        Argon2Options settings = options.Value;

        TimeSpan cost;

        try
        {
            cost = Measure();
        }
#pragma warning disable CA1031 // Nothing here is worth failing a startup over.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // A Platform that would not start because it could not time itself
            // would be a Platform brought down by its own diagnostics.
            logger.LogWarning(
                exception, "The password hashing cost could not be measured.");

            return;
        }

        string parameters = string.Create(
            CultureInfo.InvariantCulture,
            $"m={settings.MemoryKib}KiB t={settings.Iterations} p={settings.Parallelism}");

        if (cost < Floor)
        {
            logger.LogWarning(
                "Password hashing takes {Cost}ms on this hardware with {Parameters}, which is "
                + "below the {Floor}ms floor. Cheap here is cheap for somebody working through a "
                + "stolen password table: raise Identity:Argon2:MemoryKib until it is not.",
                cost.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
                parameters,
                Floor.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture));

            return;
        }

        if (cost > Ceiling)
        {
            logger.LogWarning(
                "Password hashing takes {Cost}ms on this hardware with {Parameters}. That is "
                + "safe and slow: sign-in will feel it, and a burst of them costs real CPU.",
                cost.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
                parameters);

            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Password hashing takes {Cost}ms on this hardware with {Parameters}.",
                cost.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
                parameters);
        }
    }
}
