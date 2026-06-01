using Shared.Domain;

namespace ControlPlane.Tests.Features.AgentTokens;

public class IntegrationScheduleStateLeaseTests
{
    private readonly Guid _ownerId = Guid.NewGuid();

    [Fact]
    public void HasActiveLease_ReturnsFalse_WhenNoLeaseSet()
    {
        var state = new IntegrationScheduleState();
        var now = DateTime.UtcNow;

        Assert.False(state.HasActiveLease(now));
    }

    [Fact]
    public void HasActiveLease_ReturnsFalse_WhenLeaseExpired()
    {
        var state = new IntegrationScheduleState
        {
            LeaseOwnerId = _ownerId,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        };
        var now = DateTime.UtcNow;

        Assert.False(state.HasActiveLease(now));
    }

    [Fact]
    public void HasActiveLease_ReturnsTrue_WhenLeaseActive()
    {
        var now = DateTime.UtcNow;
        var state = new IntegrationScheduleState
        {
            LeaseOwnerId = _ownerId,
            LeaseExpiresAt = now.AddMinutes(5)
        };

        Assert.True(state.HasActiveLease(now));
    }

    [Fact]
    public void IsLeaseOwnedBy_ReturnsFalse_WhenNoLease()
    {
        var state = new IntegrationScheduleState();
        var now = DateTime.UtcNow;

        Assert.False(state.IsLeaseOwnedBy(_ownerId, now));
    }

    [Fact]
    public void IsLeaseOwnedBy_ReturnsFalse_WhenDifferentOwner()
    {
        var now = DateTime.UtcNow;
        var state = new IntegrationScheduleState
        {
            LeaseOwnerId = Guid.NewGuid(),
            LeaseExpiresAt = now.AddMinutes(5)
        };

        Assert.False(state.IsLeaseOwnedBy(_ownerId, now));
    }

    [Fact]
    public void IsLeaseOwnedBy_ReturnsFalse_WhenLeaseExpired()
    {
        var state = new IntegrationScheduleState
        {
            LeaseOwnerId = _ownerId,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        };
        var now = DateTime.UtcNow;

        Assert.False(state.IsLeaseOwnedBy(_ownerId, now));
    }

    [Fact]
    public void IsLeaseOwnedBy_ReturnsTrue_WhenOwnerAndActive()
    {
        var now = DateTime.UtcNow;
        var state = new IntegrationScheduleState
        {
            LeaseOwnerId = _ownerId,
            LeaseExpiresAt = now.AddMinutes(5)
        };

        Assert.True(state.IsLeaseOwnedBy(_ownerId, now));
    }

    [Fact]
    public void IsLeaseOwnedBy_ReturnsTrue_ExactlyAtExpiry()
    {
        var now = DateTime.UtcNow;
        var state = new IntegrationScheduleState
        {
            LeaseOwnerId = _ownerId,
            LeaseExpiresAt = now.AddMilliseconds(1) // Just barely not expired
        };

        Assert.True(state.IsLeaseOwnedBy(_ownerId, now));
    }
}
