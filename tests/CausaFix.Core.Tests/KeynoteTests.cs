using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CausaFix.Keynotes.Editing;
using CausaFix.Keynotes.Execution;
using CausaFix.Keynotes.Files;
using CausaFix.Keynotes.Format;
using CausaFix.Keynotes.Model;
using CausaFix.Keynotes.ViewModels;

namespace CausaFix.Core.Tests
{
    /// <summary>Keynote file format, encodings, model validation and editing operations.</summary>
    public static class KeynoteFormatTests
    {
        private const string Sample = "E\tELECTRICAL\r\nE-LTG\tLIGHTING\tE\r\nE-LTG-001\tPROVIDE LIGHT FITTING AS SHOWN.\tE-LTG\r\n001\tLEADING ZERO\tE\r\n";

        [Test]
        public static void Utf16RoundTripKeepsKeysAsText()
        {
            var bytes = KeynoteFormat.Encode(Sample, KeynoteEncoding.Utf16LeBom, out var used);
            Assert.Equal(KeynoteEncoding.Utf16LeBom, used);
            Assert.True(bytes[0] == 0xFF && bytes[1] == 0xFE, "BOM FF FE");
            var doc = KeynoteFormat.Read(bytes);
            Assert.Equal(KeynoteEncoding.Utf16LeBom, doc.Encoding);
            Assert.Equal(4, doc.Library.Records.Count);
            Assert.Equal(0, doc.Issues.Count, string.Join(";", doc.Issues));
            Assert.True(doc.Library.Contains("001") && !doc.Library.Contains("1"), "leading zero kept");
            Assert.Equal("E-LTG", doc.Library.Find("E-LTG-001").ParentKey);
            Assert.True(doc.Library.Find("E").IsRoot);
            string again = KeynoteFormat.Compose(doc.Library, doc.Preamble, doc.Unparsed, doc.HadCrLf);
            Assert.Equal(Sample, again, "byte-identical text after round trip");
        }

        [Test]
        public static void EncodingsAreDetectedAndUnicodeIsKept()
        {
            string text = "M\tMATERIALES Ø 20 mm ±5 °C m² µ ñ á\r\nM-01\tTUBERÍA Ø25 – “COBRE”\tM\r\n";
            foreach (var enc in new[] { KeynoteEncoding.Utf16LeBom, KeynoteEncoding.Utf16BeBom, KeynoteEncoding.Utf8Bom, KeynoteEncoding.Utf8 })
            {
                var doc = KeynoteFormat.Read(KeynoteFormat.Encode(text, enc, out _));
                Assert.Equal(enc, doc.Encoding, "detected " + enc);
                Assert.Equal("MATERIALES Ø 20 mm ±5 °C m² µ ñ á", doc.Library.Find("M").Text, enc.ToString());
                Assert.Equal("TUBERÍA Ø25 – “COBRE”", doc.Library.Find("M-01").Text, enc.ToString());
            }
            // ANSI (Windows-1252): Ø ± ° ² µ ñ – “ ” are all representable.
            var ansi = KeynoteFormat.Encode(text, KeynoteEncoding.Ansi1252, out var used);
            Assert.Equal(KeynoteEncoding.Ansi1252, used);
            Assert.Equal(text.Length, ansi.Length, "one byte per char");
            var d = KeynoteFormat.Read(ansi);
            Assert.Equal(KeynoteEncoding.Ansi1252, d.Encoding);
            Assert.Equal("TUBERÍA Ø25 – “COBRE”", d.Library.Find("M-01").Text);
            // A character ANSI cannot hold → UTF-16 instead of '?'.
            KeynoteFormat.Encode("K\tΩ ≤ 3\r\n", KeynoteEncoding.Ansi1252, out var u2);
            Assert.Equal(KeynoteEncoding.Utf16LeBom, u2);
            // UTF-16 LE without BOM is recognised.
            var noBom = new UnicodeEncoding(false, false).GetBytes("A\tAAA\r\n");
            Assert.Equal(KeynoteEncoding.Utf16LeBom, KeynoteFormat.Read(noBom).Encoding);
        }

        [Test]
        public static void MalformedLinesAreReportedAndKept()
        {
            string t = "# comment kept\r\nA\tGROUP A\r\nBROKEN LINE WITHOUT TAB\r\nA-1\tX\tA\textra\r\n\r\nA-2\tOK\tA\r\n";
            var doc = KeynoteFormat.Read(KeynoteFormat.Encode(t, KeynoteEncoding.Utf16LeBom, out _));
            Assert.Equal(2, doc.Library.Records.Count);
            Assert.Equal(2, doc.Unparsed.Count);
            Assert.Equal(3, doc.Unparsed[0].Line);
            Assert.True(doc.Issues.Count(i => i.Kind == IssueKind.Malformed) == 2);
            Assert.Equal("# comment kept", doc.Preamble.Single());
            string back = KeynoteFormat.Compose(doc.Library, doc.Preamble, doc.Unparsed);
            Assert.Contains("BROKEN LINE WITHOUT TAB", back);
            Assert.Contains("# comment kept", back);
        }

        [Test]
        public static void PyRevitDeffrelDbIsRecognisedAndOnlyKeynoteTablesRead()
        {
            string t = "# @datastore(\"deffrel\")\r\n# ===================== @db(keynotesdb:\"Keynotes\")\r\n# --------------------- @table(categories:\"Root Keynotes Table\") @sep(\\t)\r\nE\tELECTRICAL\r\n# --------------------- @table(keynotes:\"Keynotes Table\")\r\nE-1\tFIRST\tE\r\nE-2\tSECOND\tNULL\r\n# --------------------- @table(locks_table:\"locks\")\r\nuser\t2026\tsomething\r\n";
            var doc = KeynoteFormat.Read(KeynoteFormat.Encode(t, KeynoteEncoding.Utf16LeBom, out _));
            Assert.True(doc.IsDeffrelDb);
            Assert.Equal(3, doc.Library.Records.Count);
            Assert.Equal("E", doc.Library.Find("E-1").ParentKey);
            Assert.True(doc.Library.Find("E-2").IsRoot, "NULL parent = root");
            Assert.False(doc.Library.Contains("user"), "lock rows are not keynotes");
        }

        [Test]
        public static void ValidationFindsDuplicatesMissingParentsSelfAndCycles()
        {
            var lib = new KeynoteLibrary(new[]
            {
                new KeynoteRecord { Key = "A", Text = "a" }, new KeynoteRecord { Key = "A", Text = "dup" },
                new KeynoteRecord { Key = "B", Text = "b", ParentKey = "NOPE" }, new KeynoteRecord { Key = "C", Text = "c", ParentKey = "C" },
                new KeynoteRecord { Key = "X", Text = "x", ParentKey = "Y" }, new KeynoteRecord { Key = "Y", Text = "y", ParentKey = "X" },
                new KeynoteRecord { Key = "T", Text = "tab\tinside" }
            });
            var kinds = lib.Validate().Select(i => i.Kind).ToList();
            Assert.True(kinds.Contains(IssueKind.DuplicateKey), "dup");
            Assert.True(kinds.Contains(IssueKind.MissingParent), "missing");
            Assert.True(kinds.Contains(IssueKind.SelfParent), "self");
            Assert.Equal(1, kinds.Count(k => k == IssueKind.Cycle), "one cycle");
            Assert.True(kinds.Contains(IssueKind.InvalidCharacters), "tab");
        }

        [Test]
        public static void PastedMultiLineTextBecomesOneLine()
        {
            Assert.Equal("PROVIDE LED LIGHT FITTING AS SHOWN.", KeynoteText.Normalize("  PROVIDE LED\r\n  LIGHT FITTING\tAS SHOWN.  \r\n"));
            Assert.Equal("A B C D", KeynoteText.Normalize("A\u2028B\u000BC\nD"));
            Assert.Equal("Ø 20 mm ±5 °C", KeynoteText.Normalize("Ø 20 mm\n±5 °C"));
            Assert.True(KeynoteText.NeedsNormalization("A\nB"));
            Assert.False(KeynoteText.NeedsNormalization("A B"));
            Assert.Equal("Provide led. Fitting", KeynoteText.ToSentence("PROVIDE LED. FITTING"));
        }

        [Test]
        public static void NaturalOrder()
        {
            var keys = new[] { "E-LTG-010", "E-LTG-2", "E-LTG-001", "E10", "E2", "001", "1", "B" }.OrderBy(k => k, NaturalComparer.Instance).ToList();
            Assert.Equal("1|001|B|E-LTG-001|E-LTG-2|E-LTG-010|E2|E10", string.Join("|", keys));
        }
    }

    public static class KeynoteEditingTests
    {
        private static KeynoteDraft Draft() => new KeynoteDraft(new KeynoteLibrary(new[]
        {
            new KeynoteRecord { Key = "E", Text = "ELECTRICAL" },
            new KeynoteRecord { Key = "E-LTG", Text = "LIGHTING", ParentKey = "E" },
            new KeynoteRecord { Key = "E-LTG-001", Text = "ONE", ParentKey = "E-LTG" },
            new KeynoteRecord { Key = "E-LTG-002", Text = "TWO", ParentKey = "E-LTG" },
            new KeynoteRecord { Key = "E-PWR", Text = "POWER", ParentKey = "E" }
        }));

        [Test]
        public static void AddEditValidateAndNothingChangesOnError()
        {
            var d = Draft();
            Assert.Contains("already exists", d.Add("E-LTG-001", "X", "E-LTG"));
            Assert.Contains("does not exist", d.Add("Z", "X", "NOPE"));
            Assert.Contains("empty", d.Add("Z", " \r\n ", ""));
            Assert.Contains("a tab", d.Add("Z\t1", "X", ""));
            Assert.False(d.IsDirty, "errors change nothing");
            Assert.Equal(null, d.Add("E-LTG-003", "THREE\r\nLINES", "E-LTG"));
            Assert.Equal("THREE LINES", d.Current.Find("E-LTG-003").Text);
            Assert.Equal(null, d.Edit("E-LTG-001", "ONE EDITED", "E-LTG"));
            Assert.Contains("cycle", d.Edit("E", "ELECTRICAL", "E-LTG-001"));
            var ch = d.Changes();
            Assert.Equal(2, ch.Count);
            Assert.True(ch.Any(c => c.Kind == ChangeKind.Added && c.Key == "E-LTG-003"));
            Assert.True(ch.Any(c => c.Kind == ChangeKind.Modified && c.Key == "E-LTG-001"));
        }

        [Test]
        public static void IndentOutdentAndMoveNeverChangeKeys()
        {
            var d = Draft();
            Assert.Equal(null, d.Indent("E-PWR"));                 // previous sibling of E-PWR is E-LTG
            Assert.Equal("E-LTG", d.Current.Find("E-PWR").ParentKey);
            Assert.Equal(null, d.Outdent("E-PWR"));
            Assert.Equal("E", d.Current.Find("E-PWR").ParentKey);
            Assert.Equal(null, d.Outdent("E-PWR"));
            Assert.True(d.Current.Find("E-PWR").IsRoot);
            Assert.Contains("top level", d.Outdent("E-PWR"));
            Assert.Contains("There is no previous keynote", d.Indent("E-LTG-001"));
            Assert.True(d.Current.Contains("E-PWR"));
        }

        [Test]
        public static void DeleteBranchNeedsExplicitBranch()
        {
            var d = Draft();
            Assert.Contains("inside", d.Delete("E-LTG", false));
            Assert.Equal(5, d.Current.Records.Count);
            Assert.Equal(null, d.Delete("E-LTG", true));
            Assert.Equal(2, d.Current.Records.Count);
            Assert.Equal(3, d.Changes().Count(c => c.Kind == ChangeKind.Removed));
        }

        [Test]
        public static void RekeyMovesChildrenAndIsRecorded()
        {
            var d = Draft();
            Assert.Equal(null, d.Rekey("E-LTG", "E-LT"));
            Assert.Equal("E-LT", d.Current.Find("E-LTG-001").ParentKey);
            Assert.Equal("E-LTG->E-LT", string.Join(",", d.Renames.Select(r => r.From + "->" + r.To)));
            Assert.Equal(null, d.Rekey("E-LT", "E-LIGHT"));
            Assert.Equal("E-LTG->E-LIGHT", string.Join(",", d.Renames.Select(r => r.From + "->" + r.To)), "composed");
            Assert.Equal(null, d.Rekey("E-LIGHT", "E-LTG"));
            Assert.Equal(0, d.Renames.Count, "renamed back");
            Assert.False(d.IsDirty);
            Assert.Contains("already exists", d.Rekey("E-LTG-001", "E-LTG-002"));
            // A new record renamed is not a model rename.
            d.Add("N-1", "NEW", "E");
            d.Rekey("N-1", "N-2");
            Assert.Equal(0, d.Renames.Count);
        }

        [Test]
        public static void SuggestedKeysKeepPadding()
        {
            var d = Draft();
            Assert.Equal("E-LTG-003", d.SuggestKey("E-LTG-001"));
            Assert.Equal("E-PWR-COPY", d.SuggestKey("E-PWR"));
            d.Add("009", "x", "");
            Assert.Equal("010", d.SuggestKey("009"));
        }

        [Test]
        public static void ImportNeverOverwritesSilently()
        {
            var d = Draft();
            var incoming = new KeynoteLibrary(new[]
            {
                new KeynoteRecord { Key = "E-LTG-001", Text = "ONE" , ParentKey = "E-LTG" },          // identical
                new KeynoteRecord { Key = "E-LTG-002", Text = "TWO CHANGED", ParentKey = "E-LTG" },   // conflict
                new KeynoteRecord { Key = "E-LTG-005", Text = "FIVE", ParentKey = "E-LTG" },          // new
            });
            var plan = KeynoteImport.PlanImport(d, incoming);
            Assert.Equal(IncomingStatus.Identical, plan[0].Status);
            Assert.Equal(IncomingStatus.Conflict, plan[1].Status);
            Assert.False(plan[1].Take, "conflict not taken by default");
            Assert.True(plan[2].Take);
            Assert.Equal(null, KeynoteImport.ApplyImport(d, plan));
            Assert.Equal("TWO", d.Current.Find("E-LTG-002").Text, "kept mine");
            Assert.True(d.Current.Contains("E-LTG-005"));
            plan[1].Take = true;
            KeynoteImport.ApplyImport(d, KeynoteImport.PlanImport(d, incoming).Select(i => { if (i.Status == IncomingStatus.Conflict) i.Take = true; return i; }).ToList());
            Assert.Equal("TWO CHANGED", d.Current.Find("E-LTG-002").Text, "explicit choice");
            // Importing a child without its parent is refused as a whole.
            var orphan = new KeynoteLibrary(new[] { new KeynoteRecord { Key = "Z-1", Text = "z", ParentKey = "Z" } });
            Assert.Contains("invalid", KeynoteImport.ApplyImport(d, KeynoteImport.PlanImport(d, orphan)));
            Assert.False(d.Current.Contains("Z-1"));
        }

        [Test]
        public static void PasteBranchGetsFreeKeysAndKeepsStructure()
        {
            var d = Draft();
            var branch = d.Current.Branch("E-LTG");
            var plan = KeynoteImport.PlanPaste(d, branch, "E-PWR");
            Assert.Equal("E-LTG-COPY", plan[0].TargetKey);
            Assert.Equal("E-PWR", plan[0].TargetParent);
            Assert.Equal("E-LTG-003", plan[1].TargetKey);
            Assert.Equal("E-LTG-004", plan[2].TargetKey);
            Assert.Equal(null, KeynoteImport.ApplyPaste(d, plan));
            Assert.Equal("E-LTG-COPY", d.Current.Find("E-LTG-003").ParentKey, "child follows the pasted parent");
            Assert.Equal("E-PWR", d.Current.Find("E-LTG-COPY").ParentKey);
            Assert.Equal(0, d.Current.Validate().Count);
        }

        [Test]
        public static void ThreeWayMergeKeepsBothSidesAndAsksOnConflict()
        {
            var b = Draft().Base;
            var mine = b.Clone(); mine.Find("E-LTG-001").Text = "MINE"; mine.Records.Add(new KeynoteRecord { Key = "E-LTG-003", Text = "NEW MINE", ParentKey = "E-LTG" });
            var theirs = b.Clone(); theirs.Find("E-LTG-002").Text = "THEIRS"; theirs.Records.Add(new KeynoteRecord { Key = "E-PWR-001", Text = "NEW THEIRS", ParentKey = "E-PWR" });
            var m = KeynoteMerge.Merge(b, mine, theirs);
            Assert.Equal(0, m.Conflicts.Count);
            Assert.Equal("MINE", m.Merged.Find("E-LTG-001").Text);
            Assert.Equal("THEIRS", m.Merged.Find("E-LTG-002").Text);
            Assert.True(m.Merged.Contains("E-LTG-003") && m.Merged.Contains("E-PWR-001"));
            Assert.Equal(2, m.TheirChanges.Count);

            theirs.Find("E-LTG-001").Text = "THEIRS TOO";
            m = KeynoteMerge.Merge(b, mine, theirs);
            Assert.Equal(1, m.Conflicts.Count);
            Assert.Equal(null, KeynoteMerge.Resolve(m, out string e1));
            Assert.Contains("unresolved", e1);
            m.Conflicts[0].Choice = ConflictChoice.Theirs;
            var lib = KeynoteMerge.Resolve(m, out string e2);
            Assert.Equal(null, e2);
            Assert.Equal("THEIRS TOO", lib.Find("E-LTG-001").Text);
        }
    }

    public static class KeynoteFileStoreTests
    {
        [Test]
        public static void SaveDetectsExternalChangesAndBacksUp()
        {
            using (var s = new KeynoteSandbox())
            {
                string p = s.Write("kn.txt", "A\tGROUP\r\n");
                var snap = s.Store.Read(p);
                Assert.True(snap.Access.CanWrite, snap.Access.ReadOnlyReason);
                var mine = KeynoteFormat.Encode("A\tGROUP EDITED\r\n", KeynoteEncoding.Utf16LeBom, out _);
                // Someone else writes first.
                s.Write("kn.txt", "A\tGROUP\r\nB\tOTHER\r\n");
                var r = s.Store.Save(p, mine, snap.Hash);
                Assert.Equal(SaveOutcome.Conflict, r.Outcome);
                Assert.True(r.Theirs.Document.Library.Contains("B"));
                Assert.True(KeynoteFormat.Read(File.ReadAllBytes(p)).Library.Contains("B"), "file untouched");
                // With the right token the save goes through, with a backup and a verified hash.
                r = s.Store.Save(p, mine, r.Theirs.Hash);
                Assert.Equal(SaveOutcome.Saved, r.Outcome, r.Message);
                Assert.True(File.Exists(r.BackupPath));
                Assert.True(KeynoteFormat.Read(File.ReadAllBytes(r.BackupPath)).Library.Contains("B"), "backup is the previous file");
                Assert.Equal(KeynoteFormat.Sha256(mine), s.Store.CurrentHash(p));
                Assert.Equal(0, Directory.GetFiles(s.Root, "*.causafix.tmp").Length, "no temp left");
            }
        }

        [Test]
        public static void RestoreOnlyWhenTheFileIsStillOurs()
        {
            using (var s = new KeynoteSandbox())
            {
                string p = s.Write("kn.txt", "A\tOLD\r\n");
                byte[] original = File.ReadAllBytes(p);
                var r = s.Store.Save(p, KeynoteFormat.Encode("A\tNEW\r\n", KeynoteEncoding.Utf16LeBom, out _), KeynoteFormat.Sha256(original));
                Assert.Equal(SaveOutcome.Saved, r.Outcome);
                s.Write("kn.txt", "A\tSOMEONE ELSE\r\n");
                Assert.Equal(SaveOutcome.Conflict, s.Store.RestoreIfUnchanged(p, r.WrittenHash, original).Outcome);
                Assert.Equal("SOMEONE ELSE", KeynoteFormat.Read(File.ReadAllBytes(p)).Library.Find("A").Text, "not reverted blindly");
                var r2 = s.Store.Save(p, KeynoteFormat.Encode("A\tNEW2\r\n", KeynoteEncoding.Utf16LeBom, out _), s.Store.CurrentHash(p));
                Assert.Equal(SaveOutcome.Saved, s.Store.RestoreIfUnchanged(p, r2.WrittenHash, original).Outcome);
                Assert.Equal("OLD", KeynoteFormat.Read(File.ReadAllBytes(p)).Library.Find("A").Text);
            }
        }

        [Test]
        public static void LocksAndReadOnlyFilesAreNotWritten()
        {
            using (var s = new KeynoteSandbox())
            {
                string p = s.Write("kn.txt", "A\tX\r\n");
                File.WriteAllText(s.P("kn.lock"), "pyrevit");
                var a = s.Store.Probe(p);
                Assert.False(a.CanWrite);
                Assert.True(a.PyRevitLock);
                Assert.Equal(SaveOutcome.Failed, s.Store.Save(p, new byte[] { 1 }, s.Store.CurrentHash(p)).Outcome);
                Assert.True(File.Exists(s.P("kn.lock")), "lock untouched");
                File.Delete(s.P("kn.lock"));
                File.SetAttributes(p, FileAttributes.ReadOnly);
                Assert.Contains("read-only", s.Store.Probe(p).ReadOnlyReason);
                File.SetAttributes(p, FileAttributes.Normal);
                Assert.True(KeynoteFileStore.IsCloudPath(@"C:\Users\juan\DC\ACCDocs\Firm\Project\Project Files\keynotes.txt"));
                Assert.False(KeynoteFileStore.IsCloudPath(@"C:\Standards\keynotes.txt"));
            }
        }

        [Test]
        public static void DraftsAndTemplatesPersist()
        {
            using (var s = new KeynoteSandbox())
            {
                string p = s.Write("kn.txt", "A\tX\r\n");
                var d = new KeynoteDraft(KeynoteFormat.Read(File.ReadAllBytes(p)).Library);
                d.Add("A-1", "CHILD", "A");
                s.Store.SaveDraft(p, d, "HASH");
                var st = s.Store.LoadDraft(p);
                Assert.Equal("HASH", st.BaseHash);
                Assert.Equal(2, st.Current.Count);
                s.Store.DeleteDraft(p);
                Assert.Equal(null, s.Store.LoadDraft(p));
                var t = s.Store.LoadTemplates();
                Assert.True(t.Any(x => x.Text == "RESERVED") && t.Any(x => x.Text == "DO NOT USE"));
                s.Store.SaveTemplates(new[] { new TextTemplate { Name = "Típico", Text = "VER DETALLE TÍPICO" } });
                Assert.True(s.Store.LoadTemplates().Any(x => x.Text == "VER DETALLE TÍPICO"));
            }
        }
    }

    /// <summary>Keynote Manager window logic end to end with a simulated Revit (fake gateway) and real files.</summary>
    public static class KeynoteManagerFlowTests
    {
        private static void Run(System.Threading.Tasks.Task t) => t.GetAwaiter().GetResult();

        /// <summary>
        /// The mandatory authoring scenario: new library → group → subgroup → keynote → Save &amp; Reload → place a User
        /// keynote → edit its text → Save &amp; Reload → the tag shows the new text → reopening shows it persisted.
        /// </summary>
        [Test]
        public static void EndToEndAuthoring()
        {
            using (var s = new KeynoteSandbox())
            {
                var vm = s.Vm();
                Assert.False(vm.HasFile);
                Assert.Contains("has no keynote file", vm.FileStatus);

                string path = s.P("Electrical Keynotes.txt");
                s.Dlg.Paths.Enqueue(path);
                Run(vm.NewFileCommand.ExecuteAsync());                        // creates + asks to link (yes)
                Assert.True(File.Exists(path));
                Assert.True(vm.IsLinked, "linked to the model");
                Assert.Equal(path, s.Gw.TablePath);

                s.Dlg.Forms.Enqueue(FakeKeynoteDialogs.Answer("E", "ELECTRICAL"));
                vm.NewGroupCommand.Execute(null);
                Assert.Equal("E", vm.SelectedRow.Key);
                s.Dlg.Forms.Enqueue(FakeKeynoteDialogs.Answer("E-LTG", "LIGHTING"));
                vm.NewSubgroupCommand.Execute(null);
                Assert.Equal("E", s.Dlg.LastForm.ParentKey);
                Assert.Equal("E-LTG", vm.SelectedRow.Key);
                s.Dlg.Forms.Enqueue(FakeKeynoteDialogs.Answer("E-LTG-001", "PROVIDE LIGHT FITTING AS SHOWN."));
                vm.NewKeynoteCommand.Execute(null);
                Assert.Equal("E-LTG", s.Dlg.LastForm.ParentKey, "new keynote goes into the selected subgroup");
                Assert.Equal(3, vm.ChangeCount);
                Assert.Equal("E|E-LTG|E-LTG-001", string.Join("|", vm.Rows.Select(r => r.Key)));
                Assert.Equal(2, vm.Rows.Single(r => r.Key == "E-LTG-001").Depth);

                Assert.True(vm.SaveAsync(true).GetAwaiter().GetResult(), string.Join(";", s.Dlg.Errors));
                Assert.False(vm.IsDirty);
                Assert.Contains("verified", vm.Status);
                var onDisk = KeynoteFormat.Read(File.ReadAllBytes(path));
                Assert.Equal(KeynoteEncoding.Utf16LeBom, onDisk.Encoding);
                Assert.Equal("E-LTG", onDisk.Library.Find("E-LTG-001").ParentKey);
                Assert.True(s.Gw.LoadedEntries.Any(e => e.Key == "E-LTG-001"), "Revit table reloaded");

                vm.Select("E-LTG-001");
                Run(vm.PlaceCommand.ExecuteAsync("user"));
                var tag = s.Gw.Tags.Single();
                Assert.Equal("E-LTG-001", tag.Key);
                Assert.Equal(TagSource.User, tag.Source);
                Assert.Equal("PROVIDE LIGHT FITTING AS SHOWN.", s.Gw.DisplayedText(tag));
                Assert.Contains("1 tag ·", vm.Rows.Single(r => r.Key == "E-LTG-001").Usage);

                vm.Select("E-LTG-001");
                s.Dlg.Forms.Enqueue(FakeKeynoteDialogs.Answer(null, "PROVIDE LED LIGHT FITTING AS SHOWN."));
                vm.EditCommand.Execute(null);
                Assert.Equal("Modified", vm.Rows.Single(r => r.Key == "E-LTG-001").State);
                Assert.Equal("PROVIDE LIGHT FITTING AS SHOWN.", s.Gw.DisplayedText(tag), "Revit unchanged until Save & Reload");
                Assert.True(vm.SaveAsync(true).GetAwaiter().GetResult());
                Assert.Equal("PROVIDE LED LIGHT FITTING AS SHOWN.", s.Gw.DisplayedText(tag), "tag shows the new text");
                Assert.Equal("E-LTG-001", tag.Key, "key unchanged");

                var vm2 = new KeynoteSandboxReopen(s).Open();
                Assert.Equal("PROVIDE LED LIGHT FITTING AS SHOWN.", vm2.Draft.Current.Find("E-LTG-001").Text, "persisted");
                Assert.False(vm2.IsDirty);
                Assert.True(vm2.IsLinked);
                Assert.Contains("Matches file", vm2.RevitStatus);
                Assert.Equal(0, s.Dlg.Errors.Count, string.Join(";", s.Dlg.Errors));
            }
        }

        private sealed class KeynoteSandboxReopen
        {
            private readonly KeynoteSandbox _s;
            public KeynoteSandboxReopen(KeynoteSandbox s) { _s = s; }
            public KeynoteManagerViewModel Open() => _s.Vm();
        }

        private static KeynoteSandbox Linked(out string path, string text = "E\tELECTRICAL\r\nE-LTG\tLIGHTING\tE\r\nE-LTG-001\tONE\tE-LTG\r\nE-LTG-002\tTWO\tE-LTG\r\n")
        {
            var s = new KeynoteSandbox();
            path = s.Write("kn.txt", text);
            s.Gw.TablePath = path;
            s.Gw.Reload(new List<KeyRename>()).GetAwaiter().GetResult();
            return s;
        }

        [Test]
        public static void SaveConflictMergesWithTheOtherUser()
        {
            using (var s = Linked(out string p))
            {
                var vm = s.Vm();
                vm.Select("E-LTG-001");
                s.Dlg.Forms.Enqueue(FakeKeynoteDialogs.Answer(null, "ONE MINE"));
                vm.EditCommand.Execute(null);
                // Another user changes another record and adds one.
                s.Write("kn.txt", "E\tELECTRICAL\r\nE-LTG\tLIGHTING\tE\r\nE-LTG-001\tONE\tE-LTG\r\nE-LTG-002\tTWO THEIRS\tE-LTG\r\nE-LTG-003\tTHREE\tE-LTG\r\n");
                Assert.True(vm.SaveAsync(true).GetAwaiter().GetResult(), string.Join(";", s.Dlg.Errors));
                var lib = KeynoteFormat.Read(File.ReadAllBytes(p)).Library;
                Assert.Equal("ONE MINE", lib.Find("E-LTG-001").Text);
                Assert.Equal("TWO THEIRS", lib.Find("E-LTG-002").Text);
                Assert.True(lib.Contains("E-LTG-003"));
                Assert.True(s.Dlg.Reviews.Any(r => r.StartsWith("The file changed")), "their changes were shown");

                // Same record changed on both sides: the user decides; cancelling saves nothing.
                vm.Select("E-LTG-001");
                s.Dlg.Forms.Enqueue(FakeKeynoteDialogs.Answer(null, "ONE MINE AGAIN"));
                vm.EditCommand.Execute(null);
                string theirs = "E\tELECTRICAL\r\nE-LTG\tLIGHTING\tE\r\nE-LTG-001\tONE THEIRS\tE-LTG\r\nE-LTG-002\tTWO THEIRS\tE-LTG\r\nE-LTG-003\tTHREE\tE-LTG\r\n";
                s.Write("kn.txt", theirs);
                s.Dlg.OnConflicts = m => false;
                Assert.False(vm.SaveAsync(true).GetAwaiter().GetResult());
                Assert.Equal("ONE THEIRS", KeynoteFormat.Read(File.ReadAllBytes(p)).Library.Find("E-LTG-001").Text, "nothing overwritten");
                Assert.True(vm.IsDirty, "draft kept");
                s.Dlg.OnConflicts = m => { m.Conflicts.ForEach(c => c.Choice = ConflictChoice.Mine); return true; };
                Assert.True(vm.SaveAsync(true).GetAwaiter().GetResult());
                Assert.Equal("ONE MINE AGAIN", KeynoteFormat.Read(File.ReadAllBytes(p)).Library.Find("E-LTG-001").Text);
            }
        }

        [Test]
        public static void RekeyUpdatesModelReferencesOnSaveAndReload()
        {
            using (var s = Linked(out string p))
            {
                s.Gw.Tags.Add(new FakeTag { Id = 1, Key = "E-LTG-001", Source = TagSource.Element, ViewId = 100 });
                s.Gw.TypeKeynotes["Downlight 600"] = "E-LTG-001";
                var vm = s.Vm();
                vm.Select("E-LTG-001");
                s.Dlg.Texts.Enqueue("E-LTG-101");
                vm.RekeyCommand.Execute(null);
                Assert.Equal("Re-key", vm.Rows.Single(r => r.Key == "E-LTG-101").State);
                Assert.Contains("1 tag ·", vm.Rows.Single(r => r.Key == "E-LTG-101").Usage, "usage of the original key shown");

                s.Gw.BlockRekey = true;
                Assert.False(vm.SaveAsync(false).GetAwaiter().GetResult());
                Assert.True(KeynoteFormat.Read(File.ReadAllBytes(p)).Library.Contains("E-LTG-001"), "blocked: file untouched");

                s.Gw.BlockRekey = false;
                Assert.True(vm.SaveAsync(false).GetAwaiter().GetResult(), "re-key forces Save & Reload");
                Assert.True(s.Gw.Calls.Contains("CheckRekey"));
                Assert.Equal("E-LTG-101", s.Gw.Tags[0].Key);
                Assert.Equal("E-LTG-101", s.Gw.TypeKeynotes["Downlight 600"]);
                Assert.Equal("ONE", s.Gw.DisplayedText(s.Gw.Tags[0]));
                Assert.Equal(0, vm.Draft.Renames.Count);
            }
        }

        [Test]
        public static void FailedReloadOffersRestoreAndKeepsTheDraft()
        {
            using (var s = Linked(out string p))
            {
                var vm = s.Vm();
                vm.Select("E-LTG-002");
                s.Dlg.Forms.Enqueue(FakeKeynoteDialogs.Answer(null, "TWO EDITED"));
                vm.EditCommand.Execute(null);
                s.Gw.FailNextReload = true;
                s.Dlg.OnReview = (t, l) => t == "Reload failed";        // accept "restore"
                vm.SaveAsync(true).GetAwaiter().GetResult();
                Assert.Equal("TWO", KeynoteFormat.Read(File.ReadAllBytes(p)).Library.Find("E-LTG-002").Text, "file restored");
                Assert.True(vm.IsDirty, "edits still pending");
                Assert.Equal("TWO EDITED", vm.Draft.Current.Find("E-LTG-002").Text);
                s.Dlg.OnReview = (t, l) => true;
                Assert.True(vm.SaveAsync(true).GetAwaiter().GetResult());
                Assert.Equal("TWO EDITED", s.Gw.LoadedEntries.Single(e => e.Key == "E-LTG-002").Text);
            }
        }

        [Test]
        public static void UnknownUsageIsNeverZero()
        {
            using (var s = Linked(out _))
            {
                s.Gw.FailUsage = true;
                var vm = s.Vm();
                Assert.True(vm.Rows.All(r => r.Usage == "?"));
                vm.UsageFilter = UsageFilter.WithoutTags;
                Assert.Equal(0, vm.Rows.Count, "cannot claim 'without tags' when usage is unknown");
                vm.UsageFilter = UsageFilter.All;
                vm.Select("E-LTG-001");
                Assert.Contains("unknown", vm.SelectedUsageText);
            }
        }

        [Test]
        public static void DeletingAUsedKeyIsReviewedAndCancellable()
        {
            using (var s = Linked(out string p))
            {
                s.Gw.Tags.Add(new FakeTag { Id = 1, Key = "E-LTG-002", Source = TagSource.User, ViewId = 7 });
                var vm = s.Vm();
                vm.Select("E-LTG");
                s.Dlg.OnReview = (t, l) => false;
                vm.DeleteCommand.Execute(null);
                Assert.False(vm.IsDirty);
                Assert.Contains("IN USE", s.Dlg.Reviews.Last());
                s.Dlg.OnReview = (t, l) => true;
                vm.DeleteCommand.Execute(null);
                Assert.Equal(1, vm.Draft.Current.Records.Count);
                Assert.True(KeynoteFormat.Read(File.ReadAllBytes(p)).Library.Contains("E-LTG-002"), "file unchanged until Save");
            }
        }

        [Test]
        public static void FiltersSearchRegexAndBranch()
        {
            using (var s = Linked(out _, "E\tELECTRICAL\r\nE-LTG\tLIGHTING\tE\r\nE-LTG-001\tDOWNLIGHT\tE-LTG\r\nE-LTG-002\tEMERGENCY LIGHT\tE-LTG\r\nM\tMECHANICAL\r\nM-001\tDUCT\tM\r\n"))
            {
                s.Gw.Tags.Add(new FakeTag { Id = 1, Key = "M-001", Source = TagSource.Material, ViewId = 100 });
                var vm = s.Vm();
                vm.SearchText = "emergency";
                Assert.Equal("E|E-LTG|E-LTG-002", string.Join("|", vm.Rows.Select(r => r.Key)), "ancestors kept as context");
                Assert.True(vm.Rows[0].IsContext && !vm.Rows[2].IsContext);
                vm.SearchText = "";
                vm.RegexInclude = "^E-LTG-\\d+";
                Assert.Equal(4, vm.Rows.Count);
                vm.RegexInclude = "([";
                Assert.Contains("Invalid", vm.RegexError);
                vm.RegexInclude = "";
                vm.RegexExclude = "LIGHT";
                Assert.False(vm.Rows.Any(r => r.Key == "E-LTG-002"));
                vm.RegexExclude = "";
                vm.UsageFilter = UsageFilter.InActiveView;
                Assert.Equal("M|M-001", string.Join("|", vm.Rows.Select(r => r.Key)));
                vm.UsageFilter = UsageFilter.All;
                vm.SourceFilter = SourceFilter.Material;
                Assert.Equal("M|M-001", string.Join("|", vm.Rows.Select(r => r.Key)));
                vm.SourceFilter = SourceFilter.Any;
                vm.BranchKey = "E-LTG";
                Assert.Equal("E|E-LTG|E-LTG-001|E-LTG-002", string.Join("|", vm.Rows.Select(r => r.Key)));
                vm.ClearFiltersCommand.Execute(null);
                // Collapse keeps state across rebuilds.
                vm.Expand("E", false);
                Assert.False(vm.Rows.Any(r => r.Key == "E-LTG"));
                vm.SearchText = "x"; vm.SearchText = "";
                Assert.False(vm.Rows.Any(r => r.Key == "E-LTG"), "still collapsed");
            }
        }

        [Test]
        public static void CopyPasteDuplicateAndExport()
        {
            using (var s = Linked(out string p))
            {
                var vm = s.Vm();
                vm.Select("E-LTG");
                vm.CopyCommand.Execute(null);
                Assert.Contains("E-LTG-001\tONE\tE-LTG", s.Dlg.Clipboard);
                vm.Select("E");
                vm.PasteCommand.Execute(null);
                Assert.True(vm.Draft.Current.Contains("E-LTG-COPY"));
                Assert.Equal(0, vm.Draft.Current.Validate().Count);
                vm.Select("E-LTG-001");
                s.Dlg.Forms.Enqueue(r => new RecordFormResult { Key = r.Key, Text = r.Text, ParentKey = r.ParentKey });
                vm.DuplicateCommand.Execute(null);
                Assert.True(vm.Draft.Current.Contains("E-LTG-005") || vm.Draft.Current.Contains("E-LTG-003"), "suggested free key");
                vm.SearchText = "E-LTG-002";
                string exp = s.P("filtered.txt");
                s.Dlg.Paths.Enqueue(exp);
                vm.ExportFilteredCommand.Execute(null);
                var lib = KeynoteFormat.Read(File.ReadAllBytes(exp)).Library;
                Assert.Equal("E|E-LTG|E-LTG-002", string.Join("|", lib.Records.Select(r => r.Key)), "filtered export includes parents");
                Assert.Equal(0, lib.Validate().Count);
            }
        }

        [Test]
        public static void PlacingANewKeyAsksToSaveAndReloadFirst()
        {
            using (var s = Linked(out _))
            {
                var vm = s.Vm();
                vm.Select("E-LTG");
                s.Dlg.Forms.Enqueue(FakeKeynoteDialogs.Answer("E-LTG-009", "NINE"));
                vm.NewKeynoteCommand.Execute(null);
                s.Dlg.Confirms.Enqueue(false);
                Run(vm.PlaceCommand.ExecuteAsync("element"));
                Assert.Equal(0, s.Gw.Tags.Count, "not placed while the key is not in Revit");
                Run(vm.PlaceCommand.ExecuteAsync("element"));
                Assert.Equal(1, s.Gw.Tags.Count);
                Assert.Equal("NINE", s.Gw.DisplayedText(s.Gw.Tags[0]));
                Assert.Equal(TagSource.Element, s.Gw.Tags[0].Source);
                s.Gw.HasTagType = false;
                Run(vm.RefreshCommand.ExecuteAsync());
                Run(vm.PlaceCommand.ExecuteAsync("user"));
                Assert.Contains("keynote tag type", s.Dlg.Errors.Last());
            }
        }

        [Test]
        public static void DraftSurvivesACrashAndCloseAsks()
        {
            using (var s = Linked(out string p))
            {
                var vm = s.Vm();
                vm.Select("E-LTG-001");
                s.Dlg.Forms.Enqueue(FakeKeynoteDialogs.Answer(null, "ONE UNSAVED"));
                vm.EditCommand.Execute(null);
                s.Dlg.CloseAnswer = CloseChoice.Cancel;
                Assert.False(vm.ConfirmCloseAsync().GetAwaiter().GetResult());
                // "Crash": a new window over the same file offers the recovered draft.
                var vm2 = s.Vm();
                Assert.Equal("ONE UNSAVED", vm2.Draft.Current.Find("E-LTG-001").Text);
                Assert.True(vm2.IsDirty);
                s.Dlg.CloseAnswer = CloseChoice.Discard;
                Assert.True(vm2.ConfirmCloseAsync().GetAwaiter().GetResult());
                Assert.Equal(null, s.Store.LoadDraft(p));
                Assert.Equal("ONE", KeynoteFormat.Read(File.ReadAllBytes(p)).Library.Find("E-LTG-001").Text);
            }
        }

        [Test]
        public static void PyRevitFileIsReadOnlyAndSaveAsMakesAStandardCopy()
        {
            using (var s = new KeynoteSandbox())
            {
                string py = s.Write("pyrevit.txt", "# @datastore(\"deffrel\")\r\n# @table(categories:\"Root\")\r\nE\tELECTRICAL\r\n# @table(keynotes:\"Keynotes\")\r\nE-1\tFIRST\tE\r\n");
                byte[] before = File.ReadAllBytes(py);
                s.Gw.TablePath = py;
                var vm = s.Vm();
                Assert.True(vm.IsReadOnly);
                Assert.Contains("pyRevit", vm.ReadOnlyReason);
                Assert.False(vm.NewGroupCommand.CanExecute(null));
                string copy = s.P("standard.txt");
                s.Dlg.Paths.Enqueue(copy);
                s.Dlg.Confirms.Enqueue(true);   // link the copy
                Run(vm.SaveAsCommand.ExecuteAsync());
                Assert.True(before.SequenceEqual(File.ReadAllBytes(py)), "pyRevit file untouched");
                var d = KeynoteFormat.Read(File.ReadAllBytes(copy));
                Assert.False(d.IsDeffrelDb);
                Assert.Equal(2, d.Library.Records.Count);
                Assert.True(vm.CanEdit);
                Assert.True(vm.IsLinked);
            }
        }
    }
}
