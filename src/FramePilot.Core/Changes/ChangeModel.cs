using FramePilot.Core.Models;

namespace FramePilot.Core.Changes;

/// <summary>
/// Lifecycle of one change to one setting.
/// Planned → Prepared → Applied → Verified, or Failed; rollback goes RollbackPending → RolledBack / RollbackFailed.
/// </summary>
public enum ChangeState
{
    Planned,
    Prepared,
    Applied,
    Verified,
    Failed,
    RollbackPending,
    RolledBack,
    RollbackFailed
}

public enum SettingValueKind
{
    None,
    String,
    DWord,
    QWord
}

/// <summary>Snapshot of a setting: whether it existed, its type and its data (DWORD/QWORD as decimal text).</summary>
public sealed record SettingValue(bool Exists, SettingValueKind Kind, string? Data)
{
    public static readonly SettingValue Absent = new(false, SettingValueKind.None, null);
    public static SettingValue DWord(uint v) => new(true, SettingValueKind.DWord, v.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static SettingValue Str(string v) => new(true, SettingValueKind.String, v);

    public bool SameAs(SettingValue other)
    {
        if (!Exists && !other.Exists) return true;
        if (Exists != other.Exists) return false;
        if (Kind != other.Kind) return false;
        return string.Equals(Data ?? "", other.Data ?? "", StringComparison.Ordinal);
    }

    public string Display => !Exists ? "(not set)" : Kind switch
    {
        SettingValueKind.String => $"\"{Data}\"",
        _ => $"{Data} ({Kind})"
    };
}

/// <summary>Persistent journal row. It is written before the system is touched.</summary>
public sealed class ChangeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }
    public string ActionId { get; set; } = "";
    public int ActionVersion { get; set; }
    public string? RuleId { get; set; }
    public string TargetKey { get; set; } = "";
    public string TargetDescription { get; set; } = "";
    public SettingValue Original { get; set; } = SettingValue.Absent;
    public SettingValue Planned { get; set; } = SettingValue.Absent;
    public ChangeState State { get; set; } = ChangeState.Planned;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string? Error { get; set; }
    public string? Note { get; set; }

    /// <summary>True while FramePilot considers this setting under its management (rollback is possible).</summary>
    public bool IsActive => State is ChangeState.Prepared or ChangeState.Applied or ChangeState.Verified
        or ChangeState.RollbackPending or ChangeState.RollbackFailed;
}

public interface IChangeJournal
{
    void Insert(ChangeRecord record);
    void Update(ChangeRecord record);
    IReadOnlyList<ChangeRecord> GetAll();
    ChangeRecord? Get(Guid id);
}

/// <summary>A single setting location that a typed action may read and write.</summary>
public interface ISettingTarget
{
    /// <summary>Stable key stored in the journal so the target can be resolved again after restart.</summary>
    string Key { get; }
    string Description { get; }
    SettingValue Read();
    void Write(SettingValue value);
}

/// <summary>Resolves a journal key back to a target. Implementations must only accept allowlisted locations.</summary>
public interface ISettingTargetResolver
{
    ISettingTarget? Resolve(string key);
}

public sealed record PlannedChange(
    ISettingTarget Target,
    Func<SettingValue, SettingValue> ComputeDesired,
    LocalizedText Description);

/// <summary>Typed allowlisted action. Implements readCurrentState / buildPlan; the engine does apply / verify / rollback.</summary>
public interface IChangeAction
{
    string Id { get; }
    int Version { get; }
    LocalizedText Title { get; }
    bool RequiresElevation { get; }
    bool RequiresRestart { get; }
    IReadOnlyList<PlannedChange> BuildPlan(IReadOnlyDictionary<string, string> parameters);
}
