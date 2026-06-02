using Shared.Domain;

namespace ControlPlane.Tests.Features.AgentTokens;

public class WorkItemClaimTests
{
    private readonly Guid _ownerId = Guid.NewGuid();

    [Fact]
    public void HasActiveClaim_ReturnsFalse_WhenNoClaimSet()
    {
        var item = new WorkItem();
        Assert.False(item.HasActiveClaim(DateTime.UtcNow));
    }

    [Fact]
    public void HasActiveClaim_ReturnsFalse_WhenClaimExpired()
    {
        var item = new WorkItem
        {
            ClaimOwner = _ownerId,
            ClaimExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        };
        Assert.False(item.HasActiveClaim(DateTime.UtcNow));
    }

    [Fact]
    public void HasActiveClaim_ReturnsTrue_WhenClaimActive()
    {
        var now = DateTime.UtcNow;
        var item = new WorkItem
        {
            ClaimOwner = _ownerId,
            ClaimExpiresAt = now.AddMinutes(5)
        };
        Assert.True(item.HasActiveClaim(now));
    }

    [Fact]
    public void IsClaimOwnedBy_ReturnsFalse_WhenNoClaim()
    {
        var item = new WorkItem();
        Assert.False(item.IsClaimOwnedBy(_ownerId, DateTime.UtcNow));
    }

    [Fact]
    public void IsClaimOwnedBy_ReturnsFalse_WhenDifferentOwner()
    {
        var now = DateTime.UtcNow;
        var item = new WorkItem
        {
            ClaimOwner = Guid.NewGuid(),
            ClaimExpiresAt = now.AddMinutes(5)
        };
        Assert.False(item.IsClaimOwnedBy(_ownerId, now));
    }

    [Fact]
    public void IsClaimOwnedBy_ReturnsFalse_WhenClaimExpired()
    {
        var item = new WorkItem
        {
            ClaimOwner = _ownerId,
            ClaimExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        };
        Assert.False(item.IsClaimOwnedBy(_ownerId, DateTime.UtcNow));
    }

    [Fact]
    public void IsClaimOwnedBy_ReturnsTrue_WhenOwnerAndActive()
    {
        var now = DateTime.UtcNow;
        var item = new WorkItem
        {
            ClaimOwner = _ownerId,
            ClaimExpiresAt = now.AddMinutes(5)
        };
        Assert.True(item.IsClaimOwnedBy(_ownerId, now));
    }

    [Fact]
    public void IsClaimExpired_ReturnsFalse_WhenNoClaim()
    {
        var item = new WorkItem();
        Assert.False(item.IsClaimExpired(DateTime.UtcNow));
    }

    [Fact]
    public void IsClaimExpired_ReturnsTrue_WhenPastExpiry()
    {
        var item = new WorkItem
        {
            ClaimOwner = _ownerId,
            ClaimExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        };
        Assert.True(item.IsClaimExpired(DateTime.UtcNow));
    }
}
