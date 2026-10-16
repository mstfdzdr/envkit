using EnvKit.Windows.Variables;

namespace EnvKit.Windows.Tests.Variables;

public class SettingChangeBroadcasterTests
{
    [Fact]
    public void Broadcast_is_delivered() => Assert.True(new SettingChangeBroadcaster().Broadcast());
}
