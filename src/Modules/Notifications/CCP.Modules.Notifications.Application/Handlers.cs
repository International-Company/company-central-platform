using CCP.Kernel.Paging;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Notifications.Application.Abstractions;
using CCP.Modules.Notifications.Contracts.Dtos;
using CCP.Modules.Notifications.Domain;
using CCP.Modules.Notifications.Domain.Notifications;
using CCP.Modules.Notifications.Domain.Preferences;
using CCP.Modules.Notifications.Domain.Templates;

namespace CCP.Modules.Notifications.Application;

// ---------------------------------------------------------------------------
// Queries
// ---------------------------------------------------------------------------

/// <summary>One person's own inbox.</summary>
public sealed record GetInboxQuery(Guid UserId, bool UnreadOnly, int Page, int PageSize);

/// <summary>What has been sent, for administration.</summary>
public sealed record SearchNotificationsQuery(
    NotificationStatus? Status, string? Category, NotificationChannel? Channel,
    int Page, int PageSize);

/// <summary>The template catalogue.</summary>
public sealed record GetTemplatesQuery(bool IncludeInactive);

/// <summary>One person's preferences.</summary>
public sealed record GetPreferencesQuery(Guid UserId);

// ---------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------

/// <summary>Marking one's own notification as read.</summary>
public sealed record MarkReadCommand(Guid NotificationId, Guid ReaderUserId);

/// <summary>Writing or revising a template.</summary>
public sealed record SaveTemplateCommand(
    string Code, string Locale, string Subject, string Body, IReadOnlyList<string> Variables);

/// <summary>Turning a category on or off for oneself.</summary>
public sealed record SetPreferenceCommand(
    Guid UserId, string Category, NotificationChannel Channel, bool IsEnabled);

// ---------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------

/// <summary>
/// The recipient's own inbox.
/// <para>
/// Scoped to the caller by the handler, not by a filter the endpoint passes:
/// a query that took a user id would be one refactor away from taking somebody
/// else's.
/// </para>
/// </summary>
public sealed class GetInboxHandler(INotificationRepository repository)
{
    public async Task<Result<PagedResult<NotificationDto>>> HandleAsync(
        GetInboxQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, 100);

        (IReadOnlyList<Notification> items, long total) = await repository.GetInboxAsync(
            query.UserId, query.UnreadOnly, (page - 1) * pageSize, pageSize, cancellationToken);

        // Delivery attempts are stripped: an SMTP transcript is an operator's
        // evidence and means nothing to the person who was waiting.
        return Result.Success(new PagedResult<NotificationDto>(
            [.. items.Select(n => NotificationMapper.ToDto(n, includeDeliveries: false))],
            page, pageSize, total));
    }
}

/// <summary>Marks one of the caller's own notifications as read.</summary>
public sealed class MarkReadHandler(
    INotificationRepository repository,
    INotificationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        MarkReadCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Notification? notification = await repository.FindNotificationAsync(
            command.NotificationId, cancellationToken);

        if (notification is null)
        {
            return Result.Failure(NotificationErrors.NotificationNotFound);
        }

        Result marked = notification.MarkRead(command.ReaderUserId, clock.UtcNow);

        if (marked.IsFailure)
        {
            return marked;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>What has been sent, and how it went.</summary>
public sealed class SearchNotificationsHandler(INotificationRepository repository)
{
    public async Task<Result<PagedResult<NotificationDto>>> HandleAsync(
        SearchNotificationsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, 100);

        (IReadOnlyList<Notification> items, long total) = await repository.SearchAsync(
            query.Status, query.Category, query.Channel,
            (page - 1) * pageSize, pageSize, cancellationToken);

        // With deliveries here: this is the screen where somebody works out why
        // a message never arrived.
        return Result.Success(new PagedResult<NotificationDto>(
            [.. items.Select(n => NotificationMapper.ToDto(n, includeDeliveries: true))],
            page, pageSize, total));
    }
}

/// <summary>The template catalogue.</summary>
public sealed class GetTemplatesHandler(INotificationRepository repository)
{
    public async Task<Result<IReadOnlyList<NotificationTemplateDto>>> HandleAsync(
        GetTemplatesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<NotificationTemplate> templates =
            await repository.GetTemplatesAsync(query.IncludeInactive, cancellationToken);

        return Result.Success<IReadOnlyList<NotificationTemplateDto>>(
            [.. templates.Select(NotificationMapper.ToDto)]);
    }
}

/// <summary>
/// Writing a template, or revising one.
/// <para>
/// One call for both, keyed on code and locale, because a template is
/// identified by what it says and in what language rather than by an id
/// somebody has to hold on to. Revising bumps the version so a delivery record
/// can still say which text was actually sent.
/// </para>
/// </summary>
public sealed class SaveTemplateHandler(
    INotificationRepository repository,
    INotificationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<NotificationTemplateDto>> HandleAsync(
        SaveTemplateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        NotificationTemplate? existing = await repository.FindTemplateAsync(
            command.Code, command.Locale, cancellationToken);

        if (existing is not null)
        {
            Result revised = existing.Revise(
                command.Subject, command.Body, command.Variables, now);

            if (revised.IsFailure)
            {
                return Result.Failure<NotificationTemplateDto>(revised.Errors);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(NotificationMapper.ToDto(existing));
        }

        Result<NotificationTemplate> created = NotificationTemplate.Create(
            command.Code, command.Locale, command.Subject, command.Body,
            command.Variables, now);

        if (created.IsFailure)
        {
            return Result.Failure<NotificationTemplateDto>(created.Errors);
        }

        repository.AddTemplate(created.Value);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(NotificationMapper.ToDto(created.Value));
    }
}

/// <summary>What one person has turned off.</summary>
public sealed class GetPreferencesHandler(INotificationRepository repository)
{
    public async Task<Result<IReadOnlyList<NotificationPreferenceDto>>> HandleAsync(
        GetPreferencesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<NotificationPreference> preferences =
            await repository.GetPreferencesAsync(query.UserId, cancellationToken);

        return Result.Success<IReadOnlyList<NotificationPreferenceDto>>(
            [.. preferences.Select(p => new NotificationPreferenceDto(
                p.Category, p.Channel.ToString(), p.IsEnabled))]);
    }
}

/// <summary>
/// Turning a category on or off for oneself.
/// <para>
/// Security notifications are refused here as well as by the aggregate. Two
/// checks, because this is the one preference whose absence protects an account
/// its owner has already lost control of.
/// </para>
/// </summary>
public sealed class SetPreferenceHandler(
    INotificationRepository repository,
    INotificationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        SetPreferenceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.Equals(
                command.Category,
                NotificationPreference.SecurityCategory,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(NotificationErrors.SecurityNotificationsCannotBeDisabled);
        }

        DateTimeOffset now = clock.UtcNow;

        NotificationPreference? existing = await repository.FindPreferenceAsync(
            command.UserId, command.Category, command.Channel, cancellationToken);

        if (existing is not null)
        {
            existing.Set(command.IsEnabled, now);
        }
        else
        {
            Result<NotificationPreference> created = NotificationPreference.Create(
                command.UserId, command.Category, command.Channel, now);

            if (created.IsFailure)
            {
                return Result.Failure(created.Errors);
            }

            created.Value.Set(command.IsEnabled, now);
            repository.AddPreference(created.Value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Turns the module's aggregates into what callers see.</summary>
public static class NotificationMapper
{
    public static NotificationDto ToDto(Notification notification, bool includeDeliveries)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return new NotificationDto(
            notification.Id,
            notification.RecipientUserId,
            notification.Category,
            notification.Channel.ToString(),
            notification.Locale,
            notification.Subject,
            notification.Body,
            notification.TemplateCode,
            notification.TemplateVersion,
            notification.Status.ToString(),
            notification.ReadAt,
            notification.CreatedAt,
            includeDeliveries
                ? [.. notification.Deliveries
                    .OrderBy(d => d.Attempt)
                    .Select(d => new NotificationDeliveryDto(
                        d.Attempt, d.Succeeded, d.ProviderName,
                        d.ProviderResponse, d.DurationMs, d.OccurredAt))]
                : []);
    }

    public static NotificationTemplateDto ToDto(NotificationTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return new NotificationTemplateDto(
            template.Id, template.Code, template.Locale, template.Subject,
            template.Body, template.DeclaredVariables, template.Version, template.IsActive);
    }
}
