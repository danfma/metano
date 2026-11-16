namespace BclReference;

/// <summary>
/// The rows for <c>metano-runtime</c>'s <c>time-span</c> and <c>date-time-offset</c> modules
/// (#673). A <c>TimeSpan</c> row records <c>Ticks</c>; a <c>DateTimeOffset</c> row records the
/// UTC ticks and the offset in minutes, which is what the TypeScript wrappers read back.
/// </summary>
internal static class TemporalRows
{
    public static void AddTo(FixtureWriter rows)
    {
        AddTimeSpanComponents(rows);
        AddTimeSpanUnits(rows);
        AddDateTimeOffset(rows);
    }

    private static void AddTimeSpanComponents(FixtureWriter rows)
    {
        rows.Each(
            "timeSpanFromHoursMinutesSeconds",
            a => new TimeSpan(Int(a, 0), Int(a, 1), Int(a, 2)).Ticks,
            [0, 5, 0],
            [1, 2, 3],
            [-1, -2, -3],
            [0, 90, 0],
            [0, 0, int.MaxValue],
            [int.MaxValue, 0, 0]
        );

        rows.Each(
            "timeSpanFromParts",
            a => new TimeSpan(Int(a, 0), Int(a, 1), Int(a, 2), Int(a, 3)).Ticks,
            [1, 2, 3, 4],
            [0, 0, 0, 0],
            [-1, 0, 0, -4],
            [10675199, 2, 48, 5],
            [10675199, 2, 48, 6],
            [int.MinValue, 0, 0, 0]
        );

        rows.Each(
            "timeSpanFromParts",
            a => new TimeSpan(Int(a, 0), Int(a, 1), Int(a, 2), Int(a, 3), Int(a, 4)).Ticks,
            [1, 2, 3, 4, 5],
            [10675199, 2, 48, 5, 477],
            [10675199, 2, 48, 5, 478],
            [-10675199, -2, -48, -5, -478],
            [0, 0, 0, 0, -1]
        );

        rows.Each(
            "timeSpanFromParts",
            a =>
                new TimeSpan(
                    Int(a, 0),
                    Int(a, 1),
                    Int(a, 2),
                    Int(a, 3),
                    Int(a, 4),
                    Int(a, 5)
                ).Ticks,
            [1, 2, 3, 4, 5, 6],
            [10675199, 2, 48, 5, 477, 580],
            [10675199, 2, 48, 5, 477, 581],
            [-10675199, -2, -48, -5, -477, -581]
        );

        rows.Each("timeSpanZero", _ => TimeSpan.Zero.Ticks, []);
    }

    private static void AddTimeSpanUnits(FixtureWriter rows)
    {
        rows.Each(
            "timeSpanFromDays",
            a => TimeSpan.FromDays(Double(a, 0)).Ticks,
            [1.5],
            [-1.5],
            [0.0],
            [1e-9],
            [double.NaN],
            [1e18],
            [double.PositiveInfinity]
        );

        rows.Each(
            "timeSpanFromHours",
            a => TimeSpan.FromHours(Double(a, 0)).Ticks,
            [1.5],
            [2.0],
            [0.0000001],
            [double.NegativeInfinity]
        );

        rows.Each(
            "timeSpanFromMinutes",
            a => TimeSpan.FromMinutes(Double(a, 0)).Ticks,
            [1.5],
            [90.0],
            [-0.5],
            [1e13]
        );

        rows.Each(
            "timeSpanFromSeconds",
            a => TimeSpan.FromSeconds(Double(a, 0)).Ticks,
            [1.0000005],
            [1.00000051],
            [0.0000001],
            [0.00000005],
            [0.00000015],
            [-0.00000015],
            [30.0],
            [double.NaN],
            [1e18]
        );

        rows.Each(
            "timeSpanFromMilliseconds",
            a => TimeSpan.FromMilliseconds(Double(a, 0)).Ticks,
            [1.5],
            [1.2345],
            [0.00004],
            [-2.5],
            [1e17]
        );

        rows.Each(
            "timeSpanFromMicroseconds",
            a => TimeSpan.FromMicroseconds(Double(a, 0)).Ticks,
            [1.5],
            [0.05],
            [0.15],
            [-3.0]
        );

        rows.Each("timeSpanFromDays", a => TimeSpan.FromDays(Int(a, 0)).Ticks, [2], [-3]);
        rows.Each("timeSpanFromHours", a => TimeSpan.FromHours(Int(a, 0)).Ticks, [2], [-100000]);

        rows.Each(
            "timeSpanFromMinutes",
            a => TimeSpan.FromMinutes(Long(a, 0)).Ticks,
            [2L],
            [-5L],
            [1_000_000_000_000L],
            [10_000_000_000_000L]
        );

        rows.Each(
            "timeSpanFromSeconds",
            a => TimeSpan.FromSeconds(Long(a, 0)).Ticks,
            [7L],
            [922_337_203_685L],
            [922_337_203_686L]
        );

        rows.Each(
            "timeSpanFromMilliseconds",
            a => TimeSpan.FromMilliseconds(Long(a, 0)).Ticks,
            [15L],
            [-1L]
        );

        rows.Each(
            "timeSpanFromMicroseconds",
            a => TimeSpan.FromMicroseconds(Long(a, 0)).Ticks,
            [150L],
            [-1L]
        );
    }

    private static void AddDateTimeOffset(FixtureWriter rows)
    {
        rows.Each(
            "dateTimeOffset",
            a =>
                Describe(
                    new DateTimeOffset(
                        Int(a, 0),
                        Int(a, 1),
                        Int(a, 2),
                        Int(a, 3),
                        Int(a, 4),
                        Int(a, 5),
                        Offset(a, 7)
                    )
                ),
            [2024, 1, 2, 3, 4, 5, 0, 330],
            [2024, 1, 2, 3, 4, 5, 0, 0],
            [2024, 1, 2, 3, 4, 5, 0, -840],
            [2024, 1, 2, 3, 4, 5, 0, 840],
            [2024, 1, 2, 3, 4, 5, 0, 846],
            [2024, 1, 2, 3, 4, 5, 0, -841],
            [2024, 13, 2, 3, 4, 5, 0, 0],
            [2024, 2, 30, 3, 4, 5, 0, 0],
            [2024, 1, 2, 24, 4, 5, 0, 0],
            [1, 1, 1, 0, 0, 0, 0, 120],
            [1, 1, 1, 0, 0, 0, 0, 0],
            [9999, 12, 31, 23, 59, 59, 0, -120],
            [9999, 12, 31, 23, 59, 59, 0, 0]
        );

        rows.Each(
            "dateTimeOffset",
            a =>
                Describe(
                    new DateTimeOffset(
                        Int(a, 0),
                        Int(a, 1),
                        Int(a, 2),
                        Int(a, 3),
                        Int(a, 4),
                        Int(a, 5),
                        Int(a, 6),
                        Offset(a, 7)
                    )
                ),
            [2024, 1, 2, 3, 4, 5, 678, 120],
            [2024, 1, 2, 3, 4, 5, 1000, 120],
            [2024, 1, 2, 3, 4, 5, 999, -300]
        );

        rows.Each(
            "dateTimeOffsetFromTicks",
            a => Describe(new DateTimeOffset(Long(a, 0), Offset(a, 1))),
            [630822816000000000L, 120],
            [0L, 0],
            [3_155_378_975_999_999_999L, 0],
            [3_155_378_975_999_999_999L, -120],
            [-1L, 0],
            [630822816000000000L, 900]
        );
    }

    private static object[] Describe(DateTimeOffset value) =>
        [value.UtcTicks, (int)value.Offset.TotalMinutes];

    private static TimeSpan Offset(object?[] arguments, int index) =>
        TimeSpan.FromMinutes((int)arguments[index]!);

    private static int Int(object?[] arguments, int index) => (int)arguments[index]!;

    private static long Long(object?[] arguments, int index) => (long)arguments[index]!;

    private static double Double(object?[] arguments, int index) => (double)arguments[index]!;
}
