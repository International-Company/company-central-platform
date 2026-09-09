using CCP.Kernel.Primitives;
using CCP.Modules.Notifications.Application.Abstractions;
using CCP.Modules.Notifications.Domain.Templates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Notifications.Infrastructure;

/// <summary>
/// Writes the templates the Platform sends for its own events.
/// <para>
/// <b>Only the Platform's own.</b> A business application registers its
/// templates through the API, the same way it registers a workflow definition;
/// these exist because the Platform itself has things to say — an approval is
/// waiting, a task is late, a password was changed — and a Platform whose first
/// notification fails because nobody wrote a template is a Platform that appears
/// broken on its first day.
/// </para>
/// <para>
/// <b>Every one exists in Arabic and English</b>, which is the acceptance
/// criterion made structural: a template added here without its pair fails the
/// unit test that walks this list.
/// </para>
/// <para>
/// Idempotent. It runs on every startup and writes only what is absent, so an
/// administrator who has revised the wording keeps their revision — a seeder
/// that overwrote on boot would silently undo somebody's work every deployment.
/// </para>
/// </summary>
public sealed class TemplateSeeder(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<TemplateSeeder> logger)
{
    /// <summary>
    /// The Platform's own messages, in both languages.
    /// <para>
    /// Deliberately plain. A notification that arrives styled like a marketing
    /// email is one people learn to ignore, and the whole value of these is that
    /// somebody reads them.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SeededTemplate> Templates =>
    [
        new("workflow.task.assigned", "ar",
            "لديك طلب ينتظر قرارك",
            "<p>وصلك طلب على الخطوة <strong>{{step}}</strong>.</p>"
            + "<p>الطلب: {{resourceType}} — {{resourceId}}</p>"
            + "<p>الموعد: {{dueAt}}</p>",
            ["step", "resourceType", "resourceId", "dueAt"]),

        new("workflow.task.assigned", "en",
            "A request is waiting for your decision",
            "<p>A request has reached the step <strong>{{step}}</strong>.</p>"
            + "<p>Request: {{resourceType}} — {{resourceId}}</p>"
            + "<p>Due: {{dueAt}}</p>",
            ["step", "resourceType", "resourceId", "dueAt"]),

        new("workflow.task.escalated", "ar",
            "طلب ينتظر قرارك تجاوز موعده",
            "<p>الطلب على الخطوة <strong>{{step}}</strong> تجاوز موعده في {{dueAt}}"
            + " ولا يزال ينتظر.</p>",
            ["step", "dueAt"]),

        new("workflow.task.escalated", "en",
            "A request waiting for you is overdue",
            "<p>The request at step <strong>{{step}}</strong> passed its deadline on "
            + "{{dueAt}} and is still waiting.</p>",
            ["step", "dueAt"]),

        new("workflow.instance.approved", "ar",
            "تمت الموافقة على طلبك",
            "<p>طلبك ({{resourceType}} — {{resourceId}}) تمت الموافقة عليه.</p>",
            ["resourceType", "resourceId", "outcome"]),

        new("workflow.instance.approved", "en",
            "Your request was approved",
            "<p>Your request ({{resourceType}} — {{resourceId}}) was approved.</p>",
            ["resourceType", "resourceId", "outcome"]),

        new("workflow.instance.rejected", "ar",
            "لم تتم الموافقة على طلبك",
            "<p>طلبك ({{resourceType}} — {{resourceId}}) لم تتم الموافقة عليه.</p>",
            ["resourceType", "resourceId", "outcome"]),

        new("workflow.instance.rejected", "en",
            "Your request was not approved",
            "<p>Your request ({{resourceType}} — {{resourceId}}) was not approved.</p>",
            ["resourceType", "resourceId", "outcome"]),

        new("workflow.instance.cancelled", "ar",
            "أُلغي طلبك",
            "<p>طلبك ({{resourceType}} — {{resourceId}}) أُلغي.</p>",
            ["resourceType", "resourceId", "outcome"]),

        new("workflow.instance.cancelled", "en",
            "Your request was cancelled",
            "<p>Your request ({{resourceType}} — {{resourceId}}) was cancelled.</p>",
            ["resourceType", "resourceId", "outcome"]),

        // Security. These reach somebody whose account may have been taken, so
        // they say what happened and what to do — not "a security event
        // occurred", which tells a worried person nothing.
        new("security.password.changed", "ar",
            "تغيّرت كلمة مرور حسابك",
            "<p>تغيّرت كلمة مرور حسابك في {{occurredAt}}.</p>"
            + "<p>إن لم تكن أنت، غيّرها فورًا وأبلغ مسؤول النظام: من غيّرها يستطيع"
            + " الدخول إلى حسابك الآن.</p>",
            ["occurredAt"]),

        new("security.password.changed", "en",
            "Your password was changed",
            "<p>The password on your account was changed on {{occurredAt}}.</p>"
            + "<p>If this was not you, change it now and tell your administrator — "
            + "whoever changed it can sign in as you until you do.</p>",
            ["occurredAt"]),

        new("security.password.reset", "ar",
            "إعادة تعيين كلمة المرور",
            "<p>طُلبت إعادة تعيين كلمة مرور حسابك.</p>"
            + "<p>استخدم هذا الرمز خلال {{validForMinutes}} دقيقة: <strong>{{token}}</strong></p>"
            + "<p>إن لم تطلبها أنت، تجاهل هذه الرسالة — لن يتغيّر شيء.</p>",
            ["token", "validForMinutes"]),

        new("security.password.reset", "en",
            "Reset your password",
            "<p>A password reset was requested for your account.</p>"
            + "<p>Use this code within {{validForMinutes}} minutes: <strong>{{token}}</strong></p>"
            + "<p>If you did not ask for this, ignore this message — nothing will change.</p>",
            ["token", "validForMinutes"]),

        new("security.mfa.enrolled", "ar",
            "فُعّل التحقّق بخطوتين على حسابك",
            "<p>فُعّل التحقّق بخطوتين على حسابك في {{occurredAt}}.</p>"
            + "<p>إن لم تكن أنت، أبلغ مسؤول النظام فورًا.</p>",
            ["occurredAt"]),

        new("security.mfa.enrolled", "en",
            "Two-factor authentication was turned on",
            "<p>Two-factor authentication was turned on for your account on {{occurredAt}}.</p>"
            + "<p>If this was not you, tell your administrator now.</p>",
            ["occurredAt"])
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<INotificationUnitOfWork>();

        DateTimeOffset now = clock.UtcNow;
        int added = 0;

        foreach (SeededTemplate seed in Templates)
        {
            NotificationTemplate? existing = await repository.FindTemplateAsync(
                seed.Code, seed.Locale, cancellationToken);

            if (existing is not null)
            {
                // Left alone. An administrator who reworded this keeps their
                // wording; a seeder that overwrote on boot would undo somebody's
                // work on every deployment, silently.
                continue;
            }

            Kernel.Results.Result<NotificationTemplate> template = NotificationTemplate.Create(
                seed.Code, seed.Locale, seed.Subject, seed.Body, seed.Variables, now);

            if (template.IsFailure)
            {
                // Loud, because a seeded template that will not validate is a
                // coding error and the Platform will fail to send that message
                // for as long as nobody notices.
                logger.LogError(
                    "The seeded template {Code}/{Locale} is not valid: {Error}",
                    seed.Code, seed.Locale, template.Errors[0].Message);

                continue;
            }

            repository.AddTemplate(template.Value);
            added++;
        }

        if (added > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Seeded {Count} notification templates.", added);
            }
        }
    }
}

/// <summary>One template the Platform ships with.</summary>
public sealed record SeededTemplate(
    string Code,
    string Locale,
    string Subject,
    string Body,
    IReadOnlyList<string> Variables);
