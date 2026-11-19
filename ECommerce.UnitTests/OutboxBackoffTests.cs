using ECommerce.Infrastructure.BackgroundJobs;
using FluentAssertions;

namespace ECommerce.UnitTests;

public class OutboxBackoffTests
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    [InlineData(5, 60)]
    [InlineData(10, 60)]
    public void Calculate_ShouldGrowExponentiallyAndCap(int attempt, int expectedSeconds)
    {
        OutboxBackoff.Calculate(attempt, BaseDelay, MaxDelay)
            .Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void Calculate_ShouldSupportDisabledDelay()
    {
        OutboxBackoff.Calculate(3, TimeSpan.Zero, MaxDelay).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Calculate_ShouldRejectInvalidAttempt()
    {
        var act = () => OutboxBackoff.Calculate(0, BaseDelay, MaxDelay);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
