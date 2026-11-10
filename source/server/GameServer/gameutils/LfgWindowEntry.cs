namespace DOL.GS
{
    /// <summary>One row in the Looking For Group window / /lfg list (player or advertising gamebot).</summary>
    public sealed class LfgWindowEntry
    {
        public string Name { get; init; }
        public byte Level { get; init; }
        public string ClassName { get; init; }
        public ushort ZoneSkinId { get; init; }
        public string ZoneDescription { get; init; }
        public bool IsGrouped { get; init; }
        public bool IsBot { get; init; }

        public static LfgWindowEntry FromPlayer(GamePlayer player) => new()
        {
            Name = player?.Name ?? string.Empty,
            Level = player?.Level ?? 0,
            ClassName = player?.CharacterClass?.Name ?? string.Empty,
            ZoneSkinId = player?.CurrentZone != null ? (ushort)player.CurrentZone.ZoneSkinID : (ushort)0,
            ZoneDescription = player?.CurrentZone?.Description ?? string.Empty,
            IsGrouped = player?.Group != null,
            IsBot = false,
        };

        public static LfgWindowEntry FromBot(GameBot bot) => new()
        {
            Name = bot?.Name ?? string.Empty,
            Level = bot?.Level ?? 0,
            ClassName = bot?.CharacterClass?.Name ?? bot?.ClassName ?? string.Empty,
            ZoneSkinId = bot?.CurrentZone != null ? (ushort)bot.CurrentZone.ZoneSkinID : (ushort)0,
            ZoneDescription = bot?.CurrentZone?.Description ?? string.Empty,
            IsGrouped = bot?.Group != null && bot.Group.MemberCount > 1,
            IsBot = true,
        };
    }
}
