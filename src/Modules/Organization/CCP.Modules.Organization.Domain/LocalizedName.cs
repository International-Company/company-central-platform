using CCP.Kernel.Results;

namespace CCP.Modules.Organization.Domain;

/// <summary>
/// A name in both company languages.
/// <para>
/// Organizational names are stored in Arabic and English rather than translated
/// at render time (ADR-011 §25.3). A department has an official name in each
/// language, chosen by the company — it is data, not a translation, and guessing
/// it would be wrong in a way users notice immediately.
/// </para>
/// <para>
/// Both are required. An optional second language becomes a permanently empty
/// column: whoever creates the record is in a hurry, nobody comes back, and the
/// Arabic interface ends up showing English names — which is exactly the
/// second-class experience ADR-011 exists to prevent.
/// </para>
/// </summary>
public sealed record LocalizedName
{
    public const int MaxLength = 200;

    private LocalizedName(string arabic, string english)
    {
        Arabic = arabic;
        English = english;
    }

    public string Arabic { get; }

    public string English { get; }

    public static Result<LocalizedName> Create(string? arabic, string? english)
    {
        List<Error> errors = [];

        if (string.IsNullOrWhiteSpace(arabic))
        {
            errors.Add(OrganizationErrors.NameArabicRequired);
        }
        else if (arabic.Trim().Length > MaxLength)
        {
            errors.Add(OrganizationErrors.NameArabicTooLong);
        }

        if (string.IsNullOrWhiteSpace(english))
        {
            errors.Add(OrganizationErrors.NameEnglishRequired);
        }
        else if (english.Trim().Length > MaxLength)
        {
            errors.Add(OrganizationErrors.NameEnglishTooLong);
        }

        return errors.Count > 0
            ? Result.Failure<LocalizedName>(errors)
            : Result.Success(new LocalizedName(arabic!.Trim(), english!.Trim()));
    }

    /// <summary>
    /// The name for a locale, falling back to the other rather than returning
    /// nothing. A missing name is never the right thing to show a user.
    /// </summary>
    public string For(string locale)
        => locale?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true
            ? Arabic
            : English;

    public override string ToString() => English;
}
