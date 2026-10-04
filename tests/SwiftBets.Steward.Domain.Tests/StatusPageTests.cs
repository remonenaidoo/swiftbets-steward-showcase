using SwiftBets.Steward.Domain.Status;

namespace SwiftBets.Steward.Domain.Tests;

public sealed class StatusPageTests
{
    [Fact]
    public void Component_with_every_check_healthy_is_operational_and_page_follows_the_worst_component()
    {
        StatusPage.ComponentState([true, true]).ShouldBe(StatusPage.Operational);
        StatusPage.Overall([StatusPage.Operational, StatusPage.Degraded, StatusPage.Unknown]).ShouldBe(StatusPage.Degraded);
    }

    [Fact]
    public void Component_with_some_or_no_healthy_checks_is_degraded_or_out()
    {
        StatusPage.ComponentState([true, false]).ShouldBe(StatusPage.Degraded);
        StatusPage.ComponentState([false, false]).ShouldBe(StatusPage.Outage);
    }
}
