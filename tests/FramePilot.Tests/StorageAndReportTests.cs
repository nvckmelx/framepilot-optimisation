using System.Security.Cryptography;
using FramePilot.Core;
using FramePilot.Core.Changes;
using FramePilot.Core.Models;
using FramePilot.Diagnostics;
using FramePilot.Licensing;
using FramePilot.Reports;
using FramePilot.Rules;
using FramePilot.Storage;
using static FramePilot.Tests.Fixtures;

namespace FramePilot.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fp-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FramePilotStore _store;

    public StorageTests()
    {
        _store = new FramePilotStore(Path.Combine(_dir, "t.db"));
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* temp cleanup */ }
    }

    [Fact]
    public void Snapshot_RoundTrips_IncludingUnavailableValues()
    {
        var s = Fixtures.Snapshot();
        _store.SaveSnapshot(s);
        var back = _store.LatestSnapshot(demo: false)!;
        Assert.Equal(s.Id, back.Id);
        Assert.Equal("Test CPU", back.Cpu.Name.Value);
        Assert.False(back.Settings.WindowedGameOptimizations.IsAvailable);
        Assert.Equal("not available in test", back.Settings.WindowedGameOptimizations.UnavailableReason);
        Assert.Null(_store.LatestSnapshot(demo: true));
    }

    [Fact]
    public void Sessions_AreSeparatedFromDemo()
    {
        _store.SaveSession(Session(GameId.Cs2, RunGroup.Baseline, Frames(5, 6, 7)));
        _store.SaveSession(Session(GameId.Cs2, RunGroup.Baseline, Frames(5, 6, 7), demo: true));
        Assert.Single(_store.Sessions(demo: false));
        Assert.Single(_store.Sessions(demo: true));
    }

    [Fact]
    public void ChangeJournal_PersistsAllFields()
    {
        var r = new ChangeRecord
        {
            BatchId = Guid.NewGuid(), ActionId = "a", ActionVersion = 2, RuleId = null, TargetKey = "HKCU\\X|Y|DWord", TargetDescription = "d",
            Original = SettingValue.Absent, Planned = SettingValue.DWord(1), State = ChangeState.Planned
        };
        _store.Insert(r);
        r.State = ChangeState.Verified;
        r.Error = "e";
        _store.Update(r);
        var back = _store.Get(r.Id)!;
        Assert.Equal(ChangeState.Verified, back.State);
        Assert.False(back.Original.Exists);
        Assert.Equal("1", back.Planned.Data);
        Assert.Equal(SettingValueKind.DWord, back.Planned.Kind);
        Assert.Null(back.RuleId);
        Assert.Equal("e", back.Error);
    }

    [Fact]
    public void ChangeEngine_WorksOnSqliteJournal()
    {
        var mem = new MemoryStore();
        var factory = new MemoryFactory(mem);
        var engine = new ChangeEngine(_store, factory);
        var actions = Optimization.ActionRegistry.Create(factory);
        var res = engine.Apply(new[] { new ActionRequest(actions[ActionIds.EnableGameMode], new Dictionary<string, string>(), null) });
        Assert.True(res.AllSucceeded);
        Assert.Equal(RollbackOutcome.RolledBack, engine.Rollback(res.Items[0].RecordId!.Value).Outcome);
    }

    [Fact]
    public void DeleteLocalData_KeepsJournalByDefault()
    {
        _store.SaveSnapshot(Fixtures.Snapshot());
        _store.Insert(new ChangeRecord { BatchId = Guid.NewGuid(), ActionId = "a", TargetKey = "k", TargetDescription = "d" });
        _store.DeleteLocalData(includeChangeJournal: false);
        Assert.Null(_store.LatestSnapshot(false));
        Assert.Single(_store.GetAll());
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        _store.SetSetting("k", "v");
        Assert.Equal("v", _store.GetSetting("k"));
        _store.SetSetting("k", null);
        Assert.Null(_store.GetSetting("k"));
    }
}

public class LicenseTests
{
    [Fact]
    public void SignedLicense_VerifiesForSameMachine_Only()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo());
        var machine = LicenseVerifier.MachineCodeFrom("machine-guid-1");
        var payload = new LicensePayload("L-1", "a@b.c", LicenseTier.Pro, machine, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12));
        var key = LicenseVerifier.Sign(payload, ec);

        var ok = LicenseVerifier.Verify(key, pub, machine, DateTimeOffset.UtcNow);
        Assert.Equal(LicenseStatus.Valid, ok.Status);
        Assert.True(ok.IsPro);

        Assert.Equal(LicenseStatus.WrongMachine, LicenseVerifier.Verify(key, pub, LicenseVerifier.MachineCodeFrom("other"), DateTimeOffset.UtcNow).Status);
        Assert.Equal(LicenseStatus.Expired, LicenseVerifier.Verify(key, pub, machine, DateTimeOffset.UtcNow.AddMonths(13)).Status);
    }

    [Fact]
    public void TamperedOrForeignKeys_AreRejected()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo());
        var machine = LicenseVerifier.MachineCodeFrom("m");
        var payload = new LicensePayload("L-1", "a@b.c", LicenseTier.Pro, machine, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));

        var foreign = LicenseVerifier.Sign(payload, other);
        Assert.Equal(LicenseStatus.InvalidSignature, LicenseVerifier.Verify(foreign, pub, machine, DateTimeOffset.UtcNow).Status);

        var key = LicenseVerifier.Sign(payload, ec);
        var parts = key.Split('.');
        var tamperedPayload = LicenseVerifier.B64UrlEncode(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Encoding.UTF8.GetString(LicenseVerifier.B64UrlDecode(parts[1])).Replace("\"Pro\"", "\"Founders\"")));
        Assert.Equal(LicenseStatus.InvalidSignature, LicenseVerifier.Verify($"{parts[0]}.{tamperedPayload}.{parts[2]}", pub, machine, DateTimeOffset.UtcNow).Status);

        Assert.Equal(LicenseStatus.Malformed, LicenseVerifier.Verify("garbage", pub, machine, DateTimeOffset.UtcNow).Status);
        Assert.Equal(LicenseStatus.Free, LicenseVerifier.Verify(null, pub, machine, DateTimeOffset.UtcNow).Status);
    }

    [Fact]
    public void RollbackIsNeverGatedByLicense()
    {
        Assert.True(Entitlements.CanRollback(LicenseState.FreeTier));
        Assert.False(Entitlements.CanAutoApply(LicenseState.FreeTier));
    }

    [Fact]
    public void MachineCode_IsStableAndFormatted()
    {
        var a = LicenseVerifier.MachineCodeFrom("ABC");
        Assert.Equal(a, LicenseVerifier.MachineCodeFrom(" abc "));
        Assert.Matches("^[0-9A-F]{4}(-[0-9A-F]{4}){4}$", a);
    }
}

public class ReportTests
{
    [Fact]
    public void Report_ContainsDefinitionsAndLimitations_AndRoundTripsAsJson()
    {
        var snap = Fixtures.Snapshot(gameModeOn: false);
        var session = Session(GameId.Cs2, RunGroup.Baseline, Frames(5, 6, 7, 30));
        var recs = RuleEngine.Evaluate(RuleCatalog.All, new RuleContext(snap, GameId.Cs2, session));
        var report = ReportBuilder.Build("1.0.0", "en", snap, new[] { session }, recs, null, Array.Empty<ChangeRecord>());
        var json = ReportBuilder.ToJson(report);
        Assert.Contains("onePercentLowFps", json);
        Assert.Contains("not 1000/P99", json);
        var back = Json.Deserialize<DiagnosticReport>(json)!;
        Assert.Equal(report.Recommendations.Count, back.Recommendations.Count);
        Assert.False(back.IsDemo);
    }

    [Fact]
    public void FrameSeries_KeepsSpikes_AndHistogramCountsAllFrames()
    {
        var times = Enumerable.Range(0, 6000).Select(i => i == 3001 ? 80.0 : 5.0 + (i % 4) * 0.25).ToArray();
        var session = Session(GameId.Cs2, RunGroup.Baseline, Frames(times));
        var report = ReportBuilder.Build("1", "en", null, new[] { session }, Array.Empty<Recommendation>(), null, Array.Empty<ChangeRecord>(),
            session, Frames(times));
        var fs = report.LatestFrames!;
        Assert.Equal(6000, fs.SourceFrameCount);
        Assert.True(fs.MaxFrameTimeMs.Count <= FrameSeries.MaxPoints);
        Assert.Contains(80.0, fs.MaxFrameTimeMs);
        Assert.Equal(6000, fs.Histogram.Sum(b => b.Count));
        Assert.True(double.IsPositiveInfinity(fs.Histogram[^1].ToMs));
        var json = ReportBuilder.ToJson(report);
        Assert.Contains("latestFrames", json);
        Assert.NotNull(Json.Deserialize<DiagnosticReport>(json)!.LatestFrames);
    }

    [Fact]
    public void Html_MarksDemoData_AndEscapesContent()
    {
        var snap = Fixtures.Snapshot() with { IsDemo = true };
        var report = ReportBuilder.Build("1.0.0", "ru", snap, Array.Empty<SessionRecord>(), Array.Empty<Recommendation>(), null, Array.Empty<ChangeRecord>());
        var html = ReportBuilder.ToHtml(report, null);
        Assert.Contains("ДЕМО-ДАННЫЕ", html);
        Assert.Contains("Недоступно", html); // unavailable values are labelled, not hidden

        var evil = Fixtures.Snapshot() with { Cpu = Fixtures.Snapshot().Cpu with { Name = M("<script>alert(1)</script>") } };
        var html2 = ReportBuilder.ToHtml(ReportBuilder.Build("1", "en", evil, Array.Empty<SessionRecord>(), Array.Empty<Recommendation>(), null, Array.Empty<ChangeRecord>()), null);
        Assert.DoesNotContain("<script>alert", html2);
    }

    [Fact]
    public void Html_IncludesComparisonVerdict()
    {
        SessionRecord Run(RunGroup g, double ft) => Session(GameId.Cs2, g, Frames(Enumerable.Repeat(ft, 100).ToArray()));
        var b = new[] { 10.0, 10.5, 9.5 }.Select(x => Run(RunGroup.Baseline, x)).ToList();
        var a = new[] { 10.2, 9.8, 10.1 }.Select(x => Run(RunGroup.After, x)).ToList();
        var cmp = SessionComparer.Compare(b, a);
        var html = ReportBuilder.ToHtml(ReportBuilder.Build("1", "ru", null, b.Concat(a).ToList(), Array.Empty<Recommendation>(), cmp, Array.Empty<ChangeRecord>()), null);
        Assert.Contains("разброса", html);
    }
}
