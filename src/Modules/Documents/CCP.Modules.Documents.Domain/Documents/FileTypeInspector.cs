using System.Text;

namespace CCP.Modules.Documents.Domain.Documents;

/// <summary>
/// Decides what a file actually is by reading it, and whether the Platform
/// accepts that.
/// <para>
/// <b>The declared type is not trusted</b> (§18.2). It is a string the caller
/// chose, and so is the extension; both are trivially set to <c>application/pdf</c>
/// on a Windows executable. The only thing that resists that is the content.
/// </para>
/// <para>
/// This lives in the domain, not the infrastructure, because "which files may
/// this company store" is a rule and not a detail of how HTTP works. It reads a
/// header, never the whole file, so cost does not grow with size.
/// </para>
/// </summary>
public static class FileTypeInspector
{
    /// <summary>
    /// How many bytes are enough to identify anything here.
    /// <para>
    /// Every signature below sits inside the first few bytes. The larger buffer
    /// exists for the text check, which needs enough to be confident and not so
    /// much that it becomes a scan of the file.
    /// </para>
    /// </summary>
    public const int HeaderLength = 512;

    /// <summary>What a recognised file turned out to be.</summary>
    /// <param name="IsRecognised">Whether the bytes matched anything known.</param>
    /// <param name="IsAllowed">Whether the Platform stores files of this kind.</param>
    /// <param name="ContentType">The media type to store and to serve back.</param>
    /// <param name="Label">A short human name, used in the refusal message.</param>
    public readonly record struct FileInspection(
        bool IsRecognised,
        bool IsAllowed,
        string ContentType,
        string Label)
    {
        public static FileInspection Unknown { get; } =
            new(false, false, "application/octet-stream", "unrecognised");

        /// <summary>Recognised, and refused on purpose.</summary>
        public static FileInspection Refused(string label) =>
            new(true, false, "application/octet-stream", label);

        public static FileInspection Allowed(string contentType, string label) =>
            new(true, true, contentType, label);
    }

    /// <summary>
    /// Identify a file from its first bytes and its name.
    /// <para>
    /// The name is consulted for <i>one</i> thing: which member of the ZIP
    /// family a ZIP container is. Office formats are ZIP archives with a
    /// particular directory inside, and telling a <c>.docx</c> from an
    /// <c>.xlsx</c> means opening the archive. The security question — "is this
    /// a ZIP or an executable?" — is answered by the bytes; the cosmetic
    /// question of which ZIP is answered by the name, and the worst outcome of
    /// getting it wrong is a spreadsheet labelled as a document.
    /// </para>
    /// </summary>
    public static FileInspection Inspect(ReadOnlySpan<byte> header, string? fileName)
    {
        if (header.IsEmpty)
        {
            return FileInspection.Unknown;
        }

        // --- Refused outright, and named -------------------------------------
        //
        // Recognising these buys a message that says what the file is instead of
        // "not accepted", which is the difference between a person fixing their
        // upload and a person filing a ticket.

        if (StartsWith(header, "MZ"u8))
        {
            return FileInspection.Refused("a Windows program");
        }

        if (StartsWith(header, [0x7F, (byte)'E', (byte)'L', (byte)'F']))
        {
            return FileInspection.Refused("a Linux program");
        }

        if (StartsWith(header, "#!"u8))
        {
            return FileInspection.Refused("a script");
        }

        // --- Accepted --------------------------------------------------------

        if (StartsWith(header, "%PDF-"u8))
        {
            return FileInspection.Allowed("application/pdf", "PDF");
        }

        if (StartsWith(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return FileInspection.Allowed("image/png", "PNG image");
        }

        if (StartsWith(header, [0xFF, 0xD8, 0xFF]))
        {
            return FileInspection.Allowed("image/jpeg", "JPEG image");
        }

        if (StartsWith(header, "GIF87a"u8) || StartsWith(header, "GIF89a"u8))
        {
            return FileInspection.Allowed("image/gif", "GIF image");
        }

        if (IsWebP(header))
        {
            return FileInspection.Allowed("image/webp", "WebP image");
        }

        if (IsZipContainer(header))
        {
            return InspectZipContainer(fileName);
        }

        // Text has no signature, so it is decided by exclusion — after every
        // signature has had its turn, and never before, or a PNG whose header
        // happened to decode would be stored as a text file.
        return LooksLikeText(header)
            ? FileInspection.Allowed("text/plain; charset=utf-8", "text")
            : FileInspection.Unknown;
    }

    /// <summary>
    /// A ZIP archive, resolved to which of the ZIP-based formats it claims to be.
    /// <para>
    /// Note what this does <i>not</i> promise. A ZIP is a container, and the
    /// Platform accepting one means accepting whatever is inside it — including
    /// an Office document carrying a macro. That is what the malware scan hook
    /// is for; refusing every ZIP-based format instead would mean refusing
    /// Word and Excel, which is not an option a company can live with.
    /// </para>
    /// </summary>
    private static FileInspection InspectZipContainer(string? fileName)
    {
        string extension = ExtensionOf(fileName);

        return extension switch
        {
            ".docx" => FileInspection.Allowed(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "Word document"),

            ".xlsx" => FileInspection.Allowed(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "Excel workbook"),

            ".pptx" => FileInspection.Allowed(
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "PowerPoint presentation"),

            ".odt" => FileInspection.Allowed(
                "application/vnd.oasis.opendocument.text", "OpenDocument text"),

            ".ods" => FileInspection.Allowed(
                "application/vnd.oasis.opendocument.spreadsheet", "OpenDocument spreadsheet"),

            ".zip" => FileInspection.Allowed("application/zip", "ZIP archive"),

            // A ZIP under any other name. Refused rather than stored as a plain
            // archive: an unexpected extension on a container is the shape of
            // somebody trying an extension that was not on anybody's list.
            _ => FileInspection.Refused("an archive with an unexpected name")
        };
    }

    /// <summary>
    /// Whether the header is plausibly UTF-8 text.
    /// <para>
    /// Deliberately strict. Control characters other than tab, carriage return
    /// and line feed are refused, because their presence means the file is
    /// binary and only looked like text for a few hundred bytes.
    /// </para>
    /// </summary>
    private static bool LooksLikeText(ReadOnlySpan<byte> header)
    {
        ReadOnlySpan<byte> body = StartsWith(header, [0xEF, 0xBB, 0xBF])
            ? header[3..]
            : header;

        if (body.IsEmpty)
        {
            return false;
        }

        foreach (byte value in body)
        {
            if (value is 0x09 or 0x0A or 0x0D)
            {
                continue;
            }

            if (value < 0x20 || value == 0x7F)
            {
                return false;
            }
        }

        // A header is a prefix of the file, so the last character may genuinely
        // be a multi-byte sequence cut in half. Decoding strictly would reject a
        // perfectly good Arabic text file for being long.
        var decoder = Encoding.UTF8.GetDecoder();
        decoder.Fallback = DecoderFallback.ExceptionFallback;

        try
        {
            Span<char> destination = stackalloc char[HeaderLength * 2];

            decoder.Convert(
                body, destination, flush: false,
                out _, out _, out _);

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>Local file header, or an empty archive, or a spanned one.</summary>
    private static bool IsZipContainer(ReadOnlySpan<byte> header) =>
        StartsWith(header, [0x50, 0x4B, 0x03, 0x04])
        || StartsWith(header, [0x50, 0x4B, 0x05, 0x06])
        || StartsWith(header, [0x50, 0x4B, 0x07, 0x08]);

    /// <summary>RIFF at 0 and WEBP at 8, with the length between them.</summary>
    private static bool IsWebP(ReadOnlySpan<byte> header) =>
        header.Length >= 12
        && StartsWith(header, "RIFF"u8)
        && header[8..12].SequenceEqual("WEBP"u8);

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> signature) =>
        header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature);

    private static string ExtensionOf(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        int dot = fileName.LastIndexOf('.');

        return dot < 0 ? string.Empty : fileName[dot..].ToLowerInvariant();
    }
}
