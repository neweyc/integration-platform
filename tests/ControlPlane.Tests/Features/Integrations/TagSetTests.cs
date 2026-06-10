using ControlPlane.Features.Integrations;

namespace ControlPlane.Tests.Features.Integrations;

public class TagSetTests
{
    [Fact]
    public void Normalize_TrimsDropsBlanksAndDeduplicatesCaseInsensitively()
    {
        var result = TagSet.Normalize([" hardware-signal ", "GPU", "gpu", "", "  ", "hardware-signal"]);

        Assert.Equal(2, result.Length);
        Assert.Contains("hardware-signal", result);
        Assert.Contains("GPU", result);
    }

    [Fact]
    public void Normalize_NullIsEmpty()
    {
        Assert.Empty(TagSet.Normalize(null));
    }

    [Theory]
    [InlineData(new[] { "a", "b" }, new[] { "b", "a" }, true)]   // order-insensitive
    [InlineData(new[] { "a", "B" }, new[] { "b", "A" }, true)]   // case-insensitive
    [InlineData(new[] { "a" }, new[] { "a", "b" }, false)]       // different size
    [InlineData(new string[0], new string[0], true)]            // both empty
    [InlineData(new[] { "a" }, new[] { "c" }, false)]            // disjoint
    public void Equal_ComparesAsCaseInsensitiveSet(string[] a, string[] b, bool expected)
    {
        Assert.Equal(expected, TagSet.Equal(a, b));
    }
}
