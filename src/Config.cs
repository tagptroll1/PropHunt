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

    public class Config_Settings_Sizes
    {
        // Volume thresholds in units^3 (AABB extents product).
        public float SmallMaxVolume { get; set; } = 4096f;     // < 16^3
        public float MediumMaxVolume { get; set; } = 262144f;  // < 64^3
        public int[] SmallHpRange { get; set; } = [10, 25];
        public int[] MediumHpRange { get; set; } = [25, 100];
        public int[] LargeHpRange { get; set; } = [100, 250];
    }
    public Config_Settings_Sizes Sizes { get; set; } = new();

    public bool Debug { get; set; } = false;
}

public class Config_Sounds
{
    public string SoundEvents { get; set; } = "soundevents/example.vsndevts";
    public List<string> RoundStart { get; set; } = ["balkan.radio_letsgo01", "balkan.radio_letsgo02", "balkan.radio_letsgo06", "balkan.radio_letsgo07", "balkan.radio_letsgo010"];
    public List<string> LastAlive { get; set; } = ["balkan.lastmanstanding06"];
    public List<string> Taunt { get; set; } = [ "training.commander_comment_19", "training.commander_comment_21", "training.commander_comment_22"];
}

public enum PropSize { Small, Medium, Large }

public class PlayerProp
{
    public CPhysicsProp entity;
    public string Model;
    public bool Frozen;
    public int Swaps;
    public int Decoys;
    public int Taunts;
    public PropSize Size;
    public int Hp;
    public int MaxHp;

    // Horizontal offset (in the prop's local model space) from the model's
    // origin to the geometric center of its AABB. Many prop models have their
    // origin at a corner/edge rather than the middle, so teleporting the prop
    // straight onto the player leaves it visibly off-center — part of it pokes
    // out of cover / through walls. We subtract this (rotated by yaw) when
    // following the player so the prop's center sits over the player instead.
    // Recomputed by ApplySizeAndCollision whenever the model changes.
    public float CenterX;
    public float CenterY;

    public PlayerProp(CPhysicsProp prop, string model)
    {
        entity = prop;
        Model = model;
        Frozen = false;
        Swaps = Plugin.Instance.Config.Settings.Hiding.SwapLimit;
        Decoys = Plugin.Instance.Config.Settings.Hiding.DecoyLimit;
        Taunts = Plugin.Instance.Config.Settings.Hiding.TauntLimit;
        Size = PropSize.Medium;
        Hp = 100;
        MaxHp = 100;
        YawOffset = 0f;
    }

    // Admin-nudged yaw offset, added to the player's AbsRotation each tick.
    // Lets an admin rotate a stuck/awkward prop without the player having to
    // physically turn around. Persists across freeze/unfreeze.
    public float YawOffset;
}