using System.Text;
using CCP.Modules.Documents.Domain.Documents;

namespace CCP.Modules.Documents.UnitTests;

/// <summary>
/// What the Platform will and will not store, decided by reading the file.
/// <para>
/// These are the tests that matter most in this module. Every other check
/// assumes the file is what the inspector said it was, and the attack this
/// defends against — a program renamed to <c>report.pdf</c> — is the oldest one
/// there is.
/// </para>
/// </summary>
public sealed class FileTypeInspectorTests
{
    [Fact]
    public void APdfIsRecognisedByItsHeaderAndNotItsName()
    {
        FileTypeInspector.FileInspection inspection =
            FileTypeInspector.Inspect("%PDF-1.7\nrest of it"u8, "anything-at-all.txt");

        Assert.True(inspection.IsAllowed);
        Assert.Equal("application/pdf", inspection.ContentType);
    }

    [Fact]
    public void AWindowsProgramRenamedToPdfIsStillAWindowsProgram()
    {
        // The whole point. The name says PDF, the declared type would say PDF,
        // and the first two bytes say otherwise.
        FileTypeInspector.FileInspection inspection =
            FileTypeInspector.Inspect("MZ\0"u8, "quarterly-report.pdf");

        Assert.False(inspection.IsAllowed);
        Assert.True(inspection.IsRecognised);
        Assert.Equal("a Windows program", inspection.Label);
    }

    [Fact]
    public void ALinuxProgramIsRefusedAndNamed()
    {
        FileTypeInspector.FileInspection inspection = FileTypeInspector.Inspect(
            [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1], "invoice.pdf");

        Assert.False(inspection.IsAllowed);
        Assert.Equal("a Linux program", inspection.Label);
    }

    [Fact]
    public void AShellScriptIsRefused()
    {
        FileTypeInspector.FileInspection inspection =
            FileTypeInspector.Inspect("#!/bin/sh\nrm -rf /"u8, "notes.txt");

        Assert.False(inspection.IsAllowed);
        Assert.Equal("a script", inspection.Label);
    }

    [Theory]
    [InlineData(".docx", "Word document")]
    [InlineData(".xlsx", "Excel workbook")]
    [InlineData(".pptx", "PowerPoint presentation")]
    [InlineData(".zip", "ZIP archive")]
    public void AZipContainerIsResolvedByExtension(string extension, string expectedLabel)
    {
        FileTypeInspector.FileInspection inspection = FileTypeInspector.Inspect(
            [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00], $"contract{extension}");

        Assert.True(inspection.IsAllowed);
        Assert.Equal(expectedLabel, inspection.Label);
    }

    [Fact]
    public void AZipUnderAnUnexpectedNameIsRefused()
    {
        // Recognised as an archive and refused as one. An unexpected extension
        // on a container is the shape of somebody trying extensions until one
        // is not on the list.
        FileTypeInspector.FileInspection inspection = FileTypeInspector.Inspect(
            [0x50, 0x4B, 0x03, 0x04], "payload.jar");

        Assert.False(inspection.IsAllowed);
        Assert.True(inspection.IsRecognised);
    }

    [Fact]
    public void PlainTextIsAccepted()
    {
        FileTypeInspector.FileInspection inspection =
            FileTypeInspector.Inspect("employee,department\n1,finance\n"u8, "people.csv");

        Assert.True(inspection.IsAllowed);
        Assert.StartsWith("text/", inspection.ContentType, StringComparison.Ordinal);
    }

    [Fact]
    public void ArabicTextIsAcceptedEvenWhenTheHeaderCutsACharacterInHalf()
    {
        // A header is a prefix, so the last character of a long Arabic document
        // arrives as half a UTF-8 sequence. Decoding strictly would reject a
        // perfectly good file for being long, which is the sort of bug that only
        // shows up in one language.
        byte[] text = Encoding.UTF8.GetBytes(new string('ع', 400));

        FileTypeInspector.FileInspection inspection =
            FileTypeInspector.Inspect(text.AsSpan(0, FileTypeInspector.HeaderLength - 1), "notes.txt");

        Assert.True(inspection.IsAllowed);
    }

    [Fact]
    public void BinaryThatIsNotRecognisedIsNotTreatedAsText()
    {
        FileTypeInspector.FileInspection inspection = FileTypeInspector.Inspect(
            [0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE], "mystery.bin");

        Assert.False(inspection.IsAllowed);
        Assert.False(inspection.IsRecognised);
    }

    [Fact]
    public void AnEmptyHeaderIsNotAnything()
    {
        FileTypeInspector.FileInspection inspection = FileTypeInspector.Inspect([], "empty.txt");

        Assert.False(inspection.IsAllowed);
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    public void ImagesAreRecognised(byte[] header, string expectedType)
    {
        FileTypeInspector.FileInspection inspection = FileTypeInspector.Inspect(header, "photo");

        Assert.True(inspection.IsAllowed);
        Assert.Equal(expectedType, inspection.ContentType);
    }
}
