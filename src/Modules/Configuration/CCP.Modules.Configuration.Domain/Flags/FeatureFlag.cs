using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Configuration.Domain.Flags;

/// <summary>
/// A capability that can be switched off without a deployment.
/// <para>
/// <b>The reason for a flag is almost never the launch.</b> It is the evening
/// somebody has to turn a thing off while they work out what it is doing, and a
/// system where the only way to do that is a deployment is a system where the
/// answer at eight o'clock is "we cannot".
/// </para>
/// <para>
/// Targeting is by <b>role</b> or by <b>organizational unit</b>, and by nothing
/// else. Not by a percentage, not by an arbitrary attribute, not by a rule
/// language — each of those turns "who has this?" into a question that needs a
/// simulator to answer, and a flag nobody can reason about is worse than no
/// flag.
/// </para>
/// </summary>
public sealed class FeatureFlag : AggregateRoot, IAuditableEntity
{
    private FeatureFlag() { }

    private FeatureFlag(Guid id, string key, string applicationCode, DateTimeOffset now)
        : base(id)
    {
        Key = key;
        ApplicationCode = applicationCode;
        IsEnabled = false;
        CreatedAt = now;
    }

    /// <summary>The full name, in its owner's namespace.</summary>
    public string Key { get; private set; } = string.Empty;

    public string ApplicationCode { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>
    /// Whether the flag is on at all.
    /// <para>
    /// <b>Off is the default, and it is not a formality.</b> A flag created in
    /// advance of the thing it guards must not switch that thing on the moment
    /// the row appears.
    /// </para>
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// Roles the flag is limited to, one per line. Empty means everybody, when
    /// the flag is on.
    /// </summary>
    public string TargetedRoles { get; private set; } = string.Empty;

    /// <summary>
    /// Unit ids the flag is limited to, one per line. A unit here covers
    /// everything beneath it.
    /// </summary>
    public string TargetedUnits { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public IReadOnlyList<Guid> RoleTargets => Parse(TargetedRoles);

    public IReadOnlyList<Guid> UnitTargets => Parse(TargetedUnits);

    /// <summary>Whether the flag reaches everybody it is on for.</summary>
    public bool IsUntargeted => RoleTargets.Count == 0 && UnitTargets.Count == 0;

    public static Result<FeatureFlag> Declare(
        string key, string applicationCode, string? description, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure<FeatureFlag>(ConfigurationErrors.KeyRequired);
        }

        if (string.IsNullOrWhiteSpace(applicationCode))
        {
            return Result.Failure<FeatureFlag>(ConfigurationErrors.ApplicationCodeRequired);
        }

        string trimmed = key.Trim().ToLowerInvariant();
        string prefix = applicationCode.Trim().ToLowerInvariant() + ".";

        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Result.Failure<FeatureFlag>(
                ConfigurationErrors.KeyOutsideNamespace(applicationCode));
        }

        return Result.Success(new FeatureFlag(
            Uuid7.NewGuid(now), trimmed, applicationCode.Trim().ToLowerInvariant(), now)
        {
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
        });
    }

    public Result SetEnabled(bool isEnabled, DateTimeOffset now)
    {
        IsEnabled = isEnabled;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Limits the flag to some roles and units, or to nobody in particular.
    /// <para>
    /// Both lists empty means everybody — which is what an untargeted flag is,
    /// and is why "on and targeted at nothing" cannot silently mean "on for
    /// nobody". A flag that was on and reached nobody would look broken and be
    /// working.
    /// </para>
    /// </summary>
    public Result Target(
        IReadOnlyList<Guid> roleIds, IReadOnlyList<Guid> unitIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(roleIds);
        ArgumentNullException.ThrowIfNull(unitIds);

        TargetedRoles = string.Join('\n', roleIds.Distinct().Select(id => id.ToString()));
        TargetedUnits = string.Join('\n', unitIds.Distinct().Select(id => id.ToString()));
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Whether the flag is on for a particular person.
    /// <para>
    /// A pure function of the flag and the caller: no lookups, no clock, nothing
    /// that could answer differently twice in a row. That matters more for flags
    /// than for most things, because the first question after a strange report
    /// is "was it on for them?" and the answer has to be reproducible.
    /// </para>
    /// </summary>
    /// <param name="callerRoleIds">Every role the person holds.</param>
    /// <param name="callerUnitChain">
    /// The person's unit and every unit above it. A flag targeted at a division
    /// reaches somebody three levels below precisely when the division appears
    /// here.
    /// </param>
    public bool IsOnFor(IReadOnlyList<Guid> callerRoleIds, IReadOnlyList<Guid> callerUnitChain)
    {
        ArgumentNullException.ThrowIfNull(callerRoleIds);
        ArgumentNullException.ThrowIfNull(callerUnitChain);

        if (!IsEnabled)
        {
            // Off is off. Targeting cannot switch a flag on for anybody, which
            // is what makes the master switch trustworthy at eight in the
            // evening.
            return false;
        }

        if (IsUntargeted)
        {
            return true;
        }

        foreach (Guid role in RoleTargets)
        {
            if (callerRoleIds.Contains(role))
            {
                return true;
            }
        }

        foreach (Guid unit in UnitTargets)
        {
            if (callerUnitChain.Contains(unit))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A line that will not parse is skipped rather than thrown over: a corrupt
    /// target should cost somebody a feature, not every evaluation in the
    /// Platform.
    /// </summary>
    private static List<Guid> Parse(string stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return [];
        }

        var ids = new List<Guid>();

        foreach (string line in stored.Split(
            '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(line, out Guid id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
