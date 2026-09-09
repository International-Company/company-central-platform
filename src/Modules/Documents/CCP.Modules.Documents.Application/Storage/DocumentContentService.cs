using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using CCP.Kernel.Results;
using CCP.Modules.Documents.Application.Abstractions;
using CCP.Modules.Documents.Domain;
using CCP.Modules.Documents.Domain.Documents;

namespace CCP.Modules.Documents.Application.Storage;

/// <summary>
/// Everything that happens to a file between arriving and being stored.
/// <para>
/// <b>The order is the design.</b> Buffer, measure, identify, hash, scan, and
/// only then write. Each step can refuse, and a refusal at any of them leaves
/// nothing behind — no partial object, no row, no half-scanned file sitting in a
/// bucket waiting for somebody to notice it.
/// </para>
/// <para>
/// It is separate from the handlers because the first upload and a new version
/// are the same pipeline with different metadata, and two copies of this would
/// be two chances to leave out the scan.
/// </para>
/// </summary>
public sealed class DocumentContentService(
    IDocumentStorageProvider storage,
    IDocumentScanner scanner,
    DocumentOptions options)
{
    /// <summary>
    /// Content that made it through, and everything the version row needs.
    /// </summary>
    public sealed record StoredContent(
        string ObjectKey,
        string ContentType,
        long SizeInBytes,
        string Sha256,
        ScanVerdict ScanVerdict,
        string ScannerName);

    /// <summary>
    /// Takes an incoming stream and leaves the bytes in object storage.
    /// </summary>
    /// <param name="source">
    /// The uploaded content. Read once, forward only — it is a network stream,
    /// not a file, and anything wanting to look at it twice has to buffer first.
    /// </param>
    /// <param name="fileName">The name from the uploader. Metadata, and a hint about which ZIP.</param>
    /// <param name="declaredContentType">What the caller said it was. Checked, never believed.</param>
    public async Task<Result<StoredContent>> AcceptAsync(
        Stream source,
        string fileName,
        string? declaredContentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Buffered to a temporary file rather than to memory. Twenty-five
        // megabytes is a reasonable file and an unreasonable allocation when
        // forty people upload at once, and the pipeline needs to read the
        // content three times — to identify it, to hash it, and to store it.
        string temporaryPath = Path.Combine(Path.GetTempPath(), $"ccp-upload-{Guid.NewGuid():N}");

        try
        {
            await using (var buffer = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, useAsync: true))
            {
                Result<long> copied = await CopyWithLimitAsync(source, buffer, cancellationToken);

                if (copied.IsFailure)
                {
                    return Result.Failure<StoredContent>(copied.Error);
                }

                if (copied.Value == 0)
                {
                    return Result.Failure<StoredContent>(DocumentErrors.EmptyFile);
                }
            }

            await using var stored = new FileStream(
                temporaryPath, FileMode.Open, FileAccess.Read, FileShare.None,
                bufferSize: 64 * 1024, useAsync: true);

            Result<string> contentType = await IdentifyAsync(stored, fileName, declaredContentType, cancellationToken);

            if (contentType.IsFailure)
            {
                return Result.Failure<StoredContent>(contentType.Error);
            }

            stored.Position = 0;
            byte[] digest = await SHA256.HashDataAsync(stored, cancellationToken);
            string sha256 = Convert.ToHexStringLower(digest);

            stored.Position = 0;
            ScanResult scan = await ScanAsync(stored, cancellationToken);

            if (scan.Rejects)
            {
                return Result.Failure<StoredContent>(
                    DocumentErrors.ScanRejected(scan.Detail ?? "no detail given"));
            }

            if (scan.Verdict == ScanVerdict.Failed && options.RejectWhenScannerUnavailable)
            {
                return Result.Failure<StoredContent>(
                    DocumentErrors.ScanRejected(scan.Detail ?? "the scanner could not be reached"));
            }

            stored.Position = 0;
            string objectKey = NewObjectKey();

            await storage.StoreAsync(objectKey, stored, contentType.Value, cancellationToken);

            return Result.Success(new StoredContent(
                objectKey, contentType.Value, stored.Length, sha256, scan.Verdict, scanner.Name));
        }
        finally
        {
            // Best effort. A temporary file left behind by a crashed process is
            // the operating system's problem; one left behind by an ordinary
            // failure here would be ours, and would accumulate.
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // Nothing useful to do, and nothing worth failing an upload over.
            }
        }
    }

    /// <summary>
    /// Copies the upload while counting, and stops the moment it is too big.
    /// <para>
    /// The limit is enforced <b>during</b> the copy and not after it. Checking
    /// a length afterwards means having already written a two-gigabyte file to
    /// disk in order to discover that it was not allowed, which is the denial
    /// of service the limit exists to prevent.
    /// </para>
    /// </summary>
    private async Task<Result<long>> CopyWithLimitAsync(
        Stream source, Stream destination, CancellationToken cancellationToken)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long total = 0;

        try
        {
            int read;

            while ((read = await source.ReadAsync(rented, cancellationToken)) > 0)
            {
                total += read;

                if (total > options.MaxFileSizeInBytes)
                {
                    return Result.Failure<long>(
                        DocumentErrors.FileTooLarge(total, options.MaxFileSizeInBytes));
                }

                await destination.WriteAsync(rented.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        return Result.Success(total);
    }

    /// <summary>
    /// What the file is, according to the file.
    /// </summary>
    private static async Task<Result<string>> IdentifyAsync(
        Stream content, string fileName, string? declaredContentType, CancellationToken cancellationToken)
    {
        byte[] header = new byte[FileTypeInspector.HeaderLength];
        int read = await content.ReadAtLeastAsync(
            header, FileTypeInspector.HeaderLength, throwOnEndOfStream: false, cancellationToken);

        FileTypeInspector.FileInspection inspection =
            FileTypeInspector.Inspect(header.AsSpan(0, read), fileName);

        if (!inspection.IsAllowed)
        {
            return Result.Failure<string>(DocumentErrors.FileTypeNotAllowed(inspection.Label));
        }

        if (!IsCompatible(declaredContentType, inspection.ContentType))
        {
            return Result.Failure<string>(
                DocumentErrors.DeclaredTypeMismatch(declaredContentType!, inspection.ContentType));
        }

        return Result.Success(inspection.ContentType);
    }

    /// <summary>
    /// Whether what the caller claimed is consistent with what was found.
    /// <para>
    /// The detected type is what gets stored either way, so this is not about
    /// choosing between them. It exists to make a <i>deliberate</i> mismatch
    /// visible: a program insisting a Windows executable is a PDF is worth
    /// refusing loudly, even when the content check has already caught it.
    /// </para>
    /// <para>
    /// Anything vague is accepted. A browser that sends
    /// <c>application/octet-stream</c> for an unfamiliar extension is not lying,
    /// and refusing it would break uploads for no gain.
    /// </para>
    /// </summary>
    private static bool IsCompatible(string? declared, string detected)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            return true;
        }

        string claim = declared.Split(';')[0].Trim().ToLowerInvariant();
        string found = detected.Split(';')[0].Trim().ToLowerInvariant();

        if (claim.Length == 0 || claim == "application/octet-stream" || claim == found)
        {
            return true;
        }

        // Text is a family, not a type. A CSV file and a plain text file are
        // the same bytes with different intentions, and the intention is the
        // caller's to declare.
        return claim.StartsWith("text/", StringComparison.Ordinal)
               && found.StartsWith("text/", StringComparison.Ordinal);
    }

    private async Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        try
        {
            return await scanner.ScanAsync(content, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A scanner that throws has failed, and the difference between
            // "failed" and "clean" is the entire value of having one. Swallowing
            // this into a pass is how an outage becomes an infection.
            return ScanResult.Failed(exception.Message);
        }
    }

    /// <summary>
    /// A key that says nothing about the file, the document or the uploader.
    /// <para>
    /// Sharded on its first two characters so a filesystem-backed store does not
    /// end up with a single directory holding every object ever uploaded, which
    /// is slow on some filesystems and unusable on others.
    /// </para>
    /// </summary>
    private static string NewObjectKey()
    {
        string value = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(20));

        return string.Create(
            CultureInfo.InvariantCulture, $"{value[..2]}/{value[2..]}");
    }
}
