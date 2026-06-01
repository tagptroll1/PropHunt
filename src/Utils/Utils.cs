using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

public static class Utils
{
    private static Plugin Instance = Plugin.Instance;
    private static Config config = Instance.Config;

    public static void Log(string message)
    {
        Instance.Logger.LogInformation(message);
    }

    public static void PrintToChat(CCSPlayerController player, string message)
    {
        player.PrintToChat($" {config.Settings.Prefix} {ChatColors.Grey}{message}");
    }

    public static void PrintToChatAll(string message)
    {
        Server.PrintToChatAll($" {config.Settings.Prefix} {ChatColors.Grey}{message}");
    }

    public static void PlaySoundAll(string sound)
    {
        foreach (var player in Utilities.GetPlayers())
            player.EmitSound(sound, [player]);
    }

    public static CsTeam TeamFromText(string team)
    {
        team = team.ToLower();

        switch (team)
        {
            case "t":
                return CsTeam.Terrorist;
            case "ct":
                return CsTeam.CounterTerrorist;
            case "terrorist":
                return CsTeam.Terrorist;
            case "counterterrorist":
                return CsTeam.CounterTerrorist;
        }

        return CsTeam.None;
    }

    public static void PropSpawner(CCSPlayerController? player, bool swap = false)
    {
        if (player == null)
            return;

        var models = Plugin.models;

        if (models.Count == 0)
        {
            Utils.Log("[PropSpawner] Models list is empty!");
            return;
        }

        // True random pick, but avoid handing the same player the same model two
        // rounds in a row. Five attempts is enough — with a pool of N>=2 the
        // probability of five collisions is (1/N)^5.
        Plugin.LastModel.TryGetValue(player.Slot, out var previous);
        string model = models[Random.Shared.Next(models.Count)];
        for (int attempt = 0; attempt < 5 && model == previous && models.Count > 1; attempt++)
            model = models[Random.Shared.Next(models.Count)];
        Plugin.LastModel[player.Slot] = model;

        if (swap && Plugin.HiddenPlayers.TryGetValue(player.Slot, out var existingProp))
        {
            // swap
            return;
        }
        else
        {
            var prop = Utilities.CreateEntityByName<CPhysicsPropOverride>("prop_physics_override")!;

            prop.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(uint)(1 << 2);
            prop.SetModel(model);
            prop.Teleport(player.AbsOrigin);
            prop.DispatchSpawn();
            prop.AcceptInput("DisableMotion");
            prop.CollisionRulesChanged(CollisionGroup.COLLISION_GROUP_DEBRIS);

            Plugin.HideHiderCosmetics(player);
            Plugin.HiddenPlayers.Add(player.Slot, new PlayerProp(prop, model));
        }
    }

    public static void AddMapModels(string mapname)
    {
        Plugin.models.Clear();

        string filePath = Path.Combine(Instance.ModuleDirectory, "maps", $"{mapname}.txt");

        if (!File.Exists(filePath))
            filePath = Path.Combine(Instance.ModuleDirectory, "maps", "default.txt");

        try
        {
            foreach (string line in File.ReadAllLines(filePath))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith("#"))
                    continue;

                if (trimmed.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase)
                    && trimmed.IndexOf("weapon_c4", StringComparison.OrdinalIgnoreCase) < 0
                    && trimmed.IndexOf("/c4", StringComparison.OrdinalIgnoreCase) < 0)
                    Plugin.models.Add(trimmed.ToLower());
            }

            Utils.Log($"(OnMapStart) Loaded {Plugin.models.Count} models from {mapname}.txt");
        }
        catch (Exception ex)
        {
            Utils.Log($"(OnMapStart) Error reading {mapname}.txt: {ex.Message}");
        }
    }

    public static void Shuffle<T>(this IList<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = Random.Shared.Next(n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }
}
