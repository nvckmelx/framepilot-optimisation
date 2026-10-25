using FramePilot.Core.Models;

namespace FramePilot.Diagnostics;

public enum ComparisonVerdict
{
    InsufficientRuns,
    IncomparableConditions,
    Improved,
    Regressed,
    Mixed,
    NoSignificantChange
}

public sealed record MetricComparison(
    string Metric,
    bool HigherIsBetter,
    int BaselineRuns,
    int AfterRuns,
    double BaselineMedian,
    double AfterMedian,
    double BaselineMin,
    double BaselineMax,
    double AfterMin,
    double AfterMax,
    double DeltaPercent,
    ComparisonVerdict Verdict);

public sealed record ComparisonResult(
    ComparisonVerdict Overall,
    IReadOnlyList<MetricComparison> Metrics,
    IReadOnlyList<LocalizedText> Warnings,
    LocalizedText Summary);

/// <summary>
/// Compares groups of comparable runs. A metric counts as changed only when the ranges of the two groups
/// do not overlap (every "after" run is better than every baseline run, or vice versa) and each group has
/// at least <see cref="MinRunsPerGroup"/> runs. Otherwise the change cannot be separated from run-to-run noise.
/// </summary>
public static class SessionComparer
{
    public const int MinRunsPerGroup = 3;

    public static ComparisonResult Compare(IReadOnlyList<SessionRecord> baseline, IReadOnlyList<SessionRecord> after)
    {
        var warnings = new List<LocalizedText>();

        if (baseline.Count == 0 || after.Count == 0)
        {
            return new ComparisonResult(ComparisonVerdict.InsufficientRuns, Array.Empty<MetricComparison>(), warnings,
                new LocalizedText("Нужны прогоны «до» и «после».", "Both baseline and after runs are required."));
        }

        var all = baseline.Concat(after).ToList();
        if (all.Select(s => s.Game).Distinct().Count() > 1)
            warnings.Add(new("Прогоны относятся к разным играм.", "Runs belong to different games."));
        if (all.Any(s => s.IsDemo) && all.Any(s => !s.IsDemo))
            warnings.Add(new("Демо-данные смешаны с реальными измерениями.", "Demo data is mixed with real measurements."));

        void CheckSame(Func<TestConditions, object?> selector, LocalizedText text)
        {
            var values = all.Select(s => selector(s.Conditions)).Where(v => v is not null).Distinct().Count();
            if (values > 1) warnings.Add(text);
        }
        CheckSame(c => c.GameVersion, new("Версия игры различается между прогонами.", "Game version differs between runs."));
        CheckSame(c => c.GpuDriverVersion, new("Версия драйвера GPU различается между прогонами.", "GPU driver version differs between runs."));
        CheckSame(c => c.DisplayWidth is null ? null : $"{c.DisplayWidth}x{c.DisplayHeight}", new("Разрешение экрана различается.", "Display resolution differs."));
        CheckSame(c => c.RefreshHz, new("Частота обновления монитора различается.", "Refresh rate differs."));
        if (all.Select(s => Math.Round(s.RequestedSeconds)).Distinct().Count() > 1)
            warnings.Add(new("Длительность прогонов различается.", "Run durations differ."));

        var metrics = new List<MetricComparison>
        {
            CompareMetric("AverageFps", true, baseline, after, s => s.Stats.AverageFps),
            CompareMetric("P99FrameTimeMs", false, baseline, after, s => s.Stats.P99FrameTimeMs),
            CompareMetric("OnePercentLowFps", true, baseline, after, s => s.Stats.OnePercentLowFps),
            CompareMetric("SpikePercent", false, baseline, after, s => 100.0 * s.Stats.SpikeCount / Math.Max(1, s.Stats.FrameCount))
        };
        if (baseline.All(s => s.Stats.MedianPcLatencyMs.IsAvailable) && after.All(s => s.Stats.MedianPcLatencyMs.IsAvailable))
            metrics.Add(CompareMetric("MedianPcLatencyMs", false, baseline, after, s => s.Stats.MedianPcLatencyMs.Value));

        ComparisonVerdict overall;
        LocalizedText summary;
        if (baseline.Count < MinRunsPerGroup || after.Count < MinRunsPerGroup)
        {
            overall = ComparisonVerdict.InsufficientRuns;
            summary = new($"Для вывода нужно минимум {MinRunsPerGroup} сопоставимых прогона в каждой группе. Сейчас: {baseline.Count} и {after.Count}.",
                $"At least {MinRunsPerGroup} comparable runs per group are needed. Currently: {baseline.Count} and {after.Count}.");
        }
        else if (warnings.Count > 0)
        {
            overall = ComparisonVerdict.IncomparableConditions;
            summary = new("Условия тестов различаются, поэтому разницу нельзя приписать изменениям настроек.",
                "Test conditions differ, so the difference cannot be attributed to the setting changes.");
        }
        else
        {
            var improved = metrics.Count(m => m.Verdict == ComparisonVerdict.Improved);
            var regressed = metrics.Count(m => m.Verdict == ComparisonVerdict.Regressed);
            (overall, summary) = (improved, regressed) switch
            {
                (> 0, 0) => (ComparisonVerdict.Improved, new LocalizedText(
                    "Улучшение подтверждено: диапазоны результатов «до» и «после» не пересекаются хотя бы по одной метрике, ухудшений нет.",
                    "Improvement confirmed: before/after ranges do not overlap for at least one metric and nothing regressed.")),
                (0, > 0) => (ComparisonVerdict.Regressed, new LocalizedText(
                    "Результат ухудшился. Рекомендуем откатить изменения из этого пакета.",
                    "Results got worse. Consider rolling back this batch of changes.")),
                (> 0, > 0) => (ComparisonVerdict.Mixed, new LocalizedText(
                    "Результат смешанный: часть метрик улучшилась, часть ухудшилась.",
                    "Mixed result: some metrics improved and some regressed.")),
                _ => (ComparisonVerdict.NoSignificantChange, new LocalizedText(
                    "В этих тестах улучшение не удалось отделить от обычного разброса результатов.",
                    "In these tests the improvement could not be separated from normal run-to-run variation."))
            };
        }

        return new ComparisonResult(overall, metrics, warnings, summary);
    }

    private static MetricComparison CompareMetric(string name, bool higherIsBetter,
        IReadOnlyList<SessionRecord> baseline, IReadOnlyList<SessionRecord> after, Func<SessionRecord, double> selector)
    {
        var b = baseline.Select(selector).OrderBy(x => x).ToList();
        var a = after.Select(selector).OrderBy(x => x).ToList();
        var bMed = MetricCalculator.Median(b);
        var aMed = MetricCalculator.Median(a);
        var delta = bMed == 0 ? 0 : 100.0 * (aMed - bMed) / bMed;

        ComparisonVerdict verdict;
        if (b.Count < MinRunsPerGroup || a.Count < MinRunsPerGroup) verdict = ComparisonVerdict.InsufficientRuns;
        else if (higherIsBetter ? a[0] > b[^1] : a[^1] < b[0]) verdict = ComparisonVerdict.Improved;
        else if (higherIsBetter ? a[^1] < b[0] : a[0] > b[^1]) verdict = ComparisonVerdict.Regressed;
        else verdict = ComparisonVerdict.NoSignificantChange;

        return new MetricComparison(name, higherIsBetter, b.Count, a.Count, bMed, aMed, b[0], b[^1], a[0], a[^1], delta, verdict);
    }
}
