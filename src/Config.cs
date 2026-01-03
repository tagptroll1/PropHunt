using CounterStrikeSharp.API.Core;

public class Config : BasePluginConfig
{
    public Config_Settings Settings { get; set; } = new();
    public Config_Sounds Sounds { get; set; } = new();
}

public class Config_Settings
{
    public string Prefix { get; set; } = "{lightblue}[PropHunt]";
    public string MenuType { get; set; } = "CenterHtmlMenu";
    public bool TeamScramble { get; set; } = true;
    public class Config_Settings_Hiding
    {
        public string Team { get; set; } = "T";
        public float Time { get; set; } = 60;
        public int DecoyLimit { get; set; } = 2;
        public int SwapLimit { get; set; } = 10;
        public int TauntLimit { get; set; } = 10;
    }
    public Config_Settings_Hiding Hiding { get; set; } = new();
}

public class Config_Sounds
{
    public string SoundEvents { get; set; } = "soundevents/example.vsndevts";
    public List<string> RoundStart { get; set; } = ["balkan.radio_letsgo01", "balkan.radio_letsgo02", "balkan.radio_letsgo06", "balkan.radio_letsgo07", "balkan.radio_letsgo010"];
    public List<string> LastAlive { get; set; } = ["balkan.lastmanstanding06"];
    public List<string> Taunt { get; set; } = [ "training.commander_comment_19", "training.commander_comment_21", "training.commander_comment_22"];
}

public class PlayerProp
{
    public CPhysicsProp entity;
    public string Model;
    public bool Frozen;
    public int Swaps;
    public int Decoys;
    public int Taunts;

    public PlayerProp(CPhysicsProp prop, string model)
    {
        entity = prop;
        Model = model;
        Frozen = false;
        Swaps = Plugin.Instance.Config.Settings.Hiding.SwapLimit;
        Decoys = Plugin.Instance.Config.Settings.Hiding.DecoyLimit;
        Taunts = Plugin.Instance.Config.Settings.Hiding.TauntLimit;
    }
}