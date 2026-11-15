using DualShift.DualSense;

namespace DualShift.Bridge;

/// <summary>Rozpozná, jestli s ovladačem někdo hýbe – drobné chvění páček se nepočítá.</summary>
public static class Activity
{
    /// <summary>Páčky v klidu „šumí“ o pár jednotek; za pohyb se bere změna nad tuto hranici.</summary>
    public const int StickThreshold = 12;
    public const int TriggerThreshold = 8;

    public static bool IsActivity(in DualSenseState reference, in DualSenseState now) =>
        reference.Buttons != now.Buttons ||
        Moved(reference.LeftX, now.LeftX, StickThreshold) || Moved(reference.LeftY, now.LeftY, StickThreshold) ||
        Moved(reference.RightX, now.RightX, StickThreshold) || Moved(reference.RightY, now.RightY, StickThreshold) ||
        Moved(reference.L2, now.L2, TriggerThreshold) || Moved(reference.R2, now.R2, TriggerThreshold);

    private static bool Moved(byte a, byte b, int threshold) => Math.Abs(a - b) > threshold;
}
