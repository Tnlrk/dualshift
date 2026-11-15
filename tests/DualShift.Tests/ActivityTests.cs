using DualShift.Bridge;
using DualShift.DualSense;

namespace DualShift.Tests;

public class ActivityTests
{
    private static DualSenseState S(byte lx = 128, byte ly = 128, byte l2 = 0, DsButtons b = DsButtons.None) =>
        new(lx, ly, 128, 128, l2, 0, b, 80, ChargingState.Discharging);

    [Fact]
    public void StickJitterIsNotActivity() => Assert.False(Activity.IsActivity(S(), S(lx: 131, ly: 125)));

    [Fact]
    public void StickMoveIsActivity() => Assert.True(Activity.IsActivity(S(), S(lx: 200)));

    [Fact]
    public void ButtonPressIsActivity() => Assert.True(Activity.IsActivity(S(), S(b: DsButtons.Cross)));

    [Fact]
    public void TriggerPressIsActivity() => Assert.True(Activity.IsActivity(S(), S(l2: 40)));

    [Fact]
    public void BatteryChangeAloneIsNotActivity()
    {
        var a = S();
        var b = a with { BatteryPercent = 70 };
        Assert.False(Activity.IsActivity(a, b));
    }
}
