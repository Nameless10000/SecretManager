using System;

namespace SecretManager.App.Services;

public static class MotionGeometry
{
    // Six pixels of growth on either side; the list reserves twelve, including spring overshoot.
    public const double CardGutter = 12;
    public static float CardHoverScale(double width) => (float)(1 + Math.Min(.012, 12 / Math.Max(1, width)));
}
