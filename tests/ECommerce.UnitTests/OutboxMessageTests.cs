using ECommerce.Domain.Entities;
using FluentAssertions;

namespace ECommerce.UnitTests;

public class OutboxMessageTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_ShouldStartPendingWithStableIdempotencyKey()
    {
        var first = OutboxMessage.Create("Email", "{\"to\":\"a@b.com\"}").Value!;
        var second = OutboxMessage.Create("Email", "{\"to\":\"a@b.com\"}").Value!;

        first.Status.Should().Be(OutboxMessageStatus.Pending);
        first.ProcessedOnUtc.Should().BeNull();
        first.RetryCount.Should().Be(0);
        first.IdempotencyKey.Should().StartWith("sha256:");
        first.IdempotencyKey.Should().Be(second.IdempotencyKey);
    }

    [Fact]
    public void Create_ShouldRejectInvalidInput()
    {
        OutboxMessage.Create("", "content").IsFailure.Should().BeTrue();
        OutboxMessage.Create("Email", " ").IsFailure.Should().BeTrue();
    }

    [Fact]
    public void CanBeClaimed_ShouldFollowLeaseAndRetryState()
    {
        var message = OutboxMessage.Create("Email", "content").Value!;

        message.CanBeClaimed(Now).Should().BeTrue();

        message.MarkAsProcessing("worker-1", TimeSpan.FromMinutes(1), Now);
        message.CanBeClaimed(Now).Should().BeFalse();
        message.CanBeClaimed(Now.AddMinutes(1).AddTicks(1)).Should().BeTrue();

        message.MarkAsFailed("temporary", Now.AddSeconds(30));
        message.CanBeClaimed(Now.AddSeconds(29)).Should().BeFalse();
        message.CanBeClaimed(Now.AddSeconds(31)).Should().BeTrue();

        message.MarkAsProcessed(Now.AddMinutes(2));
        message.CanBeClaimed(Now.AddHours(1)).Should().BeFalse();
    }

    [Fact]
    public void MarkAsFailed_ShouldKeepMessageRetryable()
    {
        var message = OutboxMessage.Create("Email", "content").Value!;
        message.MarkAsProcessing("worker", TimeSpan.FromMinutes(1), Now);

        message.MarkAsFailed(new string('x', 3000), Now.AddSeconds(5));

        message.Status.Should().Be(OutboxMessageStatus.Pending);
        message.RetryCount.Should().Be(1);
        message.ErrorMessage.Should().HaveLength(OutboxMessage.ErrorMessageMaxLength);
        message.LeaseOwner.Should().BeNull();
        message.LeaseExpiresAtUtc.Should().BeNull();
        message.NextAttemptAtUtc.Should().Be(Now.AddSeconds(5));
        message.ProcessedOnUtc.Should().BeNull();
    }

    [Fact]
    public void MarkAsDeadLettered_ShouldBeTerminal()
    {
        var message = OutboxMessage.Create("Email", "content").Value!;
        message.MarkAsProcessing("worker", TimeSpan.FromMinutes(1), Now);

        message.MarkAsDeadLettered("unknown handler");

        message.Status.Should().Be(OutboxMessageStatus.Failed);
        message.RetryCount.Should().Be(1);
        message.NextAttemptAtUtc.Should().BeNull();
        message.CanBeClaimed(Now.AddDays(1)).Should().BeFalse();
        message.Invoking(x => x.MarkAsProcessed(Now))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ExplicitIdempotencyKey_ShouldBeNormalized()
    {
        var withKey = OutboxMessage.Create("Email", "content", idempotencyKey: "reset:1:token").Value!;
        var sameKey = OutboxMessage.Create("Email", "different-content", idempotencyKey: "reset:1:token").Value!;
        var noKey = OutboxMessage.Create("Email", "content").Value!;

        withKey.IdempotencyKey.Should().Be(sameKey.IdempotencyKey);
        withKey.IdempotencyKey.Should().NotBe(noKey.IdempotencyKey);
    }
}
