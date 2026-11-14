using OfflineDaoc.Updates;

namespace OfflineDaoc.Launcher;

// Living Server release notes in a Changelog tab (GitHub release bodies via ReleaseFeed).
internal sealed partial class MainForm
{
    private ChangelogControl? _changelog;

    private void AddChangelogTab(TabControl tabs)
    {
        if (LauncherBuild.Version == null)
            return;

        var changelog = new TabPage("Changelog") { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text };
        _changelog = new ChangelogControl(() => Updater);
        changelog.Controls.Add(_changelog);
        changelog.Enter += async (_, _) => await _changelog.RefreshAsync();
        tabs.TabPages.Add(changelog);

        _allowPrereleasesCheck.CheckedChanged += (_, _) =>
        {
            if (_changelog != null && tabs.SelectedTab == changelog && _changelog.Visible)
                _ = _changelog.RefreshAsync();
        };
    }
}