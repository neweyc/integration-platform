using Shared.Domain;

namespace ControlPlane.Tests.Features.Environments;

public class EnvironmentKeyTests
{
    [Theory]
    [InlineData("Production", "production")]
    [InlineData("  STAGING  ", "staging")]
    [InlineData("Dev-1", "dev-1")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize_TrimsAndLowercases(string? raw, string expected)
    {
        Assert.Equal(expected, EnvironmentKey.Normalize(raw));
    }

    [Theory]
    [InlineData("production", true)]
    [InlineData("staging-2", true)]
    [InlineData("dev", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("UPPER", false)]      // canonical form is lowercase; callers must Normalize first
    [InlineData("under_score", false)]
    public void IsValid_EnforcesSlugShape(string name, bool expected)
    {
        Assert.Equal(expected, EnvironmentKey.IsValid(name));
    }

    [Fact]
    public void IsValid_RejectsOverlongNames()
    {
        var tooLong = new string('a', EnvironmentKey.MaxLength + 1);
        Assert.False(EnvironmentKey.IsValid(tooLong));
    }
}
