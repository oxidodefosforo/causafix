using System;
using System.Collections.Generic;
using System.Linq;
using CausaFix.Core.Compare;
using CausaFix.Core.Model;
using CausaFix.Core.Reports;
using CausaFix.Core.Values;

namespace CausaFix.Core.Tests
{
    /// <summary>
    /// Real owner of a parameter (scope correction of older workbooks, wrong destination) and the stage-by-stage write
    /// verification. Revit is simulated: these tests prove the rules, not the behaviour of a real model.
    /// </summary>
    public static class ExcelLinkOwnerTests
    {
        private static readonly Guid Tag = new Guid("0b9f7c1e-1111-4a2b-9c3d-000000000001");
        private static readonly Guid Circuit = new Guid("0b9f7c1e-2222-4a2b-9c3d-000000000002");

        /// <summary>Explicit states per (element, key).</summary>
        private sealed class Model : ICurrentStateSource
        {
            public readonly Dictionary<string, CurrentState> States = new Dictionary<string, CurrentState>(StringComparer.Ordinal);
            public int Reads;
            public void Put(string uid, ParamKey key, CurrentState s) => States[uid + "|" + key] = s;
            public CurrentState Get(string uid, ParamKey key) { Reads++; return States.TryGetValue(uid + "|" + key, out var s) ? s : CurrentState.Missing(); }
        }

        private static CurrentState Owned(string value, bool editable = true, bool isType = false, string owner = "")
            => new CurrentState { ElementExists = true, ParameterExists = true, Value = value == null ? RawValue.Empty : RawValue.FromText(value), Editable = editable,
                                  OwnerUniqueId = owner, OwnerIsType = isType, IsShared = true, StorageType = "String", TargetLabel = owner };

        /// <summary>What Revit reports for a shared TYPE parameter requested on an instance: found through it, owned by the type.</summary>
        private static CurrentState ThroughInstance(string instanceLabel, string typeUid, string value)
            => new CurrentState
            {
                ElementExists = true, ParameterExists = true, Value = RawValue.FromText(value), Editable = false, OwnerMismatch = true,
                OwnerIsType = true, OwnerUniqueId = typeUid, OwnerLabel = "Fixture : " + typeUid, TargetLabel = instanceLabel, LockCode = BlockCode.WrongScope,
                LockReason = "Wrong destination: the workbook targets '" + instanceLabel + "', but this parameter belongs to its type"
            };

        private static ProposedCell Cell(string row, string typeUid, ColumnDef col, string original, string proposed, int excelRow)
            => new ProposedCell
            {
                SheetKey = "sch:1", SheetTitle = "Electrical Fixture Schedule", ExcelRow = excelRow, RowKey = row, RowLabel = row, TargetUniqueId = row,
                TargetIsType = col.Scope == ParamScope.Type, Column = col, Original = RawValue.FromText(original),
                Proposal = proposed == null ? Proposal.None() : Proposal.Of(RawValue.FromText(proposed), proposed)
            };

        private static ColumnDef Col(Guid g, ParamScope scope, string header) => new ColumnDef { Key = ParamKey.Shared(g, scope), Header = header, Kind = ValueKind.Text };

        [Test]
        public static void OldWorkbook_TypeParameterExportedAsInstance_IsComparedAndWrittenOnTheType()
        {
            var col = Col(Tag, ParamScope.Instance, "DR_Tag-1");
            var m = new Model();
            m.Put("i1", col.Key, ThroughInstance("i1", "T1", "old"));
            m.Put("i2", col.Key, ThroughInstance("i2", "T1", "old"));
            m.Put("T1", col.Key.WithScope(ParamScope.Type), Owned("old", owner: "T1", isType: true));
            // Only one of the two rows of the type is edited; the other keeps the exported value (not a contradiction).
            var set = ChangeSetBuilder.Build(new[] { Cell("i1", "T1", col, "old", "a", 5), Cell("i2", "T1", col, "old", null, 6) }, m);
            Assert.Equal(1, set.Entries.Count);
            var e = set.Entries[0];
            Assert.Equal(ChangeStatus.Change, e.Status, e.Reason);
            Assert.True(e.TargetIsType && e.TargetUniqueId == "T1", "real owner: the type");
            Assert.Equal(ParamScope.Type, e.Key.Scope);
            Assert.Contains("Scope corrected", e.Reason);
            Assert.Contains("Scope corrected", e.Diagnostic);
            Assert.True(set.Issues.Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("Re-export")), "the user is told to re-export");
            var ops = ChangeSetBuilder.ToWriteOps(set);
            Assert.Equal(1, ops.Count);
            Assert.Equal("T1", ops[0].TargetUniqueId);
            Assert.Equal(ParamScope.Type, ops[0].Key.Scope);
        }

        [Test]
        public static void OldWorkbook_TwoRowsOfTheSameType_DifferentValues_ConflictAndNoWrite()
        {
            var col = Col(Tag, ParamScope.Instance, "DR_Tag-1");
            var m = new Model();
            m.Put("i1", col.Key, ThroughInstance("i1", "T1", "old"));
            m.Put("i2", col.Key, ThroughInstance("i2", "T1", "old"));
            m.Put("T1", col.Key.WithScope(ParamScope.Type), Owned("old", owner: "T1", isType: true));
            var set = ChangeSetBuilder.Build(new[] { Cell("i1", "T1", col, "old", "a", 5), Cell("i2", "T1", col, "old", "b", 6) }, m);
            Assert.Equal(1, set.Entries.Count);
            Assert.Equal(ChangeStatus.ProposalConflict, set.Entries[0].Status);
            Assert.Equal(0, ChangeSetBuilder.ToWriteOps(set).Count, "no value is chosen");
        }

        [Test]
        public static void WrongScope_WithoutCorrection_IsBlockedWithTheOwner()
        {
            var col = Col(Tag, ParamScope.Instance, "DR_Tag-1");
            var m = new Model();
            m.Put("i1", col.Key, ThroughInstance("i1", "T1", "old"));
            var set = ChangeSetBuilder.Build(new[] { Cell("i1", "T1", col, "old", "a", 5) }, m, new ChangeSet { RetargetWrongScope = false });
            var e = set.Entries.Single();
            Assert.Equal(ChangeStatus.Blocked, e.Status);
            Assert.Equal(BlockCode.WrongScope, e.Code);
            Assert.Contains("belongs to its type", e.Reason);
            Assert.Contains("NOT the requested element", e.Diagnostic);
            Assert.Equal(0, ChangeSetBuilder.ToWriteOps(set).Count);
        }

        [Test]
        public static void InstanceOwnedSharedText_IsReadyOnTheInstance_AndReadOnlyStaysBlockedWithEvidence()
        {
            var circuit = Col(Circuit, ParamScope.Instance, "EL_Circuit");
            var tag = Col(Tag, ParamScope.Type, "DR_Tag-1");
            var m = new Model();
            var st = Owned("", owner: "i1");
            st.UserModifiable = false;                    // UI flag only: never a lock
            st.ParameterGuid = Circuit.ToString("D");
            st.Binding = "InstanceBinding";
            m.Put("i1", circuit.Key, st);
            m.Put("T1", tag.Key, new CurrentState
            {
                ElementExists = true, ParameterExists = true, Value = RawValue.FromText("old"), Editable = false, IsReadOnly = true, OwnerUniqueId = "T1", OwnerIsType = true,
                LockCode = BlockCode.ReadOnlyParameter, LockReason = "Revit reports IsReadOnly = true for this parameter on type 'T1'. Checked: no global parameter association; the project API does not expose family formulas",
                Evidence = "no global parameter association"
            });
            var cells = new[]
            {
                Cell("i1", "T1", circuit, "", "b2", 5),
                new ProposedCell { SheetKey = "sch:1", SheetTitle = "S", ExcelRow = 5, RowKey = "i1", RowLabel = "i1", TargetUniqueId = "T1", TargetIsType = true, Column = tag,
                                   Original = RawValue.FromText("old"), Proposal = Proposal.Of(RawValue.FromText("a"), "a") }
            };
            cells[0].Original = RawValue.Empty;
            var set = ChangeSetBuilder.Build(cells, m);
            var c = set.Entries.Single(x => x.Column.Header == "EL_Circuit");
            Assert.Equal(ChangeStatus.Change, c.Status, c.Reason);
            Assert.False(c.TargetIsType);
            Assert.Contains("UserModifiable=false", c.Diagnostic);
            Assert.Contains("GUID=" + Circuit.ToString("D"), c.Diagnostic);
            Assert.Contains("binding=InstanceBinding", c.Diagnostic);
            var t = set.Entries.Single(x => x.Column.Header == "DR_Tag-1");
            Assert.Equal(BlockCode.ReadOnlyParameter, t.Code);
            Assert.Contains("IsReadOnly = true", t.Reason);
            Assert.False(t.Reason.Contains("formula") && !t.Reason.Contains("does not expose"), "no formula claimed without evidence");
        }

        // ------------------------------------------------------------------ stage-by-stage write

        private sealed class Target : IWriteTarget
        {
            public RawValue Stored;            // what the model really holds
            public RawValue Reference;         // what the reference used for Set returns
            public bool SetResult = true;
            public bool StaleReference;        // the reference keeps the old value until a fresh lookup
            public bool Discard;               // Set "succeeds" but nothing is stored
            public Exception Throw;
            public int Regenerations;
            public RawValue Read() => Reference;
            public bool Set(bool clear, RawValue v)
            {
                if (Throw != null) throw Throw;
                if (!SetResult) return false;
                if (!Discard) Stored = clear ? RawValue.Empty : v;
                if (!StaleReference) Reference = Stored;
                return true;
            }
            public RawValue RegenerateAndReadFresh() { Regenerations++; Reference = Stored; return Stored; }
        }

        [Test]
        public static void Write_TextOnEmptyAndOnPreviousValue_IsVerifiedAtEachStage()
        {
            foreach (var before in new[] { RawValue.Empty, RawValue.FromText("x1") })
            {
                var t = new Target { Stored = before, Reference = before };
                var trace = new WriteTrace();
                var f = WriteVerifier.Run(t, false, RawValue.FromText("b2"), trace, out string msg);
                Assert.Equal(WriteFailure.None, f, msg);
                Assert.Equal(before.Encode(), trace.BeforeSet);
                Assert.Equal(true, trace.SetReturned);
                Assert.Equal(RawValue.FromText("b2").Encode(), trace.AfterSet);
                Assert.Equal(0, t.Regenerations, "no regeneration when the value is already there");
            }
        }

        [Test]
        public static void Write_SetTrueButNothingStored_IsNotPersisted_WithExpectedAndReadValues()
        {
            var t = new Target { Stored = RawValue.Empty, Reference = RawValue.Empty, Discard = true };
            var trace = new WriteTrace { Owner = "Fixture : A", Guid = Circuit.ToString("D") };
            var f = WriteVerifier.Run(t, false, RawValue.FromText("b2"), trace, out string msg);
            Assert.Equal(WriteFailure.NotPersisted, f);
            Assert.Contains("expected 'b2'; read (empty) after Set and (empty) after regenerating", msg);
            Assert.Contains(Circuit.ToString("D"), msg);
            Assert.False(msg.Contains("out of range"), "no speculative explanation for text");
            Assert.Equal("after regenerate", trace.Stage);
            Assert.Equal(1, t.Regenerations);
            Assert.Contains("after regenerate=", trace.ToText());
        }

        [Test]
        public static void Write_StaleReference_FreshReadAfterRegenerateConfirms()
        {
            var t = new Target { Stored = RawValue.FromText("x1"), Reference = RawValue.FromText("x1"), StaleReference = true };
            var trace = new WriteTrace();
            var f = WriteVerifier.Run(t, false, RawValue.FromText("b2"), trace, out _);
            Assert.Equal(WriteFailure.None, f);
            Assert.True(trace.Notes.Any(n => n.Contains("fresh read")));
        }

        [Test]
        public static void Write_SetFalseOrException_AreSetFailures_WithFullText()
        {
            var t = new Target { Stored = RawValue.Empty, Reference = RawValue.Empty, SetResult = false };
            var f = WriteVerifier.Run(t, false, RawValue.FromText("b2"), new WriteTrace(), out string msg);
            Assert.Equal(WriteFailure.SetFailed, f);
            Assert.Contains("returned false", msg);

            var trace = new WriteTrace();
            t = new Target { Stored = RawValue.Empty, Reference = RawValue.Empty, Throw = new WriteRejectedException(WriteFailure.SetFailed, "Set failed: Revit ArgumentException: bad", "Autodesk.Revit.Exceptions.ArgumentException: bad value") };
            f = WriteVerifier.Run(t, false, RawValue.FromText("b2"), trace, out msg);
            Assert.Equal(WriteFailure.SetFailed, f);
            Assert.Contains("Autodesk.Revit.Exceptions.ArgumentException", trace.Exception);
        }

        [Test]
        public static void Report_KeepsEvidenceThroughRollback_AndNeverClaimsApplied()
        {
            var r = new ApplyResult { Mode = ApplyMode.Atomic, RevitVersion = "2026", AddinBuild = "0.2.0 · commit abc1234", TransactionStatus = "Committed", GroupStatus = "RolledBack" };
            var ok = new OpResult { Label = "i1", Parameter = "DR_Tag-1", Outcome = OpOutcome.Applied };
            ok.Trace.AfterSet = RawValue.FromText("a").Encode();
            var lost = new OpResult { Label = "i2", Parameter = "EL_Circuit", Outcome = OpOutcome.Failed, Failure = WriteFailure.NotPersisted, Message = "Write not persisted: expected 'b2'; read (empty) after the transaction committed" };
            lost.Trace.Stage = "after commit";
            lost.Trace.AfterCommit = RawValue.Empty.Encode();
            r.Ops.Add(ok); r.Ops.Add(lost);
            r.MarkRolledBack("Rolled back: 1 value(s) were not in the model after commit (atomic mode).");
            Assert.Equal(0, r.Applied, "zero false Applied");
            Assert.Equal(OpOutcome.RolledBack, ok.Outcome);
            Assert.Equal(WriteFailure.RolledBack, ok.Failure);
            Assert.Contains("after Set=", ok.Diagnostic, "evidence kept after rollback");
            string csv = ApplyReportWriter.Build("Model", null, r, new IdentityUnits());
            Assert.Contains("Diagnostic (stages)", csv);
            Assert.Contains("NotPersisted", csv);
            Assert.Contains("stage=after commit", csv);
            Assert.Contains("0.2.0 · commit abc1234", csv);
            Assert.Contains("NotPersisted", lost.Detail);
        }

        private sealed class IdentityUnits : IUnitConverter
        {
            public double FromInternal(double v, string unit) => v;
            public double ToInternal(double v, string unit) => v;
        }
    }
}
