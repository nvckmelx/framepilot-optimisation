using FramePilot.Core.Changes;
using FramePilot.Optimization;

namespace FramePilot.Tests;

public class ChangeEngineTests
{
    private const string GameModeKey = @"HKCU\Software\Microsoft\GameBar|AutoGameModeEnabled|DWord";
    private const string DxKey = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences|DirectXUserGlobalSettings|String";

    private readonly MemoryStore _store = new();
    private readonly MemoryJournal _journal = new();
    private readonly ChangeEngine _engine;
    private readonly IReadOnlyDictionary<string, IChangeAction> _actions;

    public ChangeEngineTests()
    {
        var factory = new MemoryFactory(_store);
        _engine = new ChangeEngine(_journal, factory);
        _actions = ActionRegistry.Create(factory);
    }

    private ActionRequest Req(string id, Dictionary<string, string>? p = null) => new(_actions[id], p ?? new Dictionary<string, string>(), "rule");

    [Fact]
    public void Apply_RecordsOriginal_WritesAndVerifies()
    {
        _store.Values[GameModeKey] = SettingValue.DWord(0);
        var r = _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });

        Assert.True(r.AllSucceeded);
        var rec = Assert.Single(_journal.GetAll());
        Assert.Equal(ChangeState.Verified, rec.State);
        Assert.Equal("0", rec.Original.Data);
        Assert.Equal("1", _store.Values[GameModeKey].Data);
    }

    [Fact]
    public void Apply_WhenAlreadySatisfied_DoesNotWriteOrJournal()
    {
        _store.Values[GameModeKey] = SettingValue.DWord(1);
        var r = _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });
        Assert.Equal(ItemOutcome.AlreadySatisfied, r.Items.Single().Outcome);
        Assert.Empty(_journal.GetAll());
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public void Apply_Twice_IsIdempotent()
    {
        _store.Values[GameModeKey] = SettingValue.DWord(0);
        _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });
        var second = _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });
        Assert.Equal(ItemOutcome.AlreadyManaged, second.Items.Single().Outcome);
        Assert.Single(_journal.GetAll());
    }

    [Fact]
    public void Rollback_RestoresOriginal_AndDeletesValueThatDidNotExist()
    {
        var r = _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });
        Assert.True(_store.Values.ContainsKey(GameModeKey));
        var rb = _engine.Rollback(r.Items.Single().RecordId!.Value);
        Assert.Equal(RollbackOutcome.RolledBack, rb.Outcome);
        Assert.False(_store.Values.ContainsKey(GameModeKey));
        Assert.Equal(ChangeState.RolledBack, _journal.GetAll().Single().State);
    }

    [Fact]
    public void Rollback_Twice_IsNoOp()
    {
        var r = _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });
        var id = r.Items.Single().RecordId!.Value;
        _engine.Rollback(id);
        var writes = _store.Writes;
        Assert.Equal(RollbackOutcome.AlreadyRolledBack, _engine.Rollback(id).Outcome);
        Assert.Equal(writes, _store.Writes);
    }

    [Fact]
    public void Rollback_AfterUserChangedValue_IsConflict_AndDoesNotOverwrite()
    {
        _store.Values[GameModeKey] = SettingValue.DWord(0);
        var r = _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });
        _store.Values[GameModeKey] = SettingValue.DWord(7); // user edited it later

        var rb = _engine.Rollback(r.Items.Single().RecordId!.Value);
        Assert.Equal(RollbackOutcome.Conflict, rb.Outcome);
        Assert.Equal("7", _store.Values[GameModeKey].Data);
        Assert.Equal("7", rb.CurrentValue!.Data);

        var forced = _engine.Rollback(r.Items.Single().RecordId!.Value, force: true);
        Assert.Equal(RollbackOutcome.RolledBack, forced.Outcome);
        Assert.Equal("0", _store.Values[GameModeKey].Data);
    }

    [Fact]
    public void DetectDrift_FindsUserChanges()
    {
        _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });
        Assert.Empty(_engine.DetectDrift());
        _store.Values[GameModeKey] = SettingValue.DWord(0);
        Assert.Single(_engine.DetectDrift());
    }

    [Fact]
    public void PartialFailure_CompensatesAlreadyAppliedItems()
    {
        _store.Values[GameModeKey] = SettingValue.DWord(0);
        _store.FailWritesFor.Add(DxKey);
        var r = _engine.Apply(new[] { Req(ActionIds.EnableGameMode), Req(ActionIds.EnableWindowedOptimizations) });

        Assert.False(r.AllSucceeded);
        Assert.Contains(r.Items, i => i.Outcome == ItemOutcome.Compensated);
        Assert.Contains(r.Items, i => i.Outcome == ItemOutcome.Failed);
        Assert.Equal("0", _store.Values[GameModeKey].Data); // compensated back to original
        Assert.DoesNotContain(_journal.GetAll(), x => x.IsActive);
    }

    [Fact]
    public void VerificationMismatch_IsFailure_NotSuccess()
    {
        _store.SilentlyIgnoreWritesFor.Add(GameModeKey);
        _store.Values[GameModeKey] = SettingValue.DWord(0);
        var r = _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });
        Assert.Equal(ItemOutcome.Failed, r.Items.Single().Outcome);
        Assert.False(r.AllSucceeded);
    }

    [Fact]
    public void JournalIsWrittenBeforeSystemChange()
    {
        // A target whose write fails still leaves a journal row with the original value captured.
        _store.Values[GameModeKey] = SettingValue.DWord(0);
        _store.FailWritesFor.Add(GameModeKey);
        _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });
        var rec = Assert.Single(_journal.GetAll());
        Assert.Equal("0", rec.Original.Data);
        Assert.Equal(ChangeState.Failed, rec.State);
    }

    [Fact]
    public void Recover_PreparedButValueMatchesPlanned_BecomesVerified()
    {
        _store.Values[GameModeKey] = SettingValue.DWord(0);
        var r = _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });
        var id = r.Items.Single().RecordId!.Value;
        _journal.ForceState(id, ChangeState.Prepared); // crash after write, before status update

        var report = _engine.Recover();
        Assert.Equal(1, report.MarkedApplied);
        Assert.Equal(ChangeState.Verified, _journal.Get(id)!.State);
    }

    [Fact]
    public void Recover_PreparedButOriginalStillPresent_BecomesFailed()
    {
        _store.Values[GameModeKey] = SettingValue.DWord(0);
        var r = _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });
        var id = r.Items.Single().RecordId!.Value;
        _store.Values[GameModeKey] = SettingValue.DWord(0); // simulate: write never reached the system
        _journal.ForceState(id, ChangeState.Prepared);

        var report = _engine.Recover();
        Assert.Equal(1, report.MarkedFailed);
        Assert.Equal(ChangeState.Failed, _journal.Get(id)!.State);
    }

    [Fact]
    public void Recover_RollbackPendingWithOriginalRestored_BecomesRolledBack()
    {
        _store.Values[GameModeKey] = SettingValue.DWord(0);
        var r = _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });
        var id = r.Items.Single().RecordId!.Value;
        _store.Values[GameModeKey] = SettingValue.DWord(0);
        _journal.ForceState(id, ChangeState.RollbackPending);

        Assert.Equal(1, _engine.Recover().CompletedRollbacks);
        Assert.Equal(ChangeState.RolledBack, _journal.Get(id)!.State);
    }

    [Fact]
    public void Recover_NeverWritesToSystem()
    {
        _store.Values[GameModeKey] = SettingValue.DWord(0);
        var r = _engine.Apply(new[] { Req(ActionIds.EnableGameMode) });
        _journal.ForceState(r.Items.Single().RecordId!.Value, ChangeState.RollbackPending);
        var writes = _store.Writes;
        _engine.Recover();
        Assert.Equal(writes, _store.Writes);
    }

    [Fact]
    public void WindowedOptimization_PreservesOtherTokens()
    {
        _store.Values[DxKey] = SettingValue.Str("VRROptimizeEnable=0;AutoHDREnable=1;");
        _engine.Apply(new[] { Req(ActionIds.EnableWindowedOptimizations) });
        var v = _store.Values[DxKey].Data!;
        Assert.Contains("VRROptimizeEnable=0;", v);
        Assert.Contains("AutoHDREnable=1;", v);
        Assert.Contains("SwapEffectUpgradeEnable=1;", v);
    }

    [Fact]
    public void GpuPreference_RejectsNonGameExecutables()
    {
        var p = new Dictionary<string, string> { [ActionIds.ParamExecutablePath] = @"C:\Windows\System32\cmd.exe" };
        Assert.Throws<ArgumentException>(() => _engine.Preview(new[] { Req(ActionIds.SetHighPerformanceGpu, p) }));
    }

    [Fact]
    public void Preview_DoesNotWrite()
    {
        var preview = _engine.Preview(new[] { Req(ActionIds.EnableGameMode) });
        Assert.Single(preview);
        Assert.Equal(0, _store.Writes);
        Assert.Empty(_journal.GetAll());
    }
}

public class DxPreferenceStringTests
{
    [Theory]
    [InlineData(null, "SwapEffectUpgradeEnable=1;")]
    [InlineData("", "SwapEffectUpgradeEnable=1;")]
    [InlineData("SwapEffectUpgradeEnable=0;", "SwapEffectUpgradeEnable=1;")]
    [InlineData("AutoHDREnable=1;", "AutoHDREnable=1;SwapEffectUpgradeEnable=1;")]
    [InlineData("AutoHDREnable=1", "AutoHDREnable=1;SwapEffectUpgradeEnable=1;")]
    public void Set_AddsOrReplacesToken(string? input, string expected)
    {
        Assert.Equal(expected, DxPreferenceString.Set(input, "SwapEffectUpgradeEnable", "1"));
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        Assert.Equal("2", DxPreferenceString.Get("gpupreference=2;", "GpuPreference"));
    }

    [Fact]
    public void RegistryAllowlist_RejectsOtherLocations()
    {
        Assert.False(RegistryAllowlist.IsAllowed(@"Software\Microsoft\Windows\CurrentVersion\Run"));
        Assert.True(RegistryAllowlist.IsAllowed(@"Software\Microsoft\GameBar"));
        Assert.Null(new RegistryTargetResolver().Resolve(@"HKLM\SYSTEM\CurrentControlSet|Start|DWord"));
        Assert.Null(new RegistryTargetResolver().Resolve(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run|evil|String"));
    }
}
