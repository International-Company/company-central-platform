using CCP.Modules.Audit.Domain;

namespace CCP.Modules.Audit.UnitTests.Domain;

/// <summary>Constructing a record that has to hold up as evidence.</summary>
public sealed class AuditEventTests
{
    private static readonly DateTimeOffset Occurred =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Record_KeepsTheTimeTheThingHappened()
    {
        AuditEvent e = AuditEvent.Record("platform", "identity", "user.created", Occurred);

        // Not the time it was written. Audit writes land a moment later, and
        // recording the write time would misorder events under load and put an
        // event in the wrong partition at a month boundary.
        Assert.Equal(Occurred, e.OccurredAt);
    }

    [Fact]
    public void Record_MakesTheIdSortByTime()
    {
        AuditEvent first = AuditEvent.Record("platform", "identity", "a", Occurred);
        AuditEvent later = AuditEvent.Record("platform", "identity", "b", Occurred.AddMinutes(1));

        // UUID v7 is time-ordered, so the index stays dense as rows arrive
        // rather than fragmenting the way random ids do.
        Assert.True(
            string.CompareOrdinal(first.Id.ToString(), later.Id.ToString()) < 0,
            "Ids must sort in the order the events occurred.");
    }

    [Theory]
    [InlineData("PLATFORM", "IDENTITY", "User.Created")]
    [InlineData("Platform", "Identity", "USER.CREATED")]
    public void Record_NormalisesTheClassifiers(string application, string module, string action)
    {
        AuditEvent e = AuditEvent.Record(application, module, action, Occurred);

        // Search filters on exact equality against a composite index. Two
        // spellings of "identity" would silently split the trail in half.
        Assert.Equal("platform", e.Application);
        Assert.Equal("identity", e.Module);
        Assert.Equal("user.created", e.Action);
    }

    [Fact]
    public void Record_RedactsBeforeStoring()
    {
        AuditEvent e = AuditEvent.Record(
            "platform", "identity", "password.changed", Occurred,
            newValue: """{"password":"hunter2"}""");

        Assert.NotNull(e.NewValue);

        // The whole point: the table is append-only, so a secret that reaches
        // it cannot be removed afterwards.
        Assert.DoesNotContain("hunter2", e.NewValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_RedactsMetadataToo()
    {
        AuditEvent e = AuditEvent.Record(
            "finance", "invoices", "invoice.approved", Occurred,
            metadata: """{"apiKey":"live_abc123"}""");

        Assert.NotNull(e.Metadata);
        Assert.DoesNotContain("live_abc123", e.Metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_KeepsTheActorNameBesideTheId()
    {
        var actorId = Guid.NewGuid();

        AuditEvent e = AuditEvent.Record(
            "platform", "authorization", "role.assigned", Occurred,
            actorUserId: actorId, actorUsername: "amira");

        // A trail that reads "user 8f3a… did X" after an employee record
        // changes is not usable evidence.
        Assert.Equal(actorId, e.ActorUserId);
        Assert.Equal("amira", e.ActorUsername);
    }

    [Fact]
    public void Record_DefaultsToSuccess()
        => Assert.Equal(
            AuditResult.Success,
            AuditEvent.Record("platform", "identity", "user.viewed", Occurred).Result);

    [Fact]
    public void Record_DistinguishesDeniedFromFailed()
    {
        AuditEvent denied = AuditEvent.Record(
            "platform", "authorization", "role.assigned", Occurred, AuditResult.Denied);

        // A run of denials is someone finding the edges of their access, or
        // mapping what they can reach. A run of failures is usually a broken
        // integration. They are investigated differently.
        Assert.Equal(AuditResult.Denied, denied.Result);
        Assert.NotEqual(AuditResult.Failure, denied.Result);
    }

    [Theory]
    [InlineData("", "identity", "a")]
    [InlineData("platform", "", "a")]
    [InlineData("platform", "identity", "")]
    public void Record_RefusesAnUnclassifiableEvent(string application, string module, string action)
    {
        // An event that cannot say which system, which module or what happened
        // is not searchable, and an unsearchable audit row is landfill.
        Assert.Throws<ArgumentException>(
            () => AuditEvent.Record(application, module, action, Occurred));
    }
}
