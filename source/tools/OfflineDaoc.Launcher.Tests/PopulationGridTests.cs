using System.Collections;
using System.Reflection;
using NUnit.Framework;
using DOL.GS;

namespace OfflineDaoc.Launcher.Tests;

/// <summary>
/// Regression coverage for Active Population rebind / SelectBot after large batch creates
/// (IndexOutOfRangeException dialogs on Shift/Ctrl + Lv), and for the 1.1.4 empty-grid
/// regression where RebindBotGrid muted ListChanged while SuspendBinding so a populated
/// status payload painted zero rows under the default filters.
/// </summary>
[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class PopulationGridTests
{
    private static readonly Assembly Launcher = Assembly.Load("OfflineDAoC");
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void RebindBotGridAcceptsALargerRosterWithoutThrowing()
    {
        Type main = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
        using Form form = (Form)Activator.CreateInstance(main)!;
        var grid = (DataGridView)main.GetField("_grid", Hidden)!.GetValue(form)!;
        var source = (BindingSource)main.GetField("_botSource", Hidden)!.GetValue(form)!;
        Type botRow = main.GetNestedType("BotRow", BindingFlags.NonPublic)!;

        var first = new List<object>();
        for (int i = 0; i < 75; i++)
            first.Add(MakeBot(botRow, $"Bot{i:000}", "Hibernia", online: true));
        source.DataSource = first;
        source.ResetBindings(false);
        source.Position = first.Count - 1;
        Assert.That(source.Count, Is.EqualTo(75));

        var grown = new List<object>(first);
        for (int i = 75; i < 85; i++)
            grown.Add(MakeBot(botRow, $"Bot{i:000}", "Hibernia", online: true));

        main.GetMethod("RebindBotGrid", Hidden)!.Invoke(form, [grown]);
        Assert.That(source.Count, Is.EqualTo(85));
        Assert.That(grid.RowCount, Is.EqualTo(85));

        main.GetMethod("SelectBot", Hidden)!.Invoke(form, ["Bot084"]);
        Assert.That(source.Position, Is.EqualTo(84));
        Assert.That(((dynamic)source.Current).Name, Is.EqualTo("Bot084"));
    }

    [Test]
    public void DefaultFiltersShowRowsFromAPopulatedStatusPayload()
    {
        Type main = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
        using Form form = (Form)Activator.CreateInstance(main)!;
        var grid = (DataGridView)main.GetField("_grid", Hidden)!.GetValue(form)!;
        var source = (BindingSource)main.GetField("_botSource", Hidden)!.GetValue(form)!;
        var bots = (IList)main.GetField("_bots", Hidden)!.GetValue(form)!;
        var realmFilter = (ComboBox)main.GetField("_realmFilter", Hidden)!.GetValue(form)!;
        var onlineOnly = (CheckBox)main.GetField("_onlineOnly", Hidden)!.GetValue(form)!;
        var createSpec = (ComboBox)main.GetField("_createSpec", Hidden)!.GetValue(form)!;
        Type botRow = main.GetNestedType("BotRow", BindingFlags.NonPublic)!;

        // Jay's defaults: All realms, Spec Auto (hashed) create picker, Online Only on.
        Assert.That(realmFilter.SelectedItem?.ToString(), Is.EqualTo("All realms"));
        Assert.That(createSpec.SelectedItem?.ToString(), Does.Contain("Auto"));
        onlineOnly.Checked = true;

        bots.Clear();
        for (int i = 0; i < 22; i++)
            bots.Add(MakeBot(botRow, $"Alb{i:00}", "Albion", online: true));
        for (int i = 0; i < 22; i++)
            bots.Add(MakeBot(botRow, $"Mid{i:00}", "Midgard", online: true));
        for (int i = 0; i < 22; i++)
            bots.Add(MakeBot(botRow, $"Hib{i:00}", "Hibernia", online: true));
        // Offline roster members must stay filtered out when Online Only is checked.
        bots.Add(MakeBot(botRow, "OfflineOne", "Albion", online: false));

        // Spec Auto must not participate in filtering — only realm / search / online-only do.
        object[] filtered = ((IEnumerable)main.GetMethod("FilterPopulationBots",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(null, [bots, "All realms", "", true])!).Cast<object>().ToArray();
        Assert.That(filtered.Length, Is.EqualTo(66), "Default filters must keep every online bot from the status payload.");

        main.GetMethod("ApplyFilter", Hidden)!.Invoke(form, null);

        Assert.That(source.Count, Is.EqualTo(66));
        Assert.That(grid.RowCount, Is.EqualTo(66), "RebindBotGrid must paint those rows (1.1.4 empty-grid regression).");
        Assert.That(source.List.Cast<dynamic>().Select(bot => (string)bot.Name), Does.Not.Contain("OfflineOne"));
    }

    [Test]
    public void OtherDashboardTabsStillBindFromTheSameStatusPayload()
    {
        Type main = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
        using Form form = (Form)Activator.CreateInstance(main)!;
        Type botRow = main.GetNestedType("BotRow", BindingFlags.NonPublic)!;
        Type groupRow = main.GetNestedType("GroupRow", BindingFlags.NonPublic)!;
        Type auctionRow = main.GetNestedType("AuctionRow", BindingFlags.NonPublic)!;

        var bots = (IList)main.GetField("_bots", Hidden)!.GetValue(form)!;
        var groups = (IList)main.GetField("_groups", Hidden)!.GetValue(form)!;
        var auctions = (IList)main.GetField("_auctions", Hidden)!.GetValue(form)!;
        var groupsPanel = (FlowLayoutPanel)main.GetField("_groupsPanel", Hidden)!.GetValue(form)!;
        var rvrSource = (BindingSource)main.GetField("_rvrSource", Hidden)!.GetValue(form)!;
        var auctionSource = (BindingSource)main.GetField("_auctionSource", Hidden)!.GetValue(form)!;
        var auctionGrid = (DataGridView)main.GetField("_auctionGrid", Hidden)!.GetValue(form)!;

        object onlineRvr = MakeBot(botRow, "RaidLead", "Albion", online: true, objectiveKind: "RvR");
        object onlinePve = MakeBot(botRow, "CampBot", "Midgard", online: true);
        bots.Clear();
        bots.Add(onlineRvr);
        bots.Add(onlinePve);

        object memberList = Activator.CreateInstance(typeof(List<>).MakeGenericType(botRow))!;
        ((IList)memberList).Add(onlineRvr);
        object group = Activator.CreateInstance(groupRow, [
            "G1", "Albion", "Camping", "Keep siege", "Fighting", "RaidLead", "Caer Benowyc", "RaidLead", memberList
        ])!;
        groups.Clear();
        groups.Add(group);

        auctions.Clear();
        auctions.Add(Activator.CreateInstance(auctionRow, [
            "Fine Steel Long Sword", "Merchant", 20, 1, "Fixed", "1g", 10000L, "2026-12-01T00:00:00Z", "Albion"
        ])!);

        main.GetMethod("ApplyFilter", Hidden)!.Invoke(form, null);
        main.GetMethod("RenderActiveGroups", Hidden)!.Invoke(form, null);
        main.GetMethod("RenderActiveRvr", Hidden)!.Invoke(form, null);
        main.GetMethod("ApplyAuctionFilter", Hidden)!.Invoke(form, null);

        Assert.That(groupsPanel.Controls.OfType<Control>().Count(c => c.Tag != null), Is.EqualTo(1),
            "Active Groups must still build a card from the status payload.");
        Assert.That(rvrSource.Count, Is.EqualTo(1), "Active RvR must still bind online RvR bots.");
        Assert.That(((dynamic)rvrSource.List[0]).Name, Is.EqualTo("RaidLead"));
        Assert.That(auctionSource.Count, Is.EqualTo(1), "Realm Exchange must still bind auction rows.");
        Assert.That(auctionGrid.RowCount, Is.EqualTo(1));
    }

    [Test]
    public void SpecializationChoicesMatchServerTableForHealer()
    {
        Assert.That(BotSpecializationChoices.HasChoices(eCharacterClass.Healer), Is.True);
        Assert.That(BotSpecializationChoices.For(eCharacterClass.Healer),
            Is.EquivalentTo(new[] { eSpecType.MendHealer, eSpecType.AugHealer, eSpecType.PacHealer }));
        Assert.That(BotSpecializationChoices.DisplayName(eSpecType.SmiteCleric), Is.EqualTo("Smite"));
        Assert.That(BotSpecializationChoices.AutoPreferredSpec, Is.EqualTo(-1));
    }

    private static object MakeBot(Type botRow, string name, string realm, bool online = false, string objectiveKind = "SoloPve")
    {
        // BotRow(long? BotId, string Name, string Realm, string RaceName, string Gender, string ClassName,
        // int Level, string ZoneName, string Activity, bool IsOnline, bool CanDelete, bool DeletionQueued,
        // string GroupId, string GroupPhase, string GroupGoal, string GroupStatus, string ObjectiveKind,
        // string ObjectiveAssignmentId, string ObjectiveAssignedUtc, string ObjectivePhase)
        object[] args =
        [
            (long?)1L,
            name,
            realm,
            "Celt",
            "Male",
            "Hero",
            1,
            "Lough Derg",
            "Idle",
            online,
            true,
            false,
            "",
            "",
            "",
            "",
            objectiveKind,
            "",
            "",
            "",
        ];
        return Activator.CreateInstance(botRow, args)!;
    }
}
