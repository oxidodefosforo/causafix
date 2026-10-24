using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CausaFix.Core.Mvvm;
using CausaFix.ProjectSetup.Execution;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Naming;
using CausaFix.ProjectSetup.Planning;
using CausaFix.ProjectSetup.Profiles;

namespace CausaFix.ProjectSetup.ViewModels
{
    /// <summary>Build Name, numbering profiles, Views on Sheet compositions, view-title placement and renumbering.</summary>
    public sealed partial class ProjectSetupViewModel
    {
        private NumberingProfileItem _selectedNumbering;
        private CompositionItem _selectedComposition;
        private TitlePlacementItem _selectedTitleScope;
        private TitlePlacementItem _rowTitle;
        private NameRuleItem _viewNameBuilder, _titleBuilder, _sheetNameBuilder;
        private List<FieldOption> _fieldOptions;
        private string _choiceSignature, _titleSignature;
        private bool _renumberSelectedOnly;

        public ObservableCollection<NumberingProfileItem> NumberingProfiles { get; } = new ObservableCollection<NumberingProfileItem>();
        public ObservableCollection<CompositionItem> Compositions { get; } = new ObservableCollection<CompositionItem>();
        public ObservableCollection<RenumberItem> Renumbers { get; } = new ObservableCollection<RenumberItem>();
        public ObservableCollection<RenumberItem> PendingRenumbers { get; } = new ObservableCollection<RenumberItem>();
        public ObservableCollection<TitlePlacementItem> TitleScopes { get; } = new ObservableCollection<TitlePlacementItem>();

        public IList<KeyName> NumberingChoices { get; private set; } = new List<KeyName>();
        public IList<KeyName> NumberingChoicesInherit { get; private set; } = new List<KeyName>();
        public IList<KeyName> ViewSetChoices { get; private set; } = new List<KeyName>();
        public IList<KeyName> TitleBlockChoicesInherit { get; private set; } = new List<KeyName>();
        public IList<KeyName> GroupChoicesInherit { get; private set; } = new List<KeyName>();
        /// <summary>Viewport types with their "Show Title" value (when Revit exposes it).</summary>
        public IList<KeyName> ViewportTypeChoices { get; private set; } = new List<KeyName>();
        public IList<KeyName> ViewportTypeChoicesInherit { get; private set; } = new List<KeyName>();

        public IEnumerable<CompositionMode> CompositionModes => (CompositionMode[])Enum.GetValues(typeof(CompositionMode));
        public IEnumerable<TitleVisibility> TitleVisibilities => (TitleVisibility[])Enum.GetValues(typeof(TitleVisibility));
        public IEnumerable<TitleMode> TitleModes => (TitleMode[])Enum.GetValues(typeof(TitleMode));
        public IEnumerable<TitleAnchor> TitleAnchors => (TitleAnchor[])Enum.GetValues(typeof(TitleAnchor));
        public IEnumerable<TitleRelativeTo> TitleRelativeTos => (TitleRelativeTo[])Enum.GetValues(typeof(TitleRelativeTo));
        public IEnumerable<TitleCaptureKind> TitleCaptureKinds => (TitleCaptureKind[])Enum.GetValues(typeof(TitleCaptureKind));
        public IEnumerable<TitleLineMode> TitleLineModes => (TitleLineMode[])Enum.GetValues(typeof(TitleLineMode));
        public IEnumerable<SequenceOrder> SequenceOrders => (SequenceOrder[])Enum.GetValues(typeof(SequenceOrder));

        public NameRuleItem ViewNameBuilder { get => _viewNameBuilder; private set => Set(ref _viewNameBuilder, value); }
        public NameRuleItem TitleBuilder { get => _titleBuilder; private set => Set(ref _titleBuilder, value); }
        public NameRuleItem SheetNameBuilder { get => _sheetNameBuilder; private set => Set(ref _sheetNameBuilder, value); }

        public NumberingProfileItem SelectedNumbering
        {
            get => _selectedNumbering;
            set { if (Set(ref _selectedNumbering, value)) { DuplicateNumberingCommand.Refresh(); RemoveNumberingCommand.Refresh(); ExportNumberingCommand.Refresh(); RefreshExamples(); } }
        }
        public CompositionItem SelectedComposition
        {
            get => _selectedComposition;
            set { if (Set(ref _selectedComposition, value)) { DuplicateCompositionCommand.Refresh(); RemoveCompositionCommand.Refresh(); AddViewSetMemberCommand.Refresh(); AddExistingMemberCommand.Refresh(); RefreshExamples(); } }
        }
        public TitlePlacementItem SelectedTitleScope { get => _selectedTitleScope; set { if (Set(ref _selectedTitleScope, value)) PickReferenceTitleCommand.Refresh(); } }
        /// <summary>Title override of the viewport selected in Review.</summary>
        public TitlePlacementItem RowTitle { get => _rowTitle; private set => Set(ref _rowTitle, value); }
        public bool RenumberSelectedOnly { get => _renumberSelectedOnly; set => Set(ref _renumberSelectedOnly, value); }

        public RelayCommand AddNumberingCommand { get; private set; }
        public RelayCommand DuplicateNumberingCommand { get; private set; }
        public RelayCommand RemoveNumberingCommand { get; private set; }
        public RelayCommand ImportNumberingCommand { get; private set; }
        public RelayCommand ExportNumberingCommand { get; private set; }
        public RelayCommand AddCompositionCommand { get; private set; }
        public RelayCommand DuplicateCompositionCommand { get; private set; }
        public RelayCommand RemoveCompositionCommand { get; private set; }
        public RelayCommand AddViewSetMemberCommand { get; private set; }
        public RelayCommand AddExistingMemberCommand { get; private set; }
        public RelayCommand RemoveMemberCommand { get; private set; }
        public RelayCommand PreviewRenumberCommand { get; private set; }
        public RelayCommand ApplyRenumberCommand { get; private set; }
        public RelayCommand ApprovePendingCommand { get; private set; }
        public AsyncCommand PickReferenceTitleCommand { get; private set; }

        private void InitSheetFeatures(Action<Exception> err)
        {
            AddNumberingCommand = new RelayCommand(() => Guard(AddNumbering));
            DuplicateNumberingCommand = new RelayCommand(() => Guard(DuplicateNumbering), () => SelectedNumbering != null);
            RemoveNumberingCommand = new RelayCommand(() => Guard(RemoveNumbering), () => SelectedNumbering != null);
            ImportNumberingCommand = new RelayCommand(() => Guard(ImportNumbering));
            ExportNumberingCommand = new RelayCommand(() => Guard(ExportNumbering), () => SelectedNumbering != null);
            AddCompositionCommand = new RelayCommand(() => Guard(AddComposition));
            DuplicateCompositionCommand = new RelayCommand(() => Guard(DuplicateComposition), () => SelectedComposition != null);
            RemoveCompositionCommand = new RelayCommand(() => Guard(RemoveComposition), () => SelectedComposition != null);
            AddViewSetMemberCommand = new RelayCommand(() => Guard(() => AddMember(existing: false)), () => SelectedComposition != null);
            AddExistingMemberCommand = new RelayCommand(() => Guard(() => AddMember(existing: true)), () => SelectedComposition != null);
            RemoveMemberCommand = new RelayCommand(o => Guard(() => RemoveMember(o as MemberItem)));
            PreviewRenumberCommand = new RelayCommand(() => Guard(PreviewRenumber));
            ApplyRenumberCommand = new RelayCommand(() => Guard(ApplyRenumber));
            ApprovePendingCommand = new RelayCommand(() => Guard(ApprovePending));
            PickReferenceTitleCommand = new AsyncCommand(PickReferenceTitle, () => !IsBusy && SelectedTitleScope != null, err);
        }

        // ------------------------------------------------------------------ defaults

        /// <summary>A new View Set with builder rules and the standard numbering profile.</summary>
        private ViewSetRule NewRule(string name, string code, string vft, string tb, string series)
        {
            var r = new ViewSetRule
            {
                Name = name, Code = code, DisciplineCode = "E", Family = PlanFamily.FloorPlan, ViewFamilyTypeUniqueId = vft ?? "", TitleBlockUniqueId = tb ?? "",
                Series = series ?? "",
                ViewNameRule = NameRule.Of(NameComponent.OfField(NameField.Discipline), NameComponent.OfField(NameField.LevelCode, "-"), NameComponent.OfField(NameField.ViewSetName, "-")),
                TitleRule = NameRule.Of(NameComponent.OfField(NameField.LevelName), NameComponent.OfField(NameField.ViewSetName, " - ")),
                SheetNameRule = NameRule.Of(NameComponent.OfField(NameField.LevelName), NameComponent.OfField(NameField.ViewSetName, " - "))
            };
            r.NumberingProfileKey = StandardNumbering().Key;
            return r;
        }

        /// <summary>
        /// "Standard" numbering: [Prefix-]Discipline[-Zone][-Condition]-Series+Sequence, every optional segment omitted with
        /// its separator when empty. E-101 · E-A-101 · E-A-P-101 · FCC-E-A-P-101 (Series 1, sequence 01).
        /// </summary>
        private NumberingProfile StandardNumbering()
        {
            var p = _cfg.NumberingProfiles.FirstOrDefault(x => x.Name == "Standard" && !x.DerivedFromLegacy);
            if (p != null) return p;
            var cond = NameComponent.OfField(NameField.Condition, "-", required: false);
            cond.Mappings.AddRange(new[] { new ValueMapping { From = "Proposed", To = "P" }, new ValueMapping { From = "Demolished", To = "D" }, new ValueMapping { From = "Existing", To = "E" } });
            var seq = NameComponent.OfField(NameField.Sequence);
            seq.PadDigits = 2;
            p = new NumberingProfile
            {
                Name = "Standard",
                Rule = NameRule.Of(NameComponent.OfField(NameField.Purpose, "", required: false), NameComponent.OfField(NameField.Discipline, "-"),
                                   NameComponent.OfField(NameField.Zone, "-", required: false), cond, NameComponent.OfField(NameField.Series, "-"), seq),
                Start = 1, Increment = 1, Order = SequenceOrder.ViewSetThenLevel,
                GroupBy = new List<NameField> { NameField.Discipline, NameField.Series }
            };
            _cfg.NumberingProfiles.Add(p);
            return p;
        }

        // ------------------------------------------------------------------ editors

        private void LoadSheetEditors()
        {
            SetupMigration.Upgrade(_cfg);
            var keepNum = _selectedNumbering?.Key;
            NumberingProfiles.Clear();
            foreach (var p in _cfg.NumberingProfiles) NumberingProfiles.Add(NewNumberingItem(p));
            _selectedNumbering = NumberingProfiles.FirstOrDefault(n => n.Key == keepNum) ?? NumberingProfiles.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedNumbering));
            var keepComp = _selectedComposition?.Composition.Key;
            Compositions.Clear();
            foreach (var c in _cfg.Compositions) Compositions.Add(NewCompositionItem(c));
            _selectedComposition = Compositions.FirstOrDefault(c => c.Composition.Key == keepComp) ?? Compositions.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedComposition));
            _choiceSignature = null;
            _titleSignature = null;
            RebuildChoiceLists();
            ViewNameBuilder = TitleBuilder = SheetNameBuilder = null;   // new pickers for a refreshed catalog
            RebuildSetBuilders();
            DuplicateNumberingCommand?.Refresh(); RemoveNumberingCommand?.Refresh(); ExportNumberingCommand?.Refresh();
            DuplicateCompositionCommand?.Refresh(); RemoveCompositionCommand?.Refresh(); AddViewSetMemberCommand?.Refresh(); AddExistingMemberCommand?.Refresh();
        }

        private NumberingProfileItem NewNumberingItem(NumberingProfile p)
            => new NumberingProfileItem(p, Builder(p.Rule, NameContext.SheetNumber, "Sheet Number – " + p.Name), Changed);

        private CompositionItem NewCompositionItem(SheetComposition c)
            => new CompositionItem(c, Changed, rule => Builder(rule, NameContext.SheetName, "Sheet Name (composition)", NameContext.ViewName));

        private NameRuleItem Builder(NameRule rule, NameContext target, string title, params NameContext[] sameAs)
            => new NameRuleItem(rule, target, title, () => FieldOptions(target), Changed, sameAs) { ParameterAdded = p => _ = EnsureValuesAsync(p) };

        private void RebuildSetBuilders()
        {
            var r = SelectedSet?.Rule;
            if (r == null) { ViewNameBuilder = TitleBuilder = SheetNameBuilder = null; return; }
            r.EnsureRules();
            if (ViewNameBuilder?.Rule != r.ViewNameRule) ViewNameBuilder = Builder(r.ViewNameRule, NameContext.ViewName, "View Name");
            if (TitleBuilder?.Rule != r.TitleRule) TitleBuilder = Builder(r.TitleRule, NameContext.TitleOnSheet, "Title on Sheet", NameContext.ViewName);
            if (SheetNameBuilder?.Rule != r.SheetNameRule) SheetNameBuilder = Builder(r.SheetNameRule, NameContext.SheetName, "Sheet Name", NameContext.ViewName, NameContext.TitleOnSheet);
        }

        /// <summary>Picker entries: CausaFix fields, plan values and Revit parameters (by scope; homonyms disambiguated).</summary>
        private IList<FieldOption> FieldOptions(NameContext target)
        {
            if (_fieldOptions == null)
            {
                var list = new List<FieldOption>();
                foreach (var f in NameFields.ConfigFields) list.Add(new FieldOption { Group = "CausaFix fields", Label = NameFields.Label(f), Detail = "configuration (CausaFix, not a Revit parameter)", Source = ComponentSource.Field, Field = f });
                foreach (var f in NameFields.ComputedFields) list.Add(new FieldOption { Group = "Computed by the plan", Label = NameFields.Label(f), Detail = f == NameField.Sequence ? "sequence of the sheet (numbering profile)" : "value known from the plan", Source = ComponentSource.Field, Field = f });
                var homonyms = new HashSet<string>(_cat.Parameters.GroupBy(p => p.Ref.Scope + "|" + p.Name.ToUpperInvariant()).Where(g => g.Count() > 1).Select(g => g.Key));
                foreach (var p in _cat.Parameters)
                {
                    bool dup = homonyms.Contains(p.Ref.Scope + "|" + p.Name.ToUpperInvariant());
                    string g = p.Ref.Guid ?? "";
                    string id = p.Ref.Kind == ParamKind.Shared ? "GUID " + g.Substring(0, Math.Min(8, g.Length)) : p.Ref.Kind == ParamKind.BuiltIn ? "built-in " + p.Ref.BuiltInId : "project parameter";
                    string scopeNote = p.Ref.Scope == ParamScope.View ? " · value from existing/reused views only"
                                     : p.Ref.Scope == ParamScope.Sheet ? " · value from existing sheets only"
                                     : p.Ref.Scope == ParamScope.TitleBlockInstance ? " · existing sheets with one title block" : "";
                    var r = p.Ref.Clone();
                    r.DocumentKey = _gw.DocumentKey;
                    list.Add(new FieldOption
                    {
                        Group = "Revit · " + ParamRef.ScopeLabel(p.Ref.Scope),
                        Label = p.Name + (dup ? "  [" + id + "]" : ""),
                        Detail = p.Detail + scopeNote,
                        Source = ComponentSource.Parameter,
                        Param = r
                    });
                }
                _fieldOptions = list;
            }
            var self = target == NameContext.ViewName ? NameField.ViewName : target == NameContext.TitleOnSheet ? NameField.TitleOnSheet : target == NameContext.SheetNumber ? NameField.SheetNumber : NameField.SheetName;
            return _fieldOptions.Where(o => !(o.Source == ComponentSource.Field && o.Field == self)).ToList();
        }

        /// <summary>Cached combo sources: replaced only when their content changes (a new list makes WPF re-write the selection).</summary>
        private void RebuildChoiceLists()
        {
            string sig = string.Join("|", _cfg.NumberingProfiles.Select(p => p.Key + "=" + p.Name)) + "#" + string.Join("|", _cfg.ViewSets.Select(v => v.Key + "=" + v.Name)) + "#" +
                         string.Join("|", _cfg.Groups.Select(g => g.Key)) + "#" + _cat.TitleBlocks.Count + "#" + _cat.ViewportTypes.Count + "#" + _cat.DocumentTitle;
            if (sig != _choiceSignature)
            {
                _choiceSignature = sig;
                NumberingChoices = _cfg.NumberingProfiles.Select(p => new KeyName { Key = p.Key, Name = p.Name }).ToList();
                NumberingChoicesInherit = new[] { new KeyName { Key = "", Name = "(primary View Set's)" } }.Concat(NumberingChoices).ToList();
                ViewSetChoices = _cfg.ViewSets.Select(v => new KeyName { Key = v.Key, Name = v.Name }).ToList();
                TitleBlockChoicesInherit = new[] { new KeyName { Key = "", Name = "(primary View Set's)" } }.Concat(_cat.TitleBlocks.Select(t => new KeyName { Key = t.UniqueId, Name = t.Display })).ToList();
                GroupChoicesInherit = new[] { new KeyName { Key = "", Name = "(primary View Set's)" } }.Concat(_cfg.Groups.Select(g => new KeyName { Key = g.Key, Name = g.Key })).ToList();
                ViewportTypeChoices = _cat.ViewportTypes.Select(t => new KeyName { Key = t.UniqueId, Name = t.Name + (string.IsNullOrEmpty(t.ShowTitleText) ? "" : "   (Show Title: " + t.ShowTitleText + ")") }).ToList();
                ViewportTypeChoicesInherit = new[] { new KeyName { Key = "", Name = "(automatic)" } }.Concat(ViewportTypeChoices).ToList();
                foreach (var n in new[] { nameof(NumberingChoices), nameof(NumberingChoicesInherit), nameof(ViewSetChoices), nameof(TitleBlockChoicesInherit), nameof(GroupChoicesInherit), nameof(ViewportTypeChoices), nameof(ViewportTypeChoicesInherit) })
                    OnPropertyChanged(n);
            }
            string tsig = string.Join("|", _cfg.Groups.Select(g => g.Key)) + "#" + string.Join("|", _cfg.Compositions.Select(c => c.Key + "=" + c.Name));
            if (tsig != _titleSignature)
            {
                _titleSignature = tsig;
                string keep = _selectedTitleScope?.Scope;
                TitleScopes.Clear();
                TitleScopes.Add(new TitlePlacementItem("Profile default", () => _cfg.Title, v => _cfg.Title = v ?? new TitlePlacement(), false, null, Changed));
                foreach (var g in _cfg.Groups)
                {
                    var grp = g;
                    TitleScopes.Add(new TitlePlacementItem("Group: " + g.Key, () => grp.Title, v => grp.Title = v, true, () => _cfg.Title, Changed));
                }
                foreach (var c in _cfg.Compositions)
                {
                    var comp = c;
                    TitleScopes.Add(new TitlePlacementItem("Composition: " + c.Name, () => comp.Title, v => comp.Title = v, true,
                        () => _cfg.Group(_cfg.Rule(comp.Primary?.ViewSetKey)?.AlignmentGroupKey ?? "")?.Title ?? _cfg.Title, Changed));
                }
                _selectedTitleScope = TitleScopes.FirstOrDefault(t => t.Scope == keep) ?? TitleScopes.FirstOrDefault();
                OnPropertyChanged(nameof(SelectedTitleScope));
                PickReferenceTitleCommand?.Refresh();
            }
        }

        /// <summary>The planner may migrate a format-1 configuration (compositions, profiles): reload the editors when it did.</summary>
        private void SyncEditorsAfterPlan()
        {
            if (_cfg.Compositions.Count != Compositions.Count || _cfg.NumberingProfiles.Count != NumberingProfiles.Count) LoadEditors();
        }

        private void RebuildRowTitle()
        {
            var row = _selectedRow?.Row;
            if (row == null || row.PlaceAction == PlaceAction.None && row.TitleAction == TitleAction.None && string.IsNullOrEmpty(row.ExistingViewportUniqueId)) { RowTitle = null; return; }
            string key = row.RowKey;
            var comp = _cfg.Composition(row.CompositionKey);
            RowTitle = new TitlePlacementItem("This viewport", () => _cfg.Override(key)?.Title, v => _cfg.EnsureOverride(key).Title = v, true,
                                              () => PlanBuilder.ResolveTitle(_cfg, row.GroupKey, comp, null), Rebuild);
        }

        /// <summary>Previews of the visible Build Name editors with real rows of the plan (first rows of the rule).</summary>
        private void RefreshBuilderPreviews(SetupPlan plan)
        {
            IEnumerable<PreviewLine> Lines(IEnumerable<PlanRow> rows, NameContext t) => rows.Take(8).Select(r =>
            {
                r.Names.TryGetValue(t, out NameResult n);
                string label = NameFields.TargetLabel(t);
                var err = n?.Errors.FirstOrDefault() ?? r.Errors.FirstOrDefault(e => e.StartsWith(label, StringComparison.Ordinal) || e.StartsWith("Circular", StringComparison.Ordinal)) ?? "";
                return new PreviewLine
                {
                    Row = r.LevelName + " · " + r.ViewSetName + (string.IsNullOrEmpty(r.CompositionName) ? "" : " · " + r.CompositionName),
                    Result = n == null ? "" : !n.Produced ? "(not written)" : n.Text,
                    Breakdown = n?.Breakdown ?? "",
                    Error = err
                };
            });
            var key = SelectedSet?.Rule.Key;
            var setRows = plan.Rows.Where(r => r.RuleKey == key).ToList();
            ViewNameBuilder?.SetPreview(Lines(setRows, NameContext.ViewName));
            TitleBuilder?.SetPreview(Lines(setRows, NameContext.TitleOnSheet));
            SheetNameBuilder?.SetPreview(Lines(setRows.Where(r => r.IsSheetPrimary), NameContext.SheetName));
            if (SelectedNumbering != null)
                SelectedNumbering.Builder.SetPreview(Lines(plan.Rows.Where(r => r.IsSheetPrimary && r.NumberingProfileKey == SelectedNumbering.Key), NameContext.SheetNumber));
            if (SelectedComposition?.SheetNameBuilder != null)
                SelectedComposition.SheetNameBuilder.SetPreview(Lines(plan.Rows.Where(r => r.IsSheetPrimary && r.CompositionKey == SelectedComposition.Composition.Key), NameContext.SheetName));
            foreach (var vs in ViewSets) vs.Composition = _cfg.CompositionOf(vs.Rule.Key)?.Name ?? "";
            foreach (var n in NumberingProfiles)
            {
                var users = _cfg.ViewSets.Where(v => v.NumberingProfileKey == n.Key).Select(v => "View Set '" + v.Name + "'")
                    .Concat(_cfg.Compositions.Where(c => c.NumberingProfileKey == n.Key).Select(c => "composition '" + c.Name + "'")).ToList();
                int sheets = plan.Rows.Count(r => r.IsSheetPrimary && r.NumberingProfileKey == n.Key);
                n.Usage = (users.Count == 0 ? "Not assigned." : "Used by " + string.Join(", ", users) + ".") + " " + sheets + " sheet(s) in the current plan.";
            }
        }

        // ------------------------------------------------------------------ parameter values on demand

        private async Task EnsureValuesAsync(ParamRef p)
        {
            if (p == null || (p.Scope != ParamScope.View && p.Scope != ParamScope.Sheet && p.Scope != ParamScope.TitleBlockInstance)) return;
            if (_cat.ValuesRead.Contains(p.Key)) return;
            ParamValueSet set = null;
            await RunBusy("Reading the values of '" + p.Name + "' (read-only)", async ct => { set = await _gw.ReadParameterValues(new[] { p }); });
            if (set == null) return;
            _cat.Merge(set);
            Status = "Values of '" + p.Name + "' read from " + (set.Views.Count + set.Sheets.Count) + " element(s).";
            Changed();
        }

        // ------------------------------------------------------------------ numbering profiles

        private void AddNumbering()
        {
            var p = new NumberingProfile
            {
                Name = SetupMigration.UniqueName(_cfg, "Numbering"),
                Rule = NameRule.Of(NameComponent.OfField(NameField.Discipline), NameComponent.OfField(NameField.Sequence, "-")),
                Start = 101
            };
            p.Rule.Components[1].PadDigits = 3;
            p.GroupBy.Add(NameField.Discipline);
            _cfg.NumberingProfiles.Add(p);
            var item = NewNumberingItem(p);
            NumberingProfiles.Add(item);
            SelectedNumbering = item;
            Changed();
        }

        private void DuplicateNumbering()
        {
            var c = SelectedNumbering.Profile.Clone();
            c.Key = "num-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            c.Name = SetupMigration.UniqueName(_cfg, SelectedNumbering.Name + " (copy)");
            c.DerivedFromLegacy = false;
            c.CounterKey = string.Empty;
            _cfg.NumberingProfiles.Add(c);
            var item = NewNumberingItem(c);
            NumberingProfiles.Add(item);
            SelectedNumbering = item;
            Changed();
        }

        private void RemoveNumbering()
        {
            var p = SelectedNumbering.Profile;
            bool used = _cfg.ViewSets.Any(v => v.NumberingProfileKey == p.Key) || _cfg.Compositions.Any(c => c.NumberingProfileKey == p.Key) || _cfg.Overrides.Any(o => o.NumberingProfileKey == p.Key);
            if (used) { _dialogs.Info("Remove numbering profile", "'" + p.Name + "' is assigned to View Sets, compositions or sheets. Assign another profile to them first."); return; }
            if (!_dialogs.Confirm("Remove numbering profile", "Remove '" + p.Name + "'? Sheet numbers already in the model are not changed.")) return;
            _cfg.NumberingProfiles.Remove(p);
            NumberingProfiles.Remove(SelectedNumbering);
            SelectedNumbering = NumberingProfiles.FirstOrDefault();
            Changed();
        }

        private void ImportNumbering()
        {
            string f = _dialogs.PickOpenProfile();
            if (string.IsNullOrEmpty(f)) return;
            var added = NumberingProfileFile.Read(f, _cfg);
            foreach (var p in added) NumberingProfiles.Add(NewNumberingItem(p));
            SelectedNumbering = NumberingProfiles.LastOrDefault();
            Status = added.Count + " numbering profile(s) imported (new keys; existing profiles are not replaced).";
            Changed();
        }

        private void ExportNumbering()
        {
            string f = _dialogs.PickSaveProfile(SelectedNumbering.Name + ".cfnumbering.json");
            if (string.IsNullOrEmpty(f)) return;
            NumberingProfileFile.Write(f, new[] { SelectedNumbering.Profile });
            Status = "Numbering profile exported to " + f;
        }

        // ------------------------------------------------------------------ renumbering

        /// <summary>Sheets whose number differs from the rule's proposal (rule/prefix changed) – kept until approved.</summary>
        public void RefreshPendingRenumbers()
        {
            var probe = SetupJson.Clone(_cfg);
            probe.Mode = SetupMode.FullSetup;
            var plan = PlanBuilder.Build(probe, _cat, _gw.DocumentKey);
            PendingRenumbers.Clear();
            foreach (var r in plan.Rows.Where(r => r.IsSheetPrimary && r.RenumberPending))
                PendingRenumbers.Add(new RenumberItem { SheetKey = r.SheetKey, Sheet = r.SheetName, Current = r.SheetNumber, Proposed = r.ProposedSheetNumber, Exists = true });
        }

        private void ApprovePending()
        {
            int n = 0;
            foreach (var i in PendingRenumbers.Where(i => i.Include)) { _cfg.ApprovedSheetNumbers[i.SheetKey] = i.Proposed; n++; }
            Status = n + " renumbering(s) approved. They are written by Run Setup (one Undo).";
            RefreshPendingRenumbers();
            Changed();
        }

        /// <summary>Explicit renumbering preview: sequences assigned again in order (all sheets or the selected profile).</summary>
        public void PreviewRenumber()
        {
            var probe = SetupJson.Clone(_cfg);
            probe.Mode = SetupMode.FullSetup;
            var before = PlanBuilder.Build(probe, _cat, _gw.DocumentKey);
            var sheets = before.Rows.Where(r => r.IsSheetPrimary && !string.IsNullOrEmpty(r.SequenceScopeKey) && (!RenumberSelectedOnly || r.NumberingProfileKey == SelectedNumbering?.Key)).ToList();
            var regen = SetupJson.Clone(probe);
            PlanBuilder.RegenerateSequences(regen, sheets.Select(r => r.SheetKey));
            var after = PlanBuilder.Build(regen, _cat, _gw.DocumentKey);
            Renumbers.Clear();
            foreach (var r in sheets)
            {
                var a = after.Rows.FirstOrDefault(x => x.SheetKey == r.SheetKey && x.IsSheetPrimary);
                if (a == null) continue;
                string current = string.IsNullOrEmpty(r.CurrentSheetNumber) ? r.SheetNumber : r.CurrentSheetNumber;
                if (current == a.ProposedSheetNumber && r.Sequence == a.Sequence) continue;
                Renumbers.Add(new RenumberItem
                {
                    SheetKey = r.SheetKey, Sheet = r.SheetName, Current = current, Proposed = a.ProposedSheetNumber,
                    NewSequence = regen.SequenceAssignments.TryGetValue(r.SheetKey, out int seq) ? seq : (int?)null,
                    Scope = regen.SequenceScopes.TryGetValue(r.SheetKey, out string sc) ? sc : "", Exists = !string.IsNullOrEmpty(r.CurrentSheetNumber)
                });
            }
            Status = Renumbers.Count == 0 ? "Renumbering would change nothing." : Renumbers.Count + " sheet(s) would be renumbered. Review the list and apply the selected ones.";
        }

        private void ApplyRenumber()
        {
            var chosen = Renumbers.Where(i => i.Include && i.NewSequence.HasValue).ToList();
            if (chosen.Count == 0) return;
            if (!_dialogs.Confirm("Renumber", "Apply the new numbers to " + chosen.Count + " sheet(s)? Nothing is written to the model until Run Setup.")) return;
            foreach (var i in chosen)
            {
                _cfg.SequenceAssignments[i.SheetKey] = i.NewSequence.Value;
                _cfg.SequenceScopes[i.SheetKey] = i.Scope;
                if (i.Exists) _cfg.ApprovedSheetNumbers[i.SheetKey] = i.Proposed;
            }
            Renumbers.Clear();
            Status = chosen.Count + " sheet(s) renumbered in the proposal. Check the Review for conflicts before Run Setup.";
            RefreshPendingRenumbers();
            Changed();
        }

        // ------------------------------------------------------------------ compositions

        private void AddComposition()
        {
            var c = new SheetComposition { Name = "Plan + RCP", Mode = CompositionMode.Overlay };
            var free = _cfg.ViewSets.Where(v => _cfg.CompositionOf(v.Key) == null).ToList();
            var primary = SelectedSet != null && _cfg.CompositionOf(SelectedSet.Rule.Key) == null ? SelectedSet.Rule : free.FirstOrDefault();
            if (primary != null)
            {
                var m = new CompositionMember { ViewSetKey = primary.Key };
                c.Members.Add(m);
                c.PrimaryMemberId = m.Id;
                c.Name = primary.Name + " + …";
            }
            _cfg.Compositions.Add(c);
            var item = NewCompositionItem(c);
            Compositions.Add(item);
            SelectedComposition = item;
            Changed();
        }

        private void DuplicateComposition()
        {
            var c = SelectedComposition.Composition.Clone();
            c.Name += " (copy)";
            c.Enabled = false; // a View Set can be in one enabled composition only
            _cfg.Compositions.Add(c);
            var item = NewCompositionItem(c);
            Compositions.Add(item);
            SelectedComposition = item;
            Status = "Copy created disabled: a View Set can belong to one enabled composition. Change its members before enabling it.";
            Changed();
        }

        private void RemoveComposition()
        {
            if (!_dialogs.Confirm("Remove composition", "Remove '" + SelectedComposition.Name + "' from the configuration? Sheets, views and viewports already in the model are not deleted; its View Sets go back to their own sheets.")) return;
            _cfg.Compositions.Remove(SelectedComposition.Composition);
            Compositions.Remove(SelectedComposition);
            SelectedComposition = Compositions.FirstOrDefault();
            Changed();
        }

        private void AddMember(bool existing)
        {
            var comp = SelectedComposition.Composition;
            var m = new CompositionMember();
            if (existing)
            {
                var v = ExistingPlanViews.FirstOrDefault(x => string.IsNullOrEmpty(x.PlacedOnSheet)) ?? ExistingPlanViews.FirstOrDefault();
                m.ExistingViewUniqueId = v?.UniqueId ?? "";
            }
            else
            {
                var free = _cfg.ViewSets.FirstOrDefault(v => _cfg.CompositionOf(v.Key) == null && comp.Members.All(x => x.ViewSetKey != v.Key))
                           ?? _cfg.ViewSets.FirstOrDefault(v => comp.Members.All(x => x.ViewSetKey != v.Key));
                if (free == null) { _dialogs.Info("Views on Sheet", "Every View Set is already a member. Add a View Set first (step View Sets)."); return; }
                m.ViewSetKey = free.Key;
            }
            comp.Members.Add(m);
            if (string.IsNullOrEmpty(comp.PrimaryMemberId) && !existing) comp.PrimaryMemberId = m.Id;
            SelectedComposition.Members.Add(new MemberItem(m, SelectedComposition));
            SelectedComposition.RefreshPrimary();
            Changed();
        }

        private void RemoveMember(MemberItem m)
        {
            if (m == null || SelectedComposition == null) return;
            var comp = SelectedComposition.Composition;
            comp.Members.Remove(m.Member);
            SelectedComposition.Members.Remove(m);
            if (comp.PrimaryMemberId == m.Member.Id) comp.PrimaryMemberId = comp.Members.FirstOrDefault(x => !x.IsExistingView)?.Id ?? "";
            SelectedComposition.RefreshPrimary();
            Status = "Member removed from the composition. Its view/viewport in the model is not deleted.";
            Changed();
        }

        // ------------------------------------------------------------------ titles

        private async Task PickReferenceTitle()
        {
            var scope = SelectedTitleScope;
            if (scope == null) return;
            if (!scope.IsEditable) { _dialogs.Info("Match reference title", "Tick 'Override' for " + scope.Scope + " first (it inherits its placement now)."); return; }
            TitlePick pick = null;
            Status = "In Revit: open the reference sheet and pick the viewport whose title is correctly placed.";
            await RunBusy("Waiting for the viewport pick in Revit", async ct => { pick = await _gw.PickReferenceTitle(scope.Anchor); });
            if (pick == null) return;
            if (!pick.Ok) { Status = pick.Message; _dialogs.Info("Match reference title", pick.Message); return; }
            scope.ApplyCapture(pick);
            Status = "Title placement of " + scope.Scope + " taken from " + pick.Description + " (" + scope.CaptureKind + ").";
            Changed();
        }

        private void AddTitlePreview(SheetPreviewData d, List<PlanRow> sameSheet)
        {
            const double w = 70, h = 9; // nominal title size for drawing only
            foreach (var r in sameSheet)
            {
                var vp = _cat.Viewports.FirstOrDefault(x => x.UniqueId == r.ExistingViewportUniqueId);
                if (vp?.BoxMm != null) d.ExistingViewports.Add((r.ViewName, vp.BoxMm, vp.LabelMm));
                if (r.TitleAction != TitleAction.Apply || r.Title == null) continue;
                var t = r.Title;
                Geometry.P2? corner = null;
                if (t.IsFixed && t.Frame == SheetFrame.Sheet) corner = new Geometry.P2(t.FixedXmm + r.TitleExtraXmm, t.FixedYmm + r.TitleExtraYmm);
                else if (!t.IsFixed && vp?.BoxMm != null && vp.Rotation == 0)
                {
                    var box = Geometry.Box2.Of(vp.BoxMm);
                    if (t.RelativeTo == TitleRelativeTo.OverlayEnvelope && !string.IsNullOrEmpty(r.OverlayGroupId))
                    {
                        var all = sameSheet.Where(x => x.OverlayGroupId == r.OverlayGroupId).Select(x => _cat.Viewports.FirstOrDefault(v => v.UniqueId == x.ExistingViewportUniqueId)?.BoxMm).Where(b => b != null).Select(Geometry.Box2.Of).ToList();
                        if (all.Count > 0) box = Geometry.Box2.Union(all);
                    }
                    corner = Geometry.TitleGeometry.Corner(box, t.Anchor) + new Geometry.P2(t.OffsetXmm + r.TitleExtraXmm, t.OffsetYmm + r.TitleExtraYmm);
                }
                if (corner.HasValue)
                {
                    var est = Geometry.TitleGeometry.EstimatedLabel(corner.Value, t.Anchor, w, h);
                    d.EstimatedLabels.Add(("title " + r.ViewName, est.ToArray()));
                    var usable = new Geometry.Box2(_cfg.UsableLeftMm, _cfg.UsableBottomMm, _cfg.UsableRightMm, _cfg.UsableTopMm);
                    if (!est.Inside(usable)) d.Notes.Add("Title of '" + r.ViewName + "' would be outside the usable area (estimate with a nominal title size).");
                }
            }
            if (d.EstimatedLabels.Count > 0) d.Notes.Add("Dashed titles: estimated position (nominal size). The real title size depends on its family and is measured in Revit after the run.");
            else if (sameSheet.Any(r => r.TitleAction == TitleAction.Apply)) d.Notes.Add("Title positions relative to a viewport that does not exist yet are known only after the run.");
        }
    }
}
