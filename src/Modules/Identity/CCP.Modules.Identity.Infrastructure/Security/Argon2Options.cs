using System.ComponentModel.DataAnnotations;

namespace CCP.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Argon2id cost parameters.
/// <para>
/// The defaults follow OWASP's current recommendation for Argon2id: 64 MiB of
/// memory, 3 iterations, 2 lanes. That combination targets roughly 100 ms per
/// hash on typical server hardware — slow enough to make offline cracking
/// expensive, fast enough that sign-in feels immediate.
/// </para>
/// <para>
/// <b>These must be measured against the production instance, not assumed.</b>
/// Too low and the hashing is cheap to attack; too high and an authentication
/// burst becomes a self-inflicted denial of service, because each attempt
/// reserves <see cref="MemoryKib"/> of memory. Phase 5 measures and records the
/// figures for the deployed hardware.
/// </para>
/// </summary>
public sealed class Argon2Options
{
    public const string SectionName = "Identity:Argon2";

    /// <summary>
    /// Memory cost in kibibytes. The parameter that makes GPU and ASIC attacks
    /// uneconomic, and therefore the one that matters most. 65536 KiB = 64 MiB.
    /// </summary>
    [Range(8 * 1024, 1024 * 1024)]
    public int MemoryKib { get; set; } = 65536;

    /// <summary>Number of passes over memory.</summary>
    [Range(1, 20)]
    public int Iterations { get; set; } = 3;

    /// <summary>
    /// Lanes used. Raising this raises throughput per hash but also the CPU
    /// each concurrent sign-in consumes.
    /// </summary>
    [Range(1, 16)]
    public int Parallelism { get; set; } = 2;
}
