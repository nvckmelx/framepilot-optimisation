using FramePilot.Core.Models;

namespace FramePilot.Core.Changes;

public sealed record PlanPreviewItem(
    string ActionId,
    string TargetKey,
    string TargetDescription,
    LocalizedText Description,
    SettingValue Current,
    SettingValue Desired,
    bool AlreadySatisfied,
    bool AlreadyManaged);

public enum ItemOutcome
{
    Verified,
    AlreadySatisfied,
    AlreadyManaged,
    Failed,
    Compensated,
    CompensationFailed
}

public sealed record ApplyItemResult(string TargetKey, string TargetDescription, ItemOutcome Outcome, Guid? RecordId, string? Message);

public sealed record ApplyResult(Guid BatchId, IReadOnlyList<ApplyItemResult> Items)
{
    public bool AllSucceeded => Items.All(i => i.Outcome is ItemOutcome.Verified or ItemOutcome.AlreadySatisfied or ItemOutcome.AlreadyManaged);
}

public enum RollbackOutcome
{
    RolledBack,
    AlreadyRolledBack,
    NotApplicable,
    Conflict,
    Failed,
    TargetNotResolvable
}

public sealed record RollbackResult(Guid RecordId, RollbackOutcome Outcome, SettingValue? CurrentValue, string? Message);

public sealed record RecoveryReport(int Examined, int MarkedApplied, int MarkedFailed, int CompletedRollbacks, IReadOnlyList<string> Notes);

public sealed record ActionRequest(IChangeAction Action, IReadOnlyDictionary<string, string> Parameters, string? RuleId);

/// <summary>
/// Applies typed actions with a journal written before every system change, verifies by read-back,
/// performs compensating rollback on partial failure and never silently overwrites a value the user changed later.
/// This is not an atomic OS transaction; every item reports its own honest status.
/// </summary>
public sealed class ChangeEngine
{
    private readonly IChangeJournal _journal;
    private readonly ISettingTargetResolver _resolver;
    private readonly object _gate = new();

    public ChangeEngine(IChangeJournal journal, ISettingTargetResolver resolver)
    {
        _journal = journal;
        _resolver = resolver;
    }

    public IReadOnlyList<PlanPreviewItem> Preview(IEnumerable<ActionRequest> requests)
    {
        var active = ActiveTargets();
        var result = new List<PlanPreviewItem>();
        foreach (var req in requests)
        {
            foreach (var change in req.Action.BuildPlan(req.Parameters))
            {
                var current = change.Target.Read();
                var desired = change.ComputeDesired(current);
                result.Add(new PlanPreviewItem(
                    req.Action.Id, change.Target.Key, change.Target.Description, change.Description,
                    current, desired, current.SameAs(desired), active.Contains(change.Target.Key)));
            }
        }
        return result;
    }

    public ApplyResult Apply(IEnumerable<ActionRequest> requests)
    {
        lock (_gate)
        {
            var batchId = Guid.NewGuid();
            var results = new List<ApplyItemResult>();
            var appliedInBatch = new List<ChangeRecord>();
            var active = ActiveTargets();
            var failed = false;

            foreach (var req in requests)
            {
                foreach (var change in req.Action.BuildPlan(req.Parameters))
                {
                    if (failed) break;
                    var target = change.Target;

                    if (active.Contains(target.Key))
                    {
                        results.Add(new(target.Key, target.Description, ItemOutcome.AlreadyManaged, null,
                            "This setting already has an active FramePilot change; roll it back first to re-apply."));
                        continue;
                    }

                    SettingValue current;
                    try { current = target.Read(); }
                    catch (Exception ex)
                    {
                        results.Add(new(target.Key, target.Description, ItemOutcome.Failed, null, "Could not read current value: " + ex.Message));
                        failed = true;
                        break;
                    }

                    var desired = change.ComputeDesired(current);
                    if (current.SameAs(desired))
                    {
                        results.Add(new(target.Key, target.Description, ItemOutcome.AlreadySatisfied, null, "Already set; nothing changed."));
                        continue;
                    }

                    var record = new ChangeRecord
                    {
                        BatchId = batchId,
                        ActionId = req.Action.Id,
                        ActionVersion = req.Action.Version,
                        RuleId = req.RuleId,
                        TargetKey = target.Key,
                        TargetDescription = target.Description,
                        Planned = desired,
                        State = ChangeState.Planned
                    };
                    _journal.Insert(record);

                    record.Original = current;
                    Transition(record, ChangeState.Prepared);

                    try
                    {
                        target.Write(desired);
                        Transition(record, ChangeState.Applied);
                    }
                    catch (Exception ex)
                    {
                        record.Error = "Write failed: " + ex.Message;
                        HandleFailedWrite(record, target);
                        results.Add(new(target.Key, target.Description, ItemOutcome.Failed, record.Id, record.Error));
                        failed = true;
                        break;
                    }

                    SettingValue readBack;
                    try { readBack = target.Read(); }
                    catch (Exception ex) { readBack = SettingValue.Absent; record.Error = "Read-back failed: " + ex.Message; }

                    if (readBack.SameAs(desired))
                    {
                        Transition(record, ChangeState.Verified);
                        appliedInBatch.Add(record);
                        active.Add(target.Key);
                        results.Add(new(target.Key, target.Description, ItemOutcome.Verified, record.Id, null));
                    }
                    else
                    {
                        record.Error ??= $"Verification mismatch: expected {desired.Display}, read {readBack.Display}.";
                        Transition(record, ChangeState.Failed);
                        // The value is in an unexpected state: try to put the original back.
                        var comp = RestoreOriginal(record, target);
                        results.Add(new(target.Key, target.Description, ItemOutcome.Failed, record.Id, record.Error + " " + comp));
                        failed = true;
                    }
                }
                if (failed) break;
            }

            if (failed && appliedInBatch.Count > 0)
            {
                // Compensating rollback of everything this batch already applied.
                foreach (var rec in Enumerable.Reverse(appliedInBatch))
                {
                    var rb = RollbackCore(rec, force: false);
                    var idx = results.FindIndex(r => r.RecordId == rec.Id);
                    var outcome = rb.Outcome == RollbackOutcome.RolledBack ? ItemOutcome.Compensated : ItemOutcome.CompensationFailed;
                    var item = new ApplyItemResult(rec.TargetKey, rec.TargetDescription, outcome, rec.Id,
                        rb.Outcome == RollbackOutcome.RolledBack ? "Reverted because another item in the batch failed." : rb.Message);
                    if (idx >= 0) results[idx] = item; else results.Add(item);
                }
            }

            return new ApplyResult(batchId, results);
        }
    }

    /// <summary>Rolls back one change. Without <paramref name="force"/>, a value changed after FramePilot applied it is reported as a conflict and left untouched.</summary>
    public RollbackResult Rollback(Guid recordId, bool force = false)
    {
        lock (_gate)
        {
            var record = _journal.Get(recordId);
            if (record is null) return new(recordId, RollbackOutcome.NotApplicable, null, "Unknown change.");
            return RollbackCore(record, force);
        }
    }

    public IReadOnlyList<RollbackResult> RollbackBatch(Guid batchId, bool force = false)
    {
        lock (_gate)
        {
            return _journal.GetAll()
                .Where(r => r.BatchId == batchId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => RollbackCore(r, force))
                .ToList();
        }
    }

    public IReadOnlyList<RollbackResult> RollbackAll(bool force = false)
    {
        lock (_gate)
        {
            return _journal.GetAll()
                .Where(r => r.IsActive)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => RollbackCore(r, force))
                .ToList();
        }
    }

    /// <summary>
    /// Reconciles records left in intermediate states by a crash, power loss or reboot.
    /// It only inspects and relabels; it never writes to the system on its own.
    /// </summary>
    public RecoveryReport Recover()
    {
        lock (_gate)
        {
            int examined = 0, markedApplied = 0, markedFailed = 0, completed = 0;
            var notes = new List<string>();
            foreach (var record in _journal.GetAll())
            {
                if (record.State is not (ChangeState.Planned or ChangeState.Prepared or ChangeState.Applied or ChangeState.RollbackPending))
                    continue;
                examined++;
                var target = _resolver.Resolve(record.TargetKey);
                if (target is null)
                {
                    notes.Add($"{record.TargetKey}: target not resolvable; left as {record.State}.");
                    continue;
                }
                SettingValue current;
                try { current = target.Read(); }
                catch (Exception ex) { notes.Add($"{record.TargetKey}: read failed ({ex.Message})."); continue; }

                switch (record.State)
                {
                    case ChangeState.Planned:
                        // Original value was never captured: nothing was written.
                        record.Error = "Interrupted before the original value was saved; no change was made.";
                        Transition(record, ChangeState.Failed);
                        markedFailed++;
                        break;
                    case ChangeState.Prepared:
                    case ChangeState.Applied:
                        if (current.SameAs(record.Planned))
                        {
                            record.Note = "Recovered after interruption: value matches the planned value.";
                            Transition(record, ChangeState.Verified);
                            markedApplied++;
                        }
                        else if (current.SameAs(record.Original))
                        {
                            record.Error = "Interrupted before the change took effect; system still has the original value.";
                            Transition(record, ChangeState.Failed);
                            markedFailed++;
                        }
                        else
                        {
                            notes.Add($"{record.TargetKey}: value differs from both original and planned; manual review needed.");
                        }
                        break;
                    case ChangeState.RollbackPending:
                        if (current.SameAs(record.Original))
                        {
                            record.Note = "Rollback confirmed after interruption.";
                            Transition(record, ChangeState.RolledBack);
                            completed++;
                        }
                        else
                        {
                            notes.Add($"{record.TargetKey}: rollback was interrupted; retry it from Recovery.");
                        }
                        break;
                }
            }
            return new RecoveryReport(examined, markedApplied, markedFailed, completed, notes);
        }
    }

    /// <summary>Detects settings that the user changed after FramePilot applied them.</summary>
    public IReadOnlyList<(ChangeRecord Record, SettingValue Current)> DetectDrift()
    {
        var list = new List<(ChangeRecord, SettingValue)>();
        foreach (var record in _journal.GetAll().Where(r => r.State is ChangeState.Applied or ChangeState.Verified))
        {
            var target = _resolver.Resolve(record.TargetKey);
            if (target is null) continue;
            try
            {
                var current = target.Read();
                if (!current.SameAs(record.Planned)) list.Add((record, current));
            }
            catch
            {
                // unreadable targets are reported elsewhere
            }
        }
        return list;
    }

    private RollbackResult RollbackCore(ChangeRecord record, bool force)
    {
        if (record.State == ChangeState.RolledBack)
            return new(record.Id, RollbackOutcome.AlreadyRolledBack, null, "Already rolled back.");
        if (record.State is ChangeState.Planned or ChangeState.Failed)
            return new(record.Id, RollbackOutcome.NotApplicable, null, "This change was not applied.");

        var target = _resolver.Resolve(record.TargetKey);
        if (target is null)
            return new(record.Id, RollbackOutcome.TargetNotResolvable, null, "The setting location is not on the allowlist.");

        SettingValue current;
        try { current = target.Read(); }
        catch (Exception ex) { return new(record.Id, RollbackOutcome.Failed, null, "Could not read current value: " + ex.Message); }

        if (current.SameAs(record.Original))
        {
            record.Note = "Value already equals the original.";
            Transition(record, ChangeState.RolledBack);
            return new(record.Id, RollbackOutcome.RolledBack, current, record.Note);
        }

        if (!current.SameAs(record.Planned) && !force)
        {
            return new(record.Id, RollbackOutcome.Conflict, current,
                $"The value was changed after FramePilot applied it (now {current.Display}). Confirm to overwrite it with the original {record.Original.Display}.");
        }

        var message = RestoreOriginal(record, target);
        return new(record.Id, record.State == ChangeState.RolledBack ? RollbackOutcome.RolledBack : RollbackOutcome.Failed, current, message);
    }

    private string RestoreOriginal(ChangeRecord record, ISettingTarget target)
    {
        Transition(record, ChangeState.RollbackPending);
        try
        {
            target.Write(record.Original);
            var after = target.Read();
            if (after.SameAs(record.Original))
            {
                Transition(record, ChangeState.RolledBack);
                return "Original value restored.";
            }
            record.Error = $"Rollback verification mismatch: read {after.Display}.";
            Transition(record, ChangeState.RollbackFailed);
            return record.Error;
        }
        catch (Exception ex)
        {
            record.Error = "Rollback failed: " + ex.Message;
            Transition(record, ChangeState.RollbackFailed);
            return record.Error;
        }
    }

    private void HandleFailedWrite(ChangeRecord record, ISettingTarget target)
    {
        try
        {
            var now = target.Read();
            if (now.SameAs(record.Original))
            {
                Transition(record, ChangeState.Failed);
                return;
            }
        }
        catch
        {
            // fall through to compensation
        }
        Transition(record, ChangeState.Failed);
        RestoreOriginal(record, target);
    }

    private HashSet<string> ActiveTargets() =>
        _journal.GetAll().Where(r => r.IsActive).Select(r => r.TargetKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void Transition(ChangeRecord record, ChangeState state)
    {
        record.State = state;
        record.UpdatedAt = DateTimeOffset.Now;
        _journal.Update(record);
    }
}
