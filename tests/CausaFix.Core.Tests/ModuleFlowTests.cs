using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CausaFix.Core.Compare;
using CausaFix.Core.Model;
using CausaFix.Core.Workbook;
using CausaFix.Core.Xlsx;
using CausaFix.ExcelLink;

namespace CausaFix.Core.Tests
{
    /// <summary>End-to-end flows of the Excel Link module against the in-memory gateway (no WPF, no Revit).</summary>
    public static class ModuleFlowTests
    {
        private sealed class Env : IDisposable
        {
            public readonly string Dir = Path.Combine(Path.GetTempPath(), "cf-flow-" + Guid.NewGuid().ToString("N"));
            public readonly FakeGateway Gateway = new FakeGateway();
            public readonly FakeDialogs Dialogs = new FakeDialogs();
            public readonly ExcelLinkViewModel Vm;

            public Env()
            {
                Directory.CreateDirectory(Dir);
                Vm = new ExcelLinkViewModel(Gateway, Dialogs, new ModulePaths
                {
                    ProfilesFolder = Path.Combine(Dir, "profiles"),
                    ReportsFolder = Path.Combine(Dir, "reports"),
                    SettingsFile = Path.Combine(Dir, "settings.json"),
                    LogsFolder = Path.Combine(Dir, "logs")
                });
                Vm.Settings.OpenFileAfterExport = false;
                Vm.InitializeAsync().GetAwaiter().GetResult();
            }

            public void SelectLighting(params ParamKey[] keys)
            {
                var tab = Vm.ModelTab;
                tab.Categories.All.First(c => c.Item.Name == "Lighting Fixtures").IsChecked = true;
                Wait();
                tab.Parameters.SetSelected(keys);
            }

            public static void Wait() => Task.Delay(50).GetAwaiter().GetResult();

            public void Dispose() { try { Directory.Delete(Dir, true); } catch { } }
        }

        [Test]
        public static void Tabs_LoadCategoriesAndParameters()
        {
            using (var env = new Env())
            {
                var tab = env.Vm.ModelTab;
                Assert.Equal(2, tab.Categories.Count, "model categories only (rooms are spatial too)");
                Assert.Equal(0, tab.Parameters.AvailableCount);
                tab.Categories.All.First(c => c.Item.Name == "Lighting Fixtures").IsChecked = true;
                Env.Wait();
                Assert.True(tab.Parameters.AvailableCount >= 10, "parameters loaded for the checked category");
                tab.Parameters.ShowReadOnly = false;
                Assert.False(tab.Parameters.VisibleAvailable.Any(p => p.IsReadOnly), "read-only filter");
                tab.Parameters.ShowReadOnly = true;
                tab.Parameters.ShowInstance = false;
                Assert.True(tab.Parameters.VisibleAvailable.All(p => p.IsType), "type filter");
                tab.Parameters.ShowInstance = true;
                tab.Parameters.SearchAvailable = "qa_";
                Assert.Equal(2, tab.Parameters.VisibleAvailable.Count, "both homonyms listed separately");
                Assert.False(env.Vm.PreviewCommand.CanExecute(null), "nothing selected yet");
                foreach (var p in tab.Parameters.VisibleAvailable) p.IsSelected = true;
                tab.Parameters.AddCommand.Execute(null);
                Assert.Equal(2, tab.Parameters.SelectedCount);
                Assert.True(env.Vm.PreviewCommand.CanExecute(null));
                Assert.Contains("parameters selected 2", env.Vm.Counters);

                // ordering
                tab.Parameters.Selected[1].IsSelected = true;
                tab.Parameters.MoveTopCommand.Execute(null);
                Assert.Equal(TestData.QaStatusB, tab.Parameters.Selected[0].Item.Key);

                // annotation and spatial tabs filter categories by kind
                env.Vm.SelectedTab = 1;
                Env.Wait();
                Assert.Equal("Lighting Fixture Tags", env.Vm.AnnotationTab.Categories.All.Single().Item.Name);
                env.Vm.SelectedTab = 4;
                Env.Wait();
                Assert.Equal("Rooms", env.Vm.SpatialTab.Categories.All.Single().Item.Name);
            }
        }

        [Test]
        public static void PreviewEditApply_UsesSameEngineAndRefreshes()
        {
            using (var env = new Env())
            {
                env.SelectLighting(TestData.Mark, TestData.Width, TestData.TypeMark, TestData.ApparentLoad);
                env.Vm.PreviewCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.True(env.Vm.IsPreviewTab, "switched to Preview/Edit");
                var pv = env.Vm.Preview;
                var markCol = pv.Columns.First(c => c.Def.Key == TestData.Mark);
                var widthCol = pv.Columns.First(c => c.Def.Key == TestData.Width);
                var loadCol = pv.Columns.First(c => c.Def.Key == TestData.ApparentLoad);
                var row1 = pv.VisibleRows.First(r => r.Data.UniqueId == TestData.Uid(1));
                var linked = pv.VisibleRows.First(r => r.IsLinked);

                Assert.Equal("600", row1.Cells[widthCol.Index].Text, "shown in mm");
                row1.Cells[markCol.Index].Text = "M-1-grid";
                row1.Cells[widthCol.Index].Text = "650";
                row1.Cells[loadCol.Index].Text = "1";              // read-only: ignored
                linked.Cells[markCol.Index].Text = "nope";          // linked: ignored
                Assert.Equal(2, pv.PendingCount);
                Assert.Equal("M-5", linked.Cells[markCol.Index].Text);

                var row2 = pv.VisibleRows.First(r => r.Data.UniqueId == TestData.Uid(2));
                row2.Cells[widthCol.Index].Text = "abc";
                Assert.Equal(GridCellState.Invalid, row2.Cells[widthCol.Index].State);
                row2.Cells[widthCol.Index].Text = "";
                Assert.Equal(GridCellState.Unchanged, row2.Cells[widthCol.Index].State, "blank input is not a clear");

                // filters never discard pending edits
                pv.Search = "zzz";
                Assert.Equal(0, pv.VisibleRows.Count);
                pv.Search = "";
                Assert.Equal(2, pv.PendingCount);

                env.Vm.ApplyPreviewCommand.ExecuteAsync().GetAwaiter().GetResult();
                var review = env.Dialogs.LastReview;
                Assert.True(review != null, "review shown");
                Assert.Equal(2, review.WriteCount);
                Assert.Equal(0, env.Gateway.ApplyCalls, "editing never writes by itself");
                review.ApplyAsync().GetAwaiter().GetResult();
                Env.Wait();
                Assert.True(review.Result.Committed);
                Assert.Equal("M-1-grid", env.Gateway.Model.Value(TestData.Uid(1), TestData.Mark).Text);
                Assert.Near(650 / 304.8, env.Gateway.Model.Value(TestData.Uid(1), TestData.Width).Double, 1e-12);
                Assert.Equal(0, env.Vm.Preview.PendingCount, "preview reloaded after apply");
                var reloaded = env.Vm.Preview.VisibleRows.First(r => r.Data.UniqueId == TestData.Uid(1));
                Assert.Equal("650", reloaded.Cells[widthCol.Index].Text);
                Assert.True(File.Exists(review.ReportPath), "report written");
                Assert.Contains("M-1-grid", File.ReadAllText(review.ReportPath));
            }
        }

        [Test]
        public static void Grid_PasteClearRevert()
        {
            using (var env = new Env())
            {
                env.SelectLighting(TestData.Mark, TestData.Comments, TestData.ApparentLoad);
                env.Vm.PreviewCommand.ExecuteAsync().GetAwaiter().GetResult();
                var pv = env.Vm.Preview;
                var mark = pv.Columns.First(c => c.Def.Key == TestData.Mark);
                string msg = pv.Paste("A\tB\tC\nD\tE\tF", pv.VisibleRows[0], mark);
                Assert.Contains("4 cell(s) pasted", msg);
                Assert.Contains("2 read-only", msg);
                Assert.Equal("A", pv.VisibleRows[0].Cells[mark.Index].Text);
                var comments = pv.Columns.First(c => c.Def.Key == TestData.Comments);
                pv.ClearValues(new[] { pv.VisibleRows[2].Cells[comments.Index] });
                Assert.Equal(GridCellState.Edited, pv.VisibleRows[2].Cells[comments.Index].State);
                string copied = PreviewViewModel.Copy(pv.VisibleRows.Take(2).ToList(), new[] { mark });
                Assert.Equal("A\r\nD\r\n", copied);
                pv.RevertAll();
                Assert.Equal(0, pv.PendingCount);
                Assert.Equal("M-1", pv.VisibleRows[0].Cells[mark.Index].Text);
            }
        }

        [Test]
        public static void ExcelRoundTrip_ExportEditImportApply()
        {
            using (var env = new Env())
            {
                env.SelectLighting(TestData.Mark, TestData.TypeMark, TestData.Voltage, TestData.Comments);
                string file = Path.Combine(env.Dir, "out.xlsx");
                env.Dialogs.SavePath = file;
                env.Vm.ExportCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.True(File.Exists(file), "workbook written: " + string.Join(";", env.Dialogs.Errors));

                // untouched re-import: no review, "no differences"
                env.Dialogs.OpenPath = file;
                var none = env.Vm.ImportFileAsync(file).GetAwaiter().GetResult();
                Assert.True(none == null, "no review for an unedited workbook");
                Assert.True(env.Dialogs.Infos.Any(i => i.Contains("No differences")));

                // edit in "Excel"
                var wb = XlsxReader.Load(file);
                var s = wb.Find("Lighting Fixtures");
                s.Set(TestData.Row(s, TestData.Uid(3)), TestData.Col(s, TestData.Voltage), XCell.Num(400));
                s.Set(TestData.Row(s, TestData.Uid(1)), TestData.Col(s, TestData.Comments), XCell.Str("from excel"));
                XlsxWriter.Save(wb, file);
                // meanwhile someone edits Mark in Revit: not touched by Excel -> must be preserved
                env.Gateway.Model.Set(TestData.Uid(2), TestData.Mark, Model.RawValue.FromText("changed in revit"));

                var review = env.Vm.ImportFileAsync(file).GetAwaiter().GetResult();
                Assert.True(review != null, "review shown: " + string.Join(";", env.Dialogs.Errors));
                Assert.Equal(2, review.WriteCount);
                Assert.True(review.Changes.Entries.Any(e => e.TargetIsType && e.TargetUniqueId == TestData.T2), "voltage is a type change");
                review.ApplyAsync().GetAwaiter().GetResult();
                Assert.True(review.Result.Committed);
                Assert.Equal("from excel", env.Gateway.Model.Value(TestData.Uid(1), TestData.Comments).Text);
                Assert.Near(400 * FakeUnits.VoltFactor, env.Gateway.Model.Value(TestData.T2, TestData.Voltage).Double, 1e-9);
                Assert.Equal("changed in revit", env.Gateway.Model.Value(TestData.Uid(2), TestData.Mark).Text, "concurrent Revit edit kept");
            }
        }

        [Test]
        public static void Import_FromAnotherModel_IsBlocked_AndLineageNeedsConfirmation()
        {
            using (var env = new Env())
            {
                env.SelectLighting(TestData.Mark);
                string file = Path.Combine(env.Dir, "other.xlsx");
                env.Dialogs.SavePath = file;
                env.Vm.ExportCommand.ExecuteAsync().GetAwaiter().GetResult();

                env.Gateway.Identity = new DocumentIdentity { Title = "Other", CreationGuid = Guid.NewGuid().ToString(), ProjectInfoUniqueId = "pi-x" };
                var r = env.Vm.ImportFileAsync(file).GetAwaiter().GetResult();
                Assert.True(r == null);
                Assert.True(env.Dialogs.Errors.Any(e => e.Contains("different model")), "blocked with explanation");

                var copy = TestData.Doc(); copy.PathName = @"C:\copies\copy.rvt"; copy.CentralPath = @"\\other\copy.rvt";
                env.Gateway.Identity = copy;
                env.Dialogs.ConfirmAnswer = false;
                r = env.Vm.ImportFileAsync(file).GetAwaiter().GetResult();
                Assert.True(r == null, "user declined the lineage confirmation");
                Assert.True(env.Dialogs.Confirms.Any(c => c.Contains("could not fully prove")));
            }
        }

        [Test]
        public static void AtomicFailure_RollsBack_AndReportsHonestly()
        {
            using (var env = new Env())
            {
                env.SelectLighting(TestData.Mark);
                env.Vm.PreviewCommand.ExecuteAsync().GetAwaiter().GetResult();
                var pv = env.Vm.Preview;
                var mark = pv.Columns.First(c => c.Def.Key == TestData.Mark);
                pv.VisibleRows.First(r => r.Data.UniqueId == TestData.Uid(1)).Cells[mark.Index].Text = "x1";
                pv.VisibleRows.First(r => r.Data.UniqueId == TestData.Uid(2)).Cells[mark.Index].Text = "x2";
                env.Gateway.FailWritesOn.Add(TestData.Uid(2));
                env.Vm.ApplyPreviewCommand.ExecuteAsync().GetAwaiter().GetResult();
                var review = env.Dialogs.LastReview;
                Assert.True(review.AtomicMode, "atomic by default");
                review.ApplyAsync().GetAwaiter().GetResult();
                Assert.False(review.Result.Committed);
                Assert.Equal(0, review.Result.Applied);
                Assert.Equal("M-1", env.Gateway.Model.Value(TestData.Uid(1), TestData.Mark).Text, "first change rolled back");
                Assert.Contains("NOT applied", review.ResultSummary);
                string report = File.ReadAllText(review.ReportPath);
                Assert.Contains("RolledBack", report);
                Assert.Equal(2, env.Vm.Preview.PendingCount, "edits kept for another attempt");

                // valid-only mode applies the good one
                env.Vm.ApplyPreviewCommand.ExecuteAsync().GetAwaiter().GetResult();
                var review2 = env.Dialogs.LastReview;
                review2.ValidOnlyMode = true;
                review2.ApplyAsync().GetAwaiter().GetResult();
                Assert.True(review2.Result.Committed);
                Assert.Equal(1, review2.Result.Applied);
                Assert.Equal(1, review2.Result.Failed);
                Assert.Equal("x1", env.Gateway.Model.Value(TestData.Uid(1), TestData.Mark).Text);
            }
        }

        [Test]
        public static void ConcurrentChangeAfterReview_IsNotOverwritten()
        {
            using (var env = new Env())
            {
                env.SelectLighting(TestData.Mark);
                env.Vm.PreviewCommand.ExecuteAsync().GetAwaiter().GetResult();
                var pv = env.Vm.Preview;
                var mark = pv.Columns.First(c => c.Def.Key == TestData.Mark);
                pv.VisibleRows.First(r => r.Data.UniqueId == TestData.Uid(1)).Cells[mark.Index].Text = "mine";
                env.Vm.ApplyPreviewCommand.ExecuteAsync().GetAwaiter().GetResult();
                // someone edits the element between review and apply
                env.Gateway.Model.Set(TestData.Uid(1), TestData.Mark, Model.RawValue.FromText("theirs"));
                var review = env.Dialogs.LastReview;
                review.ApplyAsync().GetAwaiter().GetResult();
                Assert.False(review.Result.Committed);
                Assert.Equal("theirs", env.Gateway.Model.Value(TestData.Uid(1), TestData.Mark).Text);
                Assert.Contains("after the review", review.Result.Ops[0].Message);
            }
        }

        [Test]
        public static void Profiles_SaveResetLoad()
        {
            using (var env = new Env())
            {
                env.SelectLighting(TestData.QaStatusB, TestData.Mark);
                env.Dialogs.PromptAnswer = "QA lighting";
                env.Vm.SaveProfileAsCommand.Execute(null);
                Assert.True(env.Vm.Profiles.Contains("QA lighting"));
                env.Vm.ModelTab.ResetSelection();
                Assert.Equal(0, env.Vm.ModelTab.Parameters.SelectedCount);
                env.Vm.SelectedProfile = "QA lighting";
                env.Vm.LoadProfileCommand.ExecuteAsync().GetAwaiter().GetResult();
                Env.Wait();
                var sel = env.Vm.ModelTab.Parameters.Selected.Select(s => s.Item.Key).ToList();
                Assert.Equal(2, sel.Count);
                Assert.Equal(TestData.QaStatusB, sel[0], "order restored by key");
                Assert.Equal(1, env.Vm.ModelTab.Categories.CheckedCount);
            }
        }

        [Test]
        public static void Schedules_TabCapabilitiesAndPresentationExport()
        {
            using (var env = new Env())
            {
                env.Vm.SelectedTab = 3;
                Env.Wait();
                var st = env.Vm.SchedulesTab;
                Assert.Equal(2, st.Schedules.Count);
                st.CheckOnActiveSheetCommand.Execute(null);
                Assert.Equal("Room Style Keys", st.Schedules.Checked.Single().Name);
                Assert.False(st.CanExport, "key schedule cannot be exported in data mode");
                st.IsPresentation = true;
                Assert.True(st.CanExport, "presentation export is always possible");
                string file = Path.Combine(env.Dir, "pres.xlsx");
                env.Dialogs.SavePath = file;
                env.Vm.ExportCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.True(File.Exists(file), string.Join(";", env.Dialogs.Errors));
                var r = env.Vm.ImportFileAsync(file).GetAwaiter().GetResult();
                Assert.True(r == null, "presentation workbook has nothing to import");
            }
        }
    }
}
