using System.Security.Claims;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Primitives;
using CCP.Modules.Security.Api;
using CCP.Modules.Security.Application.Abstractions;
using CCP.Modules.Security.Domain.Events;
using CCP.Modules.Security.Domain.Mfa;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;

namespace CCP.Modules.Security.UnitTests;

/// <summary>
/// Being refused for having no second factor is not the same as being refused
/// for having let one lapse.
/// <para>
/// <b>The Platform used to say both the same way.</b> Somebody who had never
/// enrolled, reaching an administrative action, was told to "verify your second
/// factor and retry" — and the portal opened a dialog asking for a code they
/// could not produce. The way out was a screen nothing had sent them to, so the
/// honest description of the old behaviour is that they would try, fail, and try
/// again.
/// </para>
/// <para>
/// <c>SecurityErrors.MfaRequiredByPolicy</c> had been written for exactly this,
/// with a comment describing the distinction as though it were implemented, and
/// <b>nothing ever raised it</b>. It was found by listing every Error value in
/// the Platform that nothing references — the same class of defect as
/// <c>ApplySnakeCaseNames</c>, which was also defined and never called.
/// </para>
/// </summary>
public sealed class StepUpRefusalTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid SessionId = Guid.CreateVersion7();

    [Fact]
    public async Task AValidElevationIsAllowedThrough()
    {
        AuthorizationHandlerContext context = await EvaluateAsync(
            new FakeRepository { HasValidStepUp = true, Enrolment = Enrolled() });

        Assert.True(context.HasSucceeded);
    }

    /// <summary>
    /// A lapsed elevation asks for a code, because there is one to give.
    /// </summary>
    [Fact]
    public async Task ALapsedElevationAsksForTheCode()
    {
        AuthorizationHandlerContext context = await EvaluateAsync(
            new FakeRepository { HasValidStepUp = false, Enrolment = Enrolled() });

        Assert.True(context.HasFailed);
        Assert.Equal(StepUpRequirement.FailureReason, OnlyReason(context));
    }

    /// <summary>
    /// No enrolment asks for an enrolment, because there is no code to give.
    /// </summary>
    [Fact]
    public async Task NoEnrolmentAsksForAnEnrolment()
    {
        AuthorizationHandlerContext context = await EvaluateAsync(
            new FakeRepository { HasValidStepUp = false, Enrolment = null });

        Assert.True(context.HasFailed);
        Assert.Equal(StepUpRequirement.EnrolmentRequiredReason, OnlyReason(context));
    }

    /// <summary>
    /// The enrolment is only looked for when the elevation has already failed.
    /// <para>
    /// Every request to a step-up endpoint passes through here, and almost all
    /// of them succeed. A query on that path would be paid for by everybody to
    /// tell a few people something they do not need to hear.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheEnrolmentIsNotLookedUpWhenTheElevationHolds()
    {
        var repository = new FakeRepository { HasValidStepUp = true, Enrolment = Enrolled() };

        await EvaluateAsync(repository);

        Assert.Equal(0, repository.EnrolmentLookups);
    }

    /// <summary>
    /// An unauthenticated caller is neither allowed nor refused here.
    /// <para>
    /// Left unhandled so the framework answers 401 rather than 403 — the same
    /// distinction the permission handler makes. Refusing would tell somebody
    /// who is not signed in that the thing they asked for exists.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnAnonymousCallerIsLeftToTheFramework()
    {
        var requirement = new StepUpRequirement();

        var context = new AuthorizationHandlerContext(
            [requirement], new ClaimsPrincipal(new ClaimsIdentity()), resource: null);

        await Handler(new FakeRepository()).HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    // --- Fixtures -----------------------------------------------------------

    private static string? OnlyReason(AuthorizationHandlerContext context)
        => Assert.Single(context.FailureReasons).Message;

    private static MfaEnrolment Enrolled()
    {
        MfaEnrolment enrolment = MfaEnrolment.Begin(UserId, "a-protected-secret", Now);

        // Active, not merely begun. A pending enrolment is somebody who started
        // and did not finish, and FindActiveEnrolmentAsync is the query the
        // handler asks -- so building a pending one here would be testing
        // against a state the handler never sees.
        Assert.True(enrolment.Activate(Now).IsSuccess);

        return enrolment;
    }

    private static DateTimeOffset Now => new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static StepUpAuthorizationHandler Handler(FakeRepository repository)
        => new(repository, new FixedClock(), NullLogger<StepUpAuthorizationHandler>.Instance);

    private static async Task<AuthorizationHandlerContext> EvaluateAsync(FakeRepository repository)
    {
        var requirement = new StepUpRequirement();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", UserId.ToString()),
                new Claim("sid", SessionId.ToString()),
            ],
            authenticationType: "Test"));

        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);

        await Handler(repository).HandleAsync(context);

        return context;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = Now;
    }

    private sealed class FakeRepository : ISecurityRepository
    {
        public bool HasValidStepUp { get; init; }

        public MfaEnrolment? Enrolment { get; init; }

        public int EnrolmentLookups { get; private set; }

        public Task<bool> HasValidStepUpAsync(
            Guid userId, Guid sessionId, DateTimeOffset asOf,
            CancellationToken cancellationToken = default)
            => Task.FromResult(HasValidStepUp);

        public Task<MfaEnrolment?> FindActiveEnrolmentAsync(
            Guid userId, CancellationToken cancellationToken = default)
        {
            EnrolmentLookups++;

            return Task.FromResult(Enrolment);
        }

        // The rest of the surface. This handler touches two members, and a fake
        // that answered the others would be inventing behaviour nothing asked
        // for -- so they throw, and a future change that starts calling one
        // fails here rather than passing on a fabricated answer.
        public Task<MfaEnrolment?> FindEnrolmentAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddEnrolment(MfaEnrolment enrolment) => throw new NotSupportedException();

        public void RemoveEnrolment(MfaEnrolment enrolment) => throw new NotSupportedException();

        public Task AddSecurityEventAsync(
            SecurityEvent securityEvent, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddStepUpConfirmation(StepUpConfirmation confirmation)
            => throw new NotSupportedException();

        public Task<int> RevokeStepUpConfirmationsAsync(
            Guid userId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(IReadOnlyList<SecurityEvent> Items, long TotalCount)> SearchSecurityEventsAsync(
            Guid? userId, string? eventType, SecuritySeverity? minimumSeverity,
            DateTimeOffset from, DateTimeOffset to, int skip, int take,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
