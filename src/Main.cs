using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;

public partial class Plugin : BasePlugin, IPluginConfig<Config>
{
    public override string ModuleName => "Prop Hunt";
    public override string ModuleVersion => "0.0.1";
    public override string ModuleAuthor => "exkludera";

    public static Plugin Instance = new();
    public static List<string> models = new();
    public static DateTime hideTime = DateTime.Now;
    public static Dictionary<int, PlayerProp> HiddenPlayers = new();
    public static bool roundStarted = false;

    public override void Load(bool hotReload)
    {
        Instance = this;

        Events.Register();
    }

    public override void Unload(bool hotReload)
    {
        Events.Deregister();
    }

    public Config Config { get; set; } = new();
    public void OnConfigParsed(Config config)
    {
        Config = config;
        Config.Settings.Prefix = StringExtensions.ReplaceColorTags(config.Settings.Prefix);
    }
}