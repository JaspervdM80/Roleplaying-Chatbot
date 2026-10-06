using Microsoft.Extensions.Time.Testing;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Tests.Web;

public class WordingTests
{
    // 00:30 on a Tuesday in Amsterdam (UTC+2), still Monday in UTC: the day labels must follow the user's zone.
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 22, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(20, "Just now")]
    [InlineData(12 * 60, "12 min")]
    [InlineData(25 * 60, "25 min")]
    [InlineData(75 * 60, "Yesterday")]
    [InlineData(3 * 24 * 3600, "Sat")]
    [InlineData(8 * 24 * 3600, "28 Sep")]
    [InlineData(300 * 24 * 3600, "9 Dec 2025")]
    public void How_long_ago_counts_days_in_the_users_time_zone(int secondsAgo, string expected)
    {
        var time = new FakeTimeProvider(Now);
        time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"));

        Assert.Equal(expected, Wording.Ago(Now.AddSeconds(-secondsAgo), time));
    }

    [Fact]
    public void A_chat_from_earlier_today_shows_hours()
    {
        var time = new FakeTimeProvider(Now.AddHours(12));
        time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"));

        Assert.Equal("5 h", Wording.Ago(Now.AddHours(7), time));
    }
}
