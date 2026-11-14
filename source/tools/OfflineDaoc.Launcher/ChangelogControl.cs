using System.Globalization;
using OfflineDaoc.Updates;

namespace OfflineDaoc.Launcher;

internal sealed class ChangelogControl : UserControl
{
    private readonly Func<LauncherUpdater> _updater;
    private readonly ListBox _releases = new() { IntegralHeight = false, HorizontalScrollbar = true };
    private readonly TextBox _body = new()
    {
        ReadOnly = true,
        Multiline = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Vertical,
        BackColor = DaocTheme.StoneDark,
        ForeColor = DaocTheme.Text,
        BorderStyle = BorderStyle.None,
    };
    private readonly Label _status = new() { AutoSize = true, ForeColor = DaocTheme.Muted };
    private readonly Button _refresh = new() { Text = "REFRESH", AutoSize = true };
    private int _request;
    private IReadOnlyList<ReleaseInfo> _items = Array.Empty<ReleaseInfo>();

    internal ChangelogControl(Func<LauncherUpdater> updater)
    {
        _updater = updater;
        Dock = DockStyle.Fill;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.Text;
        Font = new Font("Georgia", 9);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(8),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        header.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "LIVING SERVER RELEASE NOTES",
            Font = new Font("Georgia", 11f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
            Margin = new Padding(0, 2, 12, 0),
        });
        header.Controls.Add(_refresh);
        layout.SetColumnSpan(header, 2);
        layout.Controls.Add(header, 0, 0);

        _releases.Dock = DockStyle.Fill;
        _releases.BackColor = DaocTheme.StoneDark;
        _releases.ForeColor = DaocTheme.Text;
        _releases.BorderStyle = BorderStyle.None;
        _releases.SelectedIndexChanged += (_, _) => ShowSelectedNotes();
        layout.Controls.Add(_releases, 0, 1);

        var notes = new InsetPanel { Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0), Accent = DaocTheme.Gold, Padding = new Padding(10) };
        notes.Controls.Add(_body);
        layout.Controls.Add(notes, 1, 1);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        footer.Controls.Add(_status);
        layout.SetColumnSpan(footer, 2);
        layout.Controls.Add(footer, 0, 2);

        Controls.Add(layout);
        _refresh.Click += async (_, _) => await RefreshAsync();
        _status.Text = "Open this tab or choose Refresh to load release notes from GitHub.";
    }

    internal async Task RefreshAsync()
    {
        int request = ++_request;
        LauncherUpdater updater = _updater();
        bool allowPrereleases = updater.Settings.AllowPrereleases;
        ReleaseFeed.Configure(updater.Settings.ReleaseRepository);
        _refresh.Enabled = false;
        _status.Text = "Loading release notes from GitHub…";
        string? problem = null;
        IReadOnlyList<ReleaseInfo> list = Array.Empty<ReleaseInfo>();
        bool fromCache = false;
        try
        {
            list = await Task.Run(() =>
            {
                IReadOnlyList<ReleaseInfo> fetched = ReleaseFeed.TryGetReleaseHistory(updater.Web, allowPrereleases, out string? json, out problem);
                if (fetched.Count > 0 && json != null)
                    ReleaseNotesCache.Save(updater.UpdatesFolder, json);
                return fetched;
            });
            if (list.Count == 0)
            {
                string? cached = ReleaseNotesCache.TryRead(updater.UpdatesFolder);
                if (!string.IsNullOrWhiteSpace(cached))
                {
                    list = ReleaseFeed.VisibleReleases(ReleaseInfo.ParseList(cached), allowPrereleases);
                    fromCache = list.Count > 0;
                }
            }
        }
        catch (Exception ex)
        {
            problem = ex.Message;
        }

        if (IsDisposed || request != _request)
            return;

        _items = list;
        _releases.BeginUpdate();
        _releases.Items.Clear();
        foreach (ReleaseInfo release in _items)
            _releases.Items.Add(FormatListLine(release));
        _releases.EndUpdate();
        if (_releases.Items.Count > 0)
            _releases.SelectedIndex = 0;
        else
            _body.Text = "";

        if (_items.Count > 0)
        {
            string where = fromCache ? " (last saved notes; GitHub wasn't reached)" : " from GitHub";
            string repo = ReleaseFeed.Repository;
            _status.Text = $"{_items.Count} release{(_items.Count == 1 ? "" : "s")}{where} · {repo}"
                + (allowPrereleases ? " · prereleases shown" : "");
        }
        else
        {
            _status.Text = problem == null
                ? "No Living Server releases were listed."
                : "Could not load release notes: " + problem + ". Check \"Allow prereleases\" or try Refresh.";
        }

        _refresh.Enabled = true;
    }

    private void ShowSelectedNotes()
    {
        int index = _releases.SelectedIndex;
        if (index < 0 || index >= _items.Count)
        {
            _body.Text = "";
            return;
        }
        ReleaseInfo release = _items[index];
        string title = release.Title?.Trim() ?? release.Version?.DisplayName ?? release.TagName ?? "";
        string text = release.BodyPlainText();
        _body.Text = title.Length == 0 ? text : title + Environment.NewLine + Environment.NewLine + text;
    }

    private static string FormatListLine(ReleaseInfo release)
    {
        string name = release.Version?.DisplayName ?? release.TagName ?? "";
        string when = "";
        if (release.PublishedAtUtc is DateTime published)
            when = " · " + published.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
        string flag = release.Prerelease ? " · prerelease" : "";
        return name + when + flag;
    }
}