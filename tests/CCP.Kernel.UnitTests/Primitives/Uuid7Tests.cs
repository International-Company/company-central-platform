using CCP.Kernel.Primitives;

namespace CCP.Kernel.UnitTests.Primitives;

/// <summary>
/// UUID v7 is the primary key type for every table in the Platform (ADR-004).
/// Its two properties that matter are tested here: the layout is a valid v7,
/// and generated values sort in creation order — which is the entire reason it
/// was chosen over v4.
/// </summary>
public sealed class Uuid7Tests
{
    [Fact]
    public void NewGuid_SetsVersionToSeven()
    {
        Guid value = Uuid7.NewGuid();

        byte[] bytes = ToBigEndian(value);

        Assert.Equal(0x70, bytes[6] & 0xF0);
    }

    [Fact]
    public void NewGuid_SetsRfc9562Variant()
    {
        Guid value = Uuid7.NewGuid();

        byte[] bytes = ToBigEndian(value);

        // The two most significant bits of byte 8 must be 10.
        Assert.Equal(0x80, bytes[8] & 0xC0);
    }

    [Fact]
    public void NewGuid_EmbedsTheSuppliedTimestamp()
    {
        var timestamp = new DateTimeOffset(2026, 3, 14, 9, 26, 53, TimeSpan.Zero);

        Guid value = Uuid7.NewGuid(timestamp);

        // Millisecond precision is all the format carries.
        Assert.Equal(
            timestamp.ToUnixTimeMilliseconds(),
            Uuid7.GetTimestamp(value).ToUnixTimeMilliseconds());
    }

    [Fact]
    public void NewGuid_ValuesSortInCreationOrder()
    {
        // The property that makes v7 index well: lexicographic order matches
        // chronological order, so inserts land at the end of the B-tree instead
        // of scattering across it.
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        List<Guid> generated =
        [
            .. Enumerable.Range(0, 200)
                .Select(i => Uuid7.NewGuid(baseTime.AddMilliseconds(i)))
        ];

        List<Guid> sorted = [.. generated.OrderBy(g => g.ToString("N", null), StringComparer.Ordinal)];

        Assert.Equal(generated, sorted);
    }

    [Fact]
    public void NewGuid_ProducesDistinctValuesWithinTheSameMillisecond()
    {
        var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        HashSet<Guid> values = [.. Enumerable.Range(0, 5_000).Select(_ => Uuid7.NewGuid(timestamp))];

        // 74 random bits: a collision here would mean the randomness is broken.
        Assert.Equal(5_000, values.Count);
    }

    [Fact]
    public void NewGuid_IsNotEmpty()
    {
        Assert.NotEqual(Guid.Empty, Uuid7.NewGuid());
    }

    /// <summary>
    /// Guid stores its first three groups in native byte order, so the raw
    /// bytes must be rearranged before the RFC field positions can be read.
    /// </summary>
    private static byte[] ToBigEndian(Guid value)
    {
        byte[] raw = value.ToByteArray();

        if (!BitConverter.IsLittleEndian)
        {
            return raw;
        }

        return
        [
            raw[3], raw[2], raw[1], raw[0],
            raw[5], raw[4],
            raw[7], raw[6],
            raw[8], raw[9], raw[10], raw[11], raw[12], raw[13], raw[14], raw[15]
        ];
    }
}
