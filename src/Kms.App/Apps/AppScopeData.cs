using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Input.Counting;

namespace Kms.App.Apps;

/// <summary>
/// 把全局 StatsData 投影成单应用/类别视图。日粒度数据来自内存 Apps；
/// 区间聚合、小时、VK、热区与会话来自 Step 1 的不可变 AppProfileSnapshot。
/// 缺失维度保持空，不按比例分摊。
/// </summary>
public static class AppScopeData
{
    public static StatsData Filter(
        StatsData source,
        AppScopeSet scope,
        AppProfileSnapshot? profile)
    {
        if (scope.IsAll)
        {
            return source;
        }

        StatsData result = new() { FormatVersion = source.FormatVersion };
        foreach ((string key, string value) in source.Settings)
        {
            result.Settings[key] = value;
        }

        foreach ((string key, DayRecord day) in source.History)
        {
            DayRecord? filtered = FilterDay(day, scope);
            if (filtered is not null)
            {
                result.History[key] = filtered;
            }
        }

        if (profile is not null)
        {
            AddProfileSessions(result, profile);
        }

        return result;
    }

    public static DayRecord BuildProfileTotals(
        AppProfileSnapshot? profile,
        DayRecord fallback)
    {
        if (profile is null)
        {
            return fallback;
        }

        DayRecord totals = new()
        {
            Keys = profile.Totals.Keys,
            Clicks = profile.Totals.Clicks,
            Left = profile.Totals.Left,
            Right = profile.Totals.Right,
            Middle = profile.Totals.Middle,
            XButtons = profile.Totals.XButtons,
            Wheel = profile.Totals.Wheel,
            MovePx = profile.Totals.MovePx,
            ActiveSeconds = profile.Totals.ActiveSeconds,
            AppObservedSeconds = profile.Totals.ActiveSeconds,
            AppSwitches = profile.Totals.Switches,
            PeakApm = profile.Totals.PeakApm
        };

        for (int hour = 0; hour < profile.Hours.Count && hour < 24; hour++)
        {
            AppHourlyBucket bucket = profile.Hours[hour];
            totals.HourKeys[hour] = bucket.Keys;
            totals.HourClicks[hour] = bucket.Clicks;
            totals.ActiveHours[hour] = bucket.ActiveSeconds / 60.0;
        }

        foreach (AppKeyCount key in profile.Keys)
        {
            totals.KeyCounts[key.Vk] = totals.KeyCounts.GetValueOrDefault(key.Vk) + key.Count;
        }

        foreach (AppHeatCell cell in profile.Heat)
        {
            int cellKey = ClickHeat.CellKey(cell.CellX, cell.CellY);
            totals.ClickCells[cellKey] = totals.ClickCells.GetValueOrDefault(cellKey) + cell.Count;
            long channelKey = ((long)cell.Channel << 32) | (uint)cellKey;
            totals.ClickCellsV1[channelKey] =
                totals.ClickCellsV1.GetValueOrDefault(channelKey) + cell.Count;
        }

        foreach (AppWindowUsage window in profile.Windows)
        {
            AppUsage app = new()
            {
                ProcessPath = window.ProcessPath,
                Title = window.Title,
                Keys = window.Keys,
                Clicks = window.Clicks,
                Wheel = window.Wheel,
                ActiveSeconds = window.ActiveSeconds
            };
            totals.Apps[app.Id] = app;
        }

        totals.Sessions.AddRange(profile.Sessions);
        return totals;
    }

    private static DayRecord? FilterDay(DayRecord day, AppScopeSet scope)
    {
        DayRecord result = new()
        {
            Date = day.Date
        };

        bool any = false;
        foreach (AppUsage app in day.Apps.Values)
        {
            if (!scope.Contains(app.ProcessPath))
            {
                continue;
            }

            any = true;
            result.Keys += app.Keys;
            result.Clicks += app.Clicks;
            result.Wheel += app.Wheel;
            result.ActiveSeconds += app.ActiveSeconds;
            result.AppObservedSeconds += app.ActiveSeconds;
            result.Apps[app.Id] = app;
        }

        if (day.Sessions.Count > 0)
        {
            foreach (ActiveSession session in day.Sessions)
            {
                if (!scope.Contains(session.ProcessPath))
                {
                    continue;
                }

                any = true;
                result.Sessions.Add(session);
            }
        }

        if (!any)
        {
            return null;
        }

        return result;
    }

    private static void AddProfileSessions(StatsData data, AppProfileSnapshot profile)
    {
        if (profile.Sessions.Count == 0)
        {
            return;
        }

        foreach (ActiveSession session in profile.Sessions)
        {
            string key = session.Start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            DayRecord day = data.History.TryGetValue(key, out DayRecord? existing)
                ? existing
                : data.GetOrCreate(session.Start.Date);
            bool duplicate = day.Sessions.Any(candidate =>
                candidate.Start == session.Start
                && string.Equals(candidate.ProcessPath, session.ProcessPath, StringComparison.OrdinalIgnoreCase));
            if (!duplicate)
            {
                day.Sessions.Add(session);
            }
        }
    }
}
