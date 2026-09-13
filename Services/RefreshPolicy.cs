// SPDX-License-Identifier: GPL-3.0-only
namespace Beastdex.Services;

public static class RefreshPolicy
{
    public static bool IsDue(long elapsedMilliseconds, bool enabled, int intervalSeconds, bool forced) =>
        forced || (enabled && elapsedMilliseconds >= Math.Clamp(intervalSeconds, 1, 30) * 1000L);
}
