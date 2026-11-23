using System.Text;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Issue #69: Tools → Character Builder. A planner for a class's creation stats, its stats at a level, its spec points
/// and what each line unlocks (styles, spells, abilities) at the levels chosen. The rules are in CharacterBuilder; the
/// lines and unlocks come from the world database, read-only, so it works with the server running or stopped. It never
/// creates or changes a character. Plans are saved as JSON in runtime\character-builds.
/// </summary>
internal sealed class CharacterBuilderControl : UserControl
{
    private readonly string _database;
    private readonly string _runtimeRoot;
    private readonly bool _allowSluaghbinder;
    private readonly Dictionary<int, BuilderClassData?> _cache = new();

    private readonly ComboBox _realm = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly ComboBox _class = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly ComboBox _race = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly NumericUpDown _level = new() { Minimum = 1, Maximum = CharacterBuilder.MaxLevel, Value = CharacterBuilder.MaxLevel, Width = 60 };
    private readonly CheckBox _half = new() { Text = "and a half (x.5)", AutoSize = true };
    private readonly Dictionary<BuilderStat, NumericUpDown> _points = new();
    private readonly Dictionary<BuilderStat, Label> _statLabel = new(), _statRace = new(), _statLevels = new(), _statTotal = new();
    private readonly Label _creationLeft = new() { AutoSize = true, ForeColor = DaocTheme.GoldLight };
    private readonly Label _classInfo = new() { AutoSize = true, ForeColor = DaocTheme.Muted, MaximumSize = new Size(330, 0) };
    private readonly DataGridView _specs = new();
    private readonly Label _specLeft = new() { AutoSize = true, ForeColor = DaocTheme.GoldLight };
    private readonly DataGridView _unlocks = new();
    private readonly CheckBox _onlyReached = new() { Text = "Only what this build has", AutoSize = true };
    private readonly CheckBox _allLines = new() { Text = "Every line", AutoSize = true };
    private readonly Label _unlockTitle = new() { AutoSize = true, ForeColor = DaocTheme.GoldLight, Font = new Font("Georgia", 9.5f, FontStyle.Bold) };
    private readonly TextBox _name = new() { Width = 180, PlaceholderText = "Build name" };
    private readonly ComboBox _saved = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly Button _save = new() { Text = "SAVE BUILD", AutoSize = true };
    private readonly Button _load = new() { Text = "LOAD", AutoSize = true };
    private readonly Button _reset = new() { Text = "RESET", AutoSize = true };
    private readonly Button _copy = new() { Text = "COPY SUMMARY", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, ForeColor = DaocTheme.Muted };

    private readonly Dictionary<string, int> _specLevels = new(StringComparer.Ordinal);
    private BuilderClassData? _data;
    private bool _updating;
    private bool _suppressClassEvent;
    private int _loadRequest;

    internal CharacterBuilderControl(string database, string runtimeRoot, bool allowSluaghbinder)
    {
        _database = database;
        _runtimeRoot = runtimeRoot;
        _allowSluaghbinder = allowSluaghbinder;
        Dock = DockStyle.Fill;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.Text;
        Font = new Font("Georgia", 9);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(BuildChoices(), 0, 0);
        layout.Controls.Add(BuildSpecs(), 1, 0);
        layout.Controls.Add(BuildUnlocks(), 2, 0);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        footer.Controls.AddRange([_name, _save, _saved, _load, _reset, _copy, _status]);
        layout.Controls.Add(footer, 0, 1);
        layout.SetColumnSpan(footer, 3);
        Controls.Add(layout);

        foreach (int realm in new[] { 1, 2, 3 })
            _realm.Items.Add(new Option(CharacterBuilder.RealmNames[realm], realm));
        _realm.SelectedIndexChanged += (_, _) => { if (!_suppressClassEvent) FillClasses(); };
        _class.SelectedIndexChanged += async (_, _) => { if (!_suppressClassEvent) await ClassChangedAsync(); };
        _race.SelectedIndexChanged += (_, _) => Recalculate();
        _level.ValueChanged += (_, _) => { _half.Enabled = _level.Value >= 40 && _level.Value < CharacterBuilder.MaxLevel; Recalculate(); };
        _half.CheckedChanged += (_, _) => Recalculate();
        _onlyReached.CheckedChanged += (_, _) => ShowUnlocks();
        _allLines.CheckedChanged += (_, _) => ShowUnlocks();
        _specs.SelectionChanged += (_, _) => ShowUnlocks();
        _specs.CellValueChanged += (_, e) => SpecEdited(e.RowIndex);
        _specs.DataError += (_, e) => e.ThrowException = false;
        _save.Click += (_, _) => SaveBuild();
        _load.Click += async (_, _) => await LoadBuildAsync();
        _reset.Click += (_, _) => ResetBuild();
        _copy.Click += (_, _) => CopySummary();

        _half.Enabled = false;
        RefreshSavedList();
        _status.Text = "A planner: nothing here changes a character.";
        _realm.SelectedIndex = 0;
    }

    private Control BuildChoices()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true };
        var picks = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
        void Row(string label, Control control)
        {
            picks.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = DaocTheme.GoldLight, Margin = new Padding(3, 7, 3, 3) });
            picks.Controls.Add(control);
        }
        Row("Realm", _realm);
        Row("Class", _class);
        Row("Race", _race);
        var level = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        level.Controls.AddRange([_level, _half]);
        Row("Level", level);
        panel.Controls.Add(picks);
        panel.Controls.Add(_classInfo);

        var stats = new TableLayoutPanel { AutoSize = true, ColumnCount = 5, Dock = DockStyle.Top, Margin = new Padding(3, 10, 3, 3) };
        foreach (string header in new[] { "Stat", "Race", "Points", "Levels", "Total" })
            stats.Controls.Add(new Label { Text = header, AutoSize = true, ForeColor = DaocTheme.GoldLight, Font = new Font("Georgia", 9, FontStyle.Bold) });
        foreach (BuilderStat stat in CharacterBuilder.Stats)
        {
            _statLabel[stat] = new Label { Text = stat.ToString(), AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            _statRace[stat] = new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            _points[stat] = new NumericUpDown { Minimum = 0, Maximum = 30, Width = 50 };
            _points[stat].ValueChanged += (_, _) => Recalculate();
            _statLevels[stat] = new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            _statTotal[stat] = new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 3), Font = new Font("Georgia", 9, FontStyle.Bold) };
            stats.Controls.AddRange([_statLabel[stat], _statRace[stat], _points[stat], _statLevels[stat], _statTotal[stat]]);
        }
        panel.Controls.Add(stats);
        panel.Controls.Add(_creationLeft);
        panel.Controls.Add(new Label
        {
            AutoSize = true, ForeColor = DaocTheme.Muted, MaximumSize = new Size(330, 0), Margin = new Padding(3, 8, 3, 3),
            Text = "Creation points: each of the first 10 points in a stat costs 1, the next 5 cost 2, beyond that 3. " +
                   "From level 6 the class's 1st stat rises every level, its 2nd every other level and its 3rd every third level.",
        });
        return panel;
    }

    private Control BuildSpecs()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = "Specializations", AutoSize = true, ForeColor = DaocTheme.GoldLight, Font = new Font("Georgia", 9.5f, FontStyle.Bold) }, 0, 0);
        StyleGrid(_specs);
        _specs.ReadOnly = false;
        _specs.Columns.Add(new DataGridViewTextBoxColumn { Name = "Line", HeaderText = "Line", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        _specs.Columns.Add(new DataGridViewTextBoxColumn { Name = "From", HeaderText = "From lvl", ReadOnly = true, Width = 62, SortMode = DataGridViewColumnSortMode.NotSortable });
        _specs.Columns.Add(new DataGridViewTextBoxColumn { Name = "Level", HeaderText = "Spec", Width = 52, ValueType = typeof(int), SortMode = DataGridViewColumnSortMode.NotSortable });
        _specs.Columns.Add(new DataGridViewTextBoxColumn { Name = "Points", HeaderText = "Points", ReadOnly = true, Width = 58, SortMode = DataGridViewColumnSortMode.NotSortable });
        panel.Controls.Add(_specs, 0, 1);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        footer.Controls.Add(_specLeft);
        footer.Controls.Add(new Label
        {
            AutoSize = true, ForeColor = DaocTheme.Muted, MaximumSize = new Size(380, 0),
            Text = "Type a level in the Spec column. Raising a line to N costs N points, and a line goes up to your level. " +
                   "Autotrained lines (marked AT) get level/4 for free.",
        });
        panel.Controls.Add(footer, 0, 2);
        return panel;
    }

    private Control BuildUnlocks()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(_unlockTitle, 0, 0);
        var filters = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        filters.Controls.AddRange([_allLines, _onlyReached]);
        panel.Controls.Add(filters, 0, 1);
        StyleGrid(_unlocks);
        _unlocks.ReadOnly = true;
        foreach (var (name, header, width) in new[] { ("Line", "Line", 110), ("At", "At", 70), ("Kind", "Type", 60), ("Name", "Name", 0), ("Detail", "Detail", 170) })
        {
            var column = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, SortMode = DataGridViewColumnSortMode.NotSortable };
            if (width == 0) { column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; column.MinimumWidth = 120; }
            else column.Width = width;
            _unlocks.Columns.Add(column);
        }
        panel.Controls.Add(_unlocks, 0, 2);
        return panel;
    }

    private static void StyleGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.BackgroundColor = DaocTheme.StoneDark;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.GoldLight };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = DaocTheme.StoneDark, ForeColor = DaocTheme.Text,
            SelectionBackColor = Color.FromArgb(90, 65, 32), SelectionForeColor = Color.White,
        };
    }

    private BuilderClass? SelectedClass => _class.SelectedItem is Option option ? CharacterBuilder.FindClass(option.Id) : null;
    private int SelectedRace => _race.SelectedItem is Option option ? option.Id : 0;
    private int Level => (int)_level.Value;
    private bool HalfLevel => _half.Enabled && _half.Checked;

    private void FillClasses()
    {
        if (_realm.SelectedItem is not Option realm)
            return;
        _class.Items.Clear();
        foreach (BuilderClass cls in CharacterBuilder.ClassesForRealm(realm.Id, _allowSluaghbinder))
            _class.Items.Add(new Option(cls.Name, cls.Id));
        _class.SelectedIndex = _class.Items.Count == 0 ? -1 : 0;
    }

    private async Task ClassChangedAsync(CharacterBuildPlan? plan = null)
    {
        BuilderClass? cls = SelectedClass;
        if (cls == null)
            return;
        _updating = true;
        try
        {
            _specs.Rows.Clear();
            int keepRace = plan?.RaceId ?? SelectedRace;
            _race.Items.Clear();
            foreach (var (id, name) in cls.Races)
                _race.Items.Add(new Option(name, id));
            int raceIndex = _race.Items.Cast<Option>().ToList().FindIndex(o => o.Id == keepRace);
            _race.SelectedIndex = _race.Items.Count == 0 ? -1 : Math.Max(0, raceIndex);
            foreach (BuilderStat stat in CharacterBuilder.Stats)
                _points[stat].Value = 0;
            _specLevels.Clear();
            if (plan != null)
            {
                _level.Value = Math.Clamp(plan.Level, 1, CharacterBuilder.MaxLevel);
                _half.Checked = plan.HalfLevel;
                foreach (BuilderStat stat in CharacterBuilder.Stats)
                    if (plan.CreationPoints.TryGetValue(stat.ToString(), out int value))
                        _points[stat].Value = Math.Clamp(value, 0, 30);
                foreach (var entry in plan.SpecLevels)
                    _specLevels[entry.Key] = entry.Value;
            }
        }
        finally
        {
            _updating = false;
        }

        _classInfo.Text = $"Spec points x{cls.SpecMultiplier / 10.0:0.0} per level · stats from level 6: {cls.Primary}, {cls.Secondary}, {cls.Tertiary}" +
            (cls.ManaStat != BuilderStat.UNDEFINED ? $" · power from {cls.ManaStat}" : "") +
            (cls.Autotrain.Count > 0 ? $" · autotrains {string.Join(", ", cls.Autotrain)}" : "");

        int request = ++_loadRequest;
        _data = null;
        Recalculate();
        if (!_cache.TryGetValue(cls.Id, out BuilderClassData? data))
        {
            _status.Text = "Reading the class's lines from the world database…";
            try
            {
                data = File.Exists(_database) ? await Task.Run(() => CharacterBuilderDatabase.Load(_database, cls.Id)) : null;
                _status.Text = data == null ? "The world database was not found: lines and unlocks are not shown." : "A planner: nothing here changes a character.";
            }
            catch (Exception ex) when (ex is System.Data.SQLite.SQLiteException or IOException or UnauthorizedAccessException or InvalidCastException)
            {
                data = null;
                _status.Text = "Could not read the class's lines: " + ex.Message;
            }
            _cache[cls.Id] = data;
        }
        if (IsDisposed || request != _loadRequest)
            return;
        _data = data;
        FillSpecs();
        Recalculate();
    }

    private void FillSpecs()
    {
        _updating = true;
        try
        {
            _specs.Rows.Clear();
            if (_data == null || SelectedClass is not BuilderClass cls)
                return;
            foreach (BuilderSpec spec in _data.Specs)
            {
                string name = CharacterBuilder.IsAutotrained(cls, spec.Key) ? spec.Name + " (AT)" : spec.Name;
                int row = _specs.Rows.Add(name, spec.LevelAcquired, _specLevels.TryGetValue(spec.Key, out int level) ? level : 1, 0);
                _specs.Rows[row].Tag = spec;
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void SpecEdited(int rowIndex)
    {
        if (_updating || rowIndex < 0 || rowIndex >= _specs.Rows.Count || _specs.Rows[rowIndex].Tag is not BuilderSpec spec)
            return;
        object? value = _specs.Rows[rowIndex].Cells["Level"].Value;
        _specLevels[spec.Key] = value is int number ? number : int.TryParse(value?.ToString(), out int parsed) ? parsed : 1;
        Recalculate();
    }

    /// <summary>Clamps every line to what the level allows and redraws the totals.</summary>
    private void Recalculate()
    {
        if (_updating || SelectedClass is not BuilderClass cls)
            return;
        _updating = true;
        try
        {
            int level = Level;
            int race = SelectedRace;
            int spent = 0;
            foreach (BuilderStat stat in CharacterBuilder.Stats)
            {
                int points = (int)_points[stat].Value;
                spent += CharacterBuilder.CreationCost(points);
                int raceValue = CharacterBuilder.StartingStat(race, stat);
                int gain = CharacterBuilder.LevelStatGain(cls, stat, level);
                string mark = stat == cls.Primary ? " (1st)" : stat == cls.Secondary ? " (2nd)" : stat == cls.Tertiary ? " (3rd)" : "";
                _statLabel[stat].Text = stat + mark;
                _statLabel[stat].ForeColor = mark.Length > 0 ? DaocTheme.GoldLight : DaocTheme.Text;
                _statRace[stat].Text = raceValue.ToString();
                _statLevels[stat].Text = gain > 0 ? "+" + gain : "";
                _statTotal[stat].Text = (raceValue + points + gain).ToString();
            }
            int left = CharacterBuilder.CreationPointBudget - spent;
            _creationLeft.Text = left >= 0 ? $"Creation points left: {left} of {CharacterBuilder.CreationPointBudget}" : $"Over the creation budget by {-left} points";
            _creationLeft.ForeColor = left >= 0 ? DaocTheme.GoldLight : DaocTheme.Danger;

            int maxSpec = CharacterBuilder.MaxSpecLevel(level);
            int used = 0;
            foreach (DataGridViewRow row in _specs.Rows)
            {
                if (row.Tag is not BuilderSpec spec)
                    continue;
                bool available = level >= spec.LevelAcquired;
                int minimum = CharacterBuilder.IsAutotrained(cls, spec.Key) ? Math.Min(maxSpec, CharacterBuilder.AutotrainLevel(level)) : 1;
                int wanted = _specLevels.TryGetValue(spec.Key, out int stored) ? stored : 1;
                int specLevel = available ? Math.Clamp(wanted, minimum, maxSpec) : 1;
                if (available)
                    _specLevels[spec.Key] = specLevel;
                int cost = available ? CharacterBuilder.SpecPointsSpent(cls, spec.Key, specLevel, level) : 0;
                used += cost;
                row.Cells["Level"].Value = specLevel;
                row.Cells["Level"].ReadOnly = !available;
                row.Cells["Points"].Value = cost;
                row.DefaultCellStyle.ForeColor = available ? DaocTheme.Text : DaocTheme.Muted;
            }
            int total = CharacterBuilder.SpecPointsAt(cls, level, HalfLevel);
            string levelText = HalfLevel ? level + ".5" : level.ToString();
            _specLeft.Text = _data == null
                ? $"Spec points at level {levelText}: {total}"
                : used <= total
                    ? $"Spec points at level {levelText}: {used} used of {total} ({total - used} left)"
                    : $"Spec points at level {levelText}: {used} used of {total}, {used - total} too many";
            _specLeft.ForeColor = used <= total ? DaocTheme.GoldLight : DaocTheme.Danger;
        }
        finally
        {
            _updating = false;
        }
        ShowUnlocks();
    }

    private void ShowUnlocks()
    {
        if (_updating)
            return;
        _unlocks.Rows.Clear();
        if (_data == null)
        {
            _unlockTitle.Text = "Unlocks";
            return;
        }
        BuilderSpec? selected = _specs.CurrentRow?.Tag as BuilderSpec ?? _data.Specs.FirstOrDefault();
        _unlockTitle.Text = _allLines.Checked ? "Unlocks: every line" : selected == null ? "Unlocks" : "Unlocks: " + selected.Name;
        int level = Level;
        var names = _data.Specs.ToDictionary(s => s.Key, s => s.Name);
        IEnumerable<BuilderUnlock> list = _data.Unlocks;
        if (!_allLines.Checked)
            list = list.Where(u => u.SpecKey == selected?.Key);
        foreach (BuilderUnlock unlock in list.OrderBy(u => u.Baseline ? 0 : 1).ThenBy(u => u.Level))
        {
            bool reached = Reached(unlock, level);
            if (_onlyReached.Checked && !reached)
                continue;
            string line = names.TryGetValue(unlock.SpecKey, out string? name) ? name : unlock.SpecKey;
            int row = _unlocks.Rows.Add(line, unlock.Baseline ? $"Level {unlock.Level}" : $"Spec {unlock.Level}", unlock.Kind, unlock.Name, unlock.Detail);
            _unlocks.Rows[row].DefaultCellStyle.ForeColor = reached ? DaocTheme.Text : DaocTheme.Muted;
        }
    }

    private bool Reached(BuilderUnlock unlock, int level)
    {
        BuilderSpec? spec = _data?.Specs.FirstOrDefault(s => s.Key == unlock.SpecKey);
        if (spec == null || level < spec.LevelAcquired)
            return false;
        if (unlock.Baseline)
            return level >= unlock.Level;
        return (_specLevels.TryGetValue(unlock.SpecKey, out int specLevel) ? specLevel : 1) >= unlock.Level;
    }

    private CharacterBuildPlan CurrentPlan() => new()
    {
        Name = string.IsNullOrWhiteSpace(_name.Text) ? $"{SelectedClass?.Name} {Level}" : _name.Text.Trim(),
        ClassId = SelectedClass?.Id ?? 0,
        RaceId = SelectedRace,
        Level = Level,
        HalfLevel = HalfLevel,
        CreationPoints = CharacterBuilder.Stats.Where(s => _points[s].Value > 0).ToDictionary(s => s.ToString(), s => (int)_points[s].Value),
        SpecLevels = _specLevels.Where(e => e.Value > 1).ToDictionary(e => e.Key, e => e.Value),
    };

    private void SaveBuild()
    {
        try
        {
            CharacterBuildPlan plan = CurrentPlan();
            plan.Save(_runtimeRoot);
            _name.Text = plan.Name;
            RefreshSavedList(CharacterBuildPlan.SafeFileName(plan.Name));
            _status.Text = $"Saved \"{plan.Name}\" in runtime\\character-builds.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = "Could not save the build: " + ex.Message;
        }
    }

    private async Task LoadBuildAsync()
    {
        if (_saved.SelectedItem is not string file)
            return;
        CharacterBuildPlan? plan = CharacterBuildPlan.Load(Path.Combine(CharacterBuildPlan.Folder(_runtimeRoot), file + ".json"));
        BuilderClass? cls = plan == null ? null : CharacterBuilder.FindClass(plan.ClassId);
        if (plan == null || cls == null || (cls.Id == 63 && !_allowSluaghbinder))
        {
            _status.Text = $"\"{file}\" could not be read as a build for this game.";
            return;
        }
        _name.Text = plan.Name;
        // Select the plan's realm and class without the combo events loading an empty build, then apply the plan.
        _suppressClassEvent = true;
        try
        {
            _realm.SelectedIndex = cls.Realm - 1;
            _class.Items.Clear();
            foreach (BuilderClass option in CharacterBuilder.ClassesForRealm(cls.Realm, _allowSluaghbinder))
                _class.Items.Add(new Option(option.Name, option.Id));
            _class.SelectedIndex = Math.Max(0, _class.Items.Cast<Option>().ToList().FindIndex(o => o.Id == cls.Id));
        }
        finally
        {
            _suppressClassEvent = false;
        }
        await ClassChangedAsync(plan);
        _status.Text = $"Loaded \"{plan.Name}\".";
    }

    private void ResetBuild()
    {
        _updating = true;
        foreach (BuilderStat stat in CharacterBuilder.Stats)
            _points[stat].Value = 0;
        _specLevels.Clear();
        _updating = false;
        FillSpecs();
        Recalculate();
        _status.Text = "Reset the stats and spec lines.";
    }

    private void RefreshSavedList(string? select = null)
    {
        _saved.Items.Clear();
        foreach (string name in CharacterBuildPlan.List(_runtimeRoot))
            _saved.Items.Add(name);
        if (select != null)
            _saved.SelectedItem = select;
        else if (_saved.Items.Count > 0)
            _saved.SelectedIndex = 0;
        _load.Enabled = _saved.Items.Count > 0;
    }

    /// <summary>The build as text, for notes or a forum post.</summary>
    internal string Summary()
    {
        BuilderClass? cls = SelectedClass;
        if (cls == null)
            return "";
        var text = new StringBuilder();
        string race = (_race.SelectedItem as Option)?.Text ?? "";
        text.AppendLine($"{race} {cls.Name} ({CharacterBuilder.RealmNames[cls.Realm]}), level {(HalfLevel ? Level + ".5" : Level.ToString())}");
        text.AppendLine("Stats: " + string.Join(", ", CharacterBuilder.Stats.Select(s => $"{s} {_statTotal[s].Text}" + (_points[s].Value > 0 ? $" (+{_points[s].Value})" : ""))));
        if (_data != null)
            text.AppendLine("Specs: " + string.Join(", ", _data.Specs.Where(s => Level >= s.LevelAcquired)
                .Select(s => $"{s.Name} {(_specLevels.TryGetValue(s.Key, out int l) ? l : 1)}")));
        text.AppendLine(_specLeft.Text);
        return text.ToString();
    }

    private void CopySummary()
    {
        try
        {
            Clipboard.SetText(Summary());
            _status.Text = "Copied the build summary.";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            _status.Text = "Could not copy: " + ex.Message;
        }
    }

    private sealed record Option(string Text, int Id)
    {
        public override string ToString() => Text;
    }
}
