// Two ways to trigger each action:
//   1. Console command: bind f1 "css_phfreeze"  (or chat alias "!phfreeze")
//   2. Default hotkeys (no client config needed; server reads input bits):
//        E (Use)     → freeze
//        R (Reload)  → swap / reroll prop model
//        Mouse2      → taunt
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

public partial class Plugin
{
    private static PlayerProp? Hider(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid)
            return null;
        return Plugin.HiddenPlayers.TryGetValue(player.Slot, out var data) ? data : null;
    }

    // ---- Action helpers (shared by console command + hotkey paths) ---------

    private static void DoFreeze(CCSPlayerController player, PlayerProp data)
    {
        data.Frozen = !data.Frozen;

        if (data.Frozen)
        {
            data.entity.AcceptInput("EnableMotion");
            data.entity.CollisionRulesChanged(CollisionGroup.COLLISION_GROUP_DEFAULT);
            player.Freeze();
        }
        else
        {
            data.entity.AcceptInput("DisableMotion");
            data.entity.CollisionRulesChanged(CollisionGroup.COLLISION_GROUP_DEBRIS);
            player.UnFreeze();
        }

        data.entity.Teleport(player.PlayerPawn.Value?.AbsOrigin, player.PlayerPawn.Value?.AbsRotation);
        Utils.PrintToChat(player, $"{ChatColors.Grey}Freeze: {(data.Frozen ? $"{ChatColors.Green}ON" : $"{ChatColors.Red}OFF")}");
    }

    private static void DoDecoy(CCSPlayerController player, PlayerProp data)
    {
        if (data.Decoys <= 0)
        {
            Utils.PrintToChat(player, $"{ChatColors.Grey}No decoys left");
            return;
        }

        data.Decoys--;

        var decoy = Utilities.CreateEntityByName<CPhysicsPropOverride>("prop_physics_override")!;
        decoy.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(uint)(1 << 2);
        decoy.SetModel(data.entity.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName);
        decoy.Teleport(data.entity.AbsOrigin, data.entity.AbsRotation);
        decoy.DispatchSpawn();

        Utils.PrintToChat(player, $"{ChatColors.Grey}Placed decoy. You have {data.Decoys} left");
    }

    private static void DoTaunt(CCSPlayerController player, PlayerProp data)
    {
        bool unlimited = Instance.Config.Settings.Hiding.TauntLimit <= 0;
        if (!unlimited)
        {
            if (data.Taunts <= 0)
            {
                Utils.PrintToChat(player, $"{ChatColors.Grey}No taunts left");
                return;
            }
            data.Taunts--;
        }

        var sounds = Instance.Config.Sounds.Taunt;
        // Emit from the PROP entity (not the player pawn): the prop is where
        // seekers see the hider, so audio must come from the same world point.
        // Also bypasses the per-player sound-recipient filter in
        // CMsgSosStartSoundEvent — that one only strips msg-208 sounds emitted
        // by the player pawn.
        if (data.entity != null && data.entity.IsValid)
            data.entity.EmitSound(sounds[Random.Shared.Next(sounds.Count)]);
        else
            player.EmitSound(sounds[Random.Shared.Next(sounds.Count)]);

        Utils.PrintToChat(player, unlimited
            ? $"{ChatColors.Grey}Taunt!"
            : $"{ChatColors.Grey}Used taunt. You have {data.Taunts} left");
    }

    private static void DoSwap(CCSPlayerController player, PlayerProp data)
    {
        if (data.Swaps <= 0)
        {
            Utils.PrintToChat(player, $"{ChatColors.Grey}No swaps left");
            return;
        }

        var models = Plugin.models;
        if (models.Count == 0)
        {
            Utils.PrintToChat(player, $"{ChatColors.Grey}No prop models available");
            return;
        }

        if (data.entity == null || !data.entity.IsValid)
            return;

        // Instant swap: SetModel on the live prop. Works smoothly now that the
        // precache fix ensures every model in the per-map .txt is in the resource
        // manifest. Earlier we did Remove + Create to work around the precache
        // miss, which caused the 1–4 sec "no prop visible" gap.
        string oldModel = data.entity.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName;
        string model = oldModel;
        for (int attempt = 0; attempt < 5 && model == oldModel && models.Count > 1; attempt++)
            model = models[Random.Shared.Next(models.Count)];

        data.entity.SetModel(model);
        Plugin.LastModel[player.Slot] = model;

        data.Swaps--;
        Utils.PrintToChat(player, $"{ChatColors.Grey}Swapped model. You have {data.Swaps} left");
    }

    // ---- Console-command entry points --------------------------------------

    [ConsoleCommand("css_phfreeze", "Prop Hunt: toggle freezing your prop in place")]
    public void OnPhFreeze(CCSPlayerController? player, CommandInfo command)
    {
        var data = Hider(player);
        if (data == null) return;
        DoFreeze(player!, data);
    }

    [ConsoleCommand("css_phdecoy", "Prop Hunt: place a decoy prop")]
    public void OnPhDecoy(CCSPlayerController? player, CommandInfo command)
    {
        var data = Hider(player);
        if (data == null) return;
        DoDecoy(player!, data);
    }

    [ConsoleCommand("css_phtaunt", "Prop Hunt: play a taunt sound")]
    public void OnPhTaunt(CCSPlayerController? player, CommandInfo command)
    {
        var data = Hider(player);
        if (data == null) return;
        DoTaunt(player!, data);
    }

    [ConsoleCommand("css_phswap", "Prop Hunt: swap to a random prop model")]
    public void OnPhSwap(CCSPlayerController? player, CommandInfo command)
    {
        var data = Hider(player);
        if (data == null) return;
        DoSwap(player!, data);
    }

    // ---- Hide cosmetics (body + gloves + agent wearables) ------------------
    // Pawn `m_clrRender` alpha=0 hides only the body model. Gloves and agent
    // cosmetics are separate CEconWearable entities listed in the pawn's
    // m_hMyWearables array and have their own render state. Without zeroing
    // them too, hiders see their own gloves in first-person and would see
    // gloves in third-person if anything (mirror, spec) renders their pawn.
    //
    // Called both directly from PropSpawner (via postPatch injection) and from
    // a delayed timer after EventPlayerSpawn — wearables sometimes spawn AFTER
    // PropSpawner runs, so a single immediate call can miss them.
    public static void HideHiderCosmetics(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid) return;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;

        pawn.Render = Color.FromArgb(0, 0, 0, 0);
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");

        foreach (var handle in pawn.MyWearables)
        {
            var w = handle.Value;
            if (w == null || !w.IsValid) continue;
            w.Render = Color.FromArgb(0, 0, 0, 0);
            Utilities.SetStateChanged(w, "CBaseModelEntity", "m_clrRender");
        }
    }

    // ---- Hotkeys + spawn-time cosmetic hide --------------------------------
    // Called from Main.cs::Load via postPatch — see pkgs/cs2-prophunt/default.nix.
    public static void RegisterHotkeys()
    {
        Instance.RegisterListener<Listeners.OnPlayerButtonsChanged>((player, pressed, released) =>
        {
            if (player == null || !player.IsValid) return;
            if (!Plugin.HiddenPlayers.TryGetValue(player.Slot, out var data)) return;

            if ((pressed & PlayerButtons.Use)     != 0) DoFreeze(player, data);
            if ((pressed & PlayerButtons.Reload)  != 0) DoSwap(player, data);
            if ((pressed & PlayerButtons.Attack2) != 0) DoTaunt(player, data);
        });

        // Safety net: re-run the cosmetic hide ~250ms after each player spawn
        // for the hider team. The immediate PropSpawner-time call usually wins,
        // but CS2 occasionally spawns wearables a few frames later — this
        // catches those.
        Instance.RegisterEventHandler<EventPlayerSpawn>((@event, info) =>
        {
            var player = @event.Userid;
            if (player == null) return HookResult.Continue;
            if (player.Team != Utils.TeamFromText(Instance.Config.Settings.Hiding.Team))
                return HookResult.Continue;

            Instance.AddTimer(0.25f, () => HideHiderCosmetics(player), TimerFlags.STOP_ON_MAPCHANGE);
            return HookResult.Continue;
        });
    }
}

