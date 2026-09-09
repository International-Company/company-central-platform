namespace CCP.Modules.Notifications.Contracts.Dtos;

/// <summary>One message, as its recipient or an administrator sees it.</summary>
public sealed record NotificationDto(
    Guid Id,
    Guid RecipientUserId,
    string Category,
    string Channel,
    string Locale,
    string Subject,
    string Body,
    string? TemplateCode,
    int? TemplateVersion,
    string Status,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<NotificationDeliveryDto> Deliveries);

/// <summary>
/// One attempt to deliver.
/// <para>
/// Shown in administration, never in the recipient's inbox: an SMTP transcript
/// is an operator's evidence and means nothing to the person who was waiting for
/// the message.
/// </para>
/// </summary>
public sealed record NotificationDeliveryDto(
    int Attempt,
    bool Succeeded,
    string? ProviderName,
    string? ProviderResponse,
    int DurationMs,
    DateTimeOffset OccurredAt);

/// <summary>A template, in one language.</summary>
public sealed record NotificationTemplateDto(
    Guid Id,
    string Code,
    string Locale,
    string Subject,
    string Body,
    IReadOnlyList<string> Variables,
    int Version,
    bool IsActive);

/// <summary>
/// One person's choice about one kind of message on one channel.
/// <para>
/// Only the choices somebody has made are stored, so a list of these is a list
/// of what has been turned off rather than a complete matrix.
/// </para>
/// </summary>
public sealed record NotificationPreferenceDto(
    string Category,
    string Channel,
    bool IsEnabled);
