namespace EmpireSim;

/// <summary>
/// Visible build stamp. BUMP <see cref="Build"/> ON EVERY RELEASE COMMIT.
/// The number is shown in the top bar and on the nation select screen, so a
/// stale deploy on the emulator is instantly recognisable (three stale-build
/// confusions in one day on 2026-10-08: population numbers, rectangles, boxes).
/// When telling blyr to pull, always tell him the new build number to expect.
/// </summary>
public static class AppVersion
{
    public const int Build = 9;
}
