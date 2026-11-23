namespace OfflineDaoc.Launcher;

// Issue #69: a Tools tab, with subtabs (the Active tab's layout, #70) so later tools sit beside the character builder.
internal sealed partial class MainForm
{
    private void AddToolsTab(TabControl tabs)
    {
        var builder = new TabPage("Character Builder") { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text };
        builder.Controls.Add(new CharacterBuilderControl(_database, _root, InstalledEditionHasCustomClass(_database)));

        var tools = new TabPage("Tools") { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text };
        var subtabs = new DaocTabControl { Dock = DockStyle.Fill, ItemSize = new Size(170, 26) };
        subtabs.TabPages.Add(builder);
        tools.Controls.Add(subtabs);
        tabs.TabPages.Add(tools);
    }
}
