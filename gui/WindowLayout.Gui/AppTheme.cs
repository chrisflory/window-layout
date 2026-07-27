namespace WindowLayout.Gui;

internal enum AppThemeMode
{
    Dark,
    Light
}

internal sealed class AppTheme
{
    public required Color BgDeep { get; init; }
    public required Color BgPanel { get; init; }
    public required Color BgHeader { get; init; }
    public required Color TextPrimary { get; init; }
    public required Color TextMuted { get; init; }
    public required Color TextSoft { get; init; }
    public required Color TextVersion { get; init; }
    public required Color Rule { get; init; }
    public required Color LogFrame { get; init; }
    public required Color AccentTeal { get; init; }
    public required Color AccentGreen { get; init; }
    public required Color AccentBlue { get; init; }
    public required Color AccentHint { get; init; }
    public required Color AccentOk { get; init; }
    public required Color AccentWarn { get; init; }
    public required Color BtnMuted { get; init; }
    public required Color BtnDanger { get; init; }
    public required Color BtnDeep { get; init; }
    public required Color SoftButtonFg { get; init; }

    public static AppTheme Dark { get; } = new()
    {
        BgDeep = Color.FromArgb(14, 20, 34),
        BgPanel = Color.FromArgb(20, 28, 44),
        BgHeader = Color.FromArgb(22, 32, 50),
        TextPrimary = Color.FromArgb(248, 250, 252),
        TextMuted = Color.FromArgb(148, 163, 184),
        TextSoft = Color.FromArgb(203, 213, 225),
        TextVersion = Color.FromArgb(100, 116, 139),
        Rule = Color.FromArgb(40, 55, 78),
        LogFrame = Color.FromArgb(40, 55, 78),
        AccentTeal = Color.FromArgb(14, 116, 144),
        AccentGreen = Color.FromArgb(15, 118, 110),
        AccentBlue = Color.FromArgb(37, 99, 175),
        AccentHint = Color.FromArgb(125, 211, 252),
        AccentOk = Color.FromArgb(167, 243, 208),
        AccentWarn = Color.FromArgb(252, 165, 165),
        BtnMuted = Color.FromArgb(51, 65, 85),
        BtnDanger = Color.FromArgb(153, 27, 27),
        BtnDeep = Color.FromArgb(30, 58, 138),
        SoftButtonFg = Color.White
    };

    // Soft paper / cool slate — not cream+terracotta AI default
    public static AppTheme Light { get; } = new()
    {
        BgDeep = Color.FromArgb(241, 245, 249),
        BgPanel = Color.FromArgb(255, 255, 255),
        BgHeader = Color.FromArgb(226, 232, 240),
        TextPrimary = Color.FromArgb(15, 23, 42),
        TextMuted = Color.FromArgb(71, 85, 105),
        TextSoft = Color.FromArgb(51, 65, 85),
        TextVersion = Color.FromArgb(100, 116, 139),
        Rule = Color.FromArgb(203, 213, 225),
        LogFrame = Color.FromArgb(203, 213, 225),
        AccentTeal = Color.FromArgb(8, 145, 178),
        AccentGreen = Color.FromArgb(13, 148, 136),
        AccentBlue = Color.FromArgb(37, 99, 235),
        AccentHint = Color.FromArgb(3, 105, 161),
        AccentOk = Color.FromArgb(4, 120, 87),
        AccentWarn = Color.FromArgb(185, 28, 28),
        BtnMuted = Color.FromArgb(71, 85, 105),
        BtnDanger = Color.FromArgb(185, 28, 28),
        BtnDeep = Color.FromArgb(29, 78, 216),
        SoftButtonFg = Color.White
    };

    public static AppTheme For(AppThemeMode mode) => mode == AppThemeMode.Light ? Light : Dark;
}
