using System.Reflection;
using NUnit.Framework;
using DOL.GS;

namespace OfflineDaoc.Launcher.Tests;

/// <summary>
/// Regression coverage for Active Population rebind / SelectBot after large batch creates
/// (IndexOutOfRangeException dialogs on Shift/Ctrl + Lv).
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
            first.Add(MakeBot(botRow, $"Bot{i:000}", "Hibernia"));
        source.DataSource = first;
        source.ResetBindings(false);
        source.Position = first.Count - 1;
        Assert.That(source.Count, Is.EqualTo(75));

        var grown = new List<object>(first);
        for (int i = 75; i < 85; i++)
            grown.Add(MakeBot(botRow, $"Bot{i:000}", "Hibernia"));

        main.GetMethod("RebindBotGrid", Hidden)!.Invoke(form, [grown]);
        Assert.That(source.Count, Is.EqualTo(85));
        Assert.That(grid.RowCount, Is.EqualTo(85));

        main.GetMethod("SelectBot", Hidden)!.Invoke(form, ["Bot084"]);
        Assert.That(source.Position, Is.EqualTo(84));
        Assert.That(((dynamic)source.Current).Name, Is.EqualTo("Bot084"));
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

    private static object MakeBot(Type botRow, string name, string realm)
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
            false,
            true,
            false,
            "",
            "",
            "",
            "",
            "SoloPve",
            "",
            "",
            "",
        ];
        return Activator.CreateInstance(botRow, args)!;
    }
}
