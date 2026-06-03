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
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using FixVectorLeak;

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

        // Prop stays motion-disabled in BOTH states. Enabling motion when
        // freezing made the prop drop under gravity, which defeats the point
        // of freezing (locking the prop in a chosen hiding spot).
        // Collision: when frozen the prop is solid + standable (NPC group) so
        // seekers bump into and stand on it; when unfrozen we restore the
        // size-tier collision so it tracks the player normally.
        var pawn = player.PlayerPawn.Value;

        if (data.Frozen)
        {
            // NPC is the "solid + standable" group (same one large props use):
            // seekers can bump into and jump on top of a frozen prop. DEFAULT
            // did not register as standable here, so frozen hiders had no
            // effective hitbox to climb on.
            data.entity.CollisionRulesChanged(CollisionGroup.COLLISION_GROUP_NPC);

            // Kill the pawn's velocity *before* freezing the move type. Freezing
            // only stops further movement; any velocity the player carried into
            // the freeze (e.g. mid-air) would otherwise drift the pawn away from
            // the now-stationary prop, so the player and prop ended up in
            // different spots. Zeroing it keeps them locked together.
            if (pawn != null && pawn.AbsVelocity != null)
            {
                pawn.AbsVelocity.X = 0f;
                pawn.AbsVelocity.Y = 0f;
                pawn.AbsVelocity.Z = 0f;
            }

            player.Freeze();
        }
        else
        {
            var group = data.Size == PropSize.Large
                ? CollisionGroup.COLLISION_GROUP_NPC
                : CollisionGroup.COLLISION_GROUP_DEBRIS;
            data.entity.CollisionRulesChanged(group);
            player.UnFreeze();
        }

        if (pawn != null)
        {
            var rot = pawn.AbsRotation;
            float yaw = rot.Y + data.YawOffset;
            if (data.YawOffset != 0f)
                rot = new QAngle(rot.X, yaw, rot.Z);
            data.entity.Teleport(Utils.PropFollowOrigin(pawn.AbsOrigin, yaw, data), rot);
        }

        Utils.PrintToChat(player, $"{ChatColors.Grey}Freeze: {(data.Frozen ? $"{ChatColors.Green}ON" : $"{ChatColors.Red}OFF")}");
    }

    // Nudge the prop's yaw by `delta` degrees. Only meaningful while the prop
    // is Frozen — when unfrozen, OnTick re-applies the player's AbsRotation
    // (plus YawOffset) every tick. Frozen props don't get OnTick updates, so
    // we snap them with an explicit Teleport here.
    private static void DoRotate(CCSPlayerController player, PlayerProp data, float delta)
    {
        if (!data.Frozen)
        {
            Utils.PrintToChat(player, $"{ChatColors.Grey}Rotation only works while frozen");
            return;
        }

        data.YawOffset = (data.YawOffset + delta) % 360f;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return;

        var baseRot = pawn.AbsRotation;
        float yaw = baseRot.Y + data.YawOffset;
        var rot = new QAngle(baseRot.X, yaw, baseRot.Z);
        // Re-derive the origin from the pawn (not the prop's current origin):
        // rotating changes which way the center offset points, so we must
        // recompute it to keep the prop's center over the player.
        data.entity.Teleport(Utils.PropFollowOrigin(pawn.AbsOrigin, yaw, data), rot);
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
        var sound = sounds[Random.Shared.Next(sounds.Count)];
        // Emit from the player PAWN, not the controller. The controller is a
        // non-spatial entity (no world transform), so EmitSound on it plays at
        // the world origin (0,0,0) for everyone — that was the "taunts all play
        // in one spot" bug. OnTick keeps the pawn synced to the prop's visible
        // location, and the pawn is a proper 3D audio emitter, so emitting from
        // it plays the taunt at the prop with correct directional falloff.
        var pawn = player.PlayerPawn.Value;
        if (pawn != null && pawn.IsValid)
            pawn.EmitSound(sound);
        else
            player.EmitSound(sound);

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
        data.Model = model;

        // SetModel changes the underlying mesh — the AABB updates, so re-run
        // the size/HP classification. Reset HP to the new max (intentional:
        // swapping refreshes you, like picking a new disguise).
        Utils.ApplySizeAndCollision(data.entity, data);

        data.Swaps--;
        Utils.PrintToChat(player, $"{ChatColors.Grey}Swapped model. You have {data.Swaps} left ({data.Size} / {data.Hp} HP)");
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

    [ConsoleCommand("css_phrotleft", "Prop Hunt: nudge frozen prop rotation 15° left")]
    public void OnPhRotLeft(CCSPlayerController? player, CommandInfo command)
    {
        var data = Hider(player);
        if (data == null) return;
        DoRotate(player!, data, -15f);
    }

    [ConsoleCommand("css_phrotright", "Prop Hunt: nudge frozen prop rotation 15° right")]
    public void OnPhRotRight(CCSPlayerController? player, CommandInfo command)
    {
        var data = Hider(player);
        if (data == null) return;
        DoRotate(player!, data, 15f);
    }

    // ---- Thirdperson (no sv_cheats) ----------------------------------------
    // We deliberately don't touch the `thirdperson` / cam_* cheat cvars (they
    // need sv_cheats 1). Instead we point the pawn's camera at an invisible
    // prop_dynamic anchored behind the player's eyes and re-position it every
    // tick (UpdateThirdpersonCameras, called from OnTick). Setting the pawn's
    // m_hViewEntity makes the client render from that entity's POV — for a
    // hider this shows their own prop from behind, for a seeker their body.
    private static void ToggleThirdperson(CCSPlayerController player)
    {
        if (Plugin.ThirdpersonCam.ContainsKey(player.Slot))
            DisableThirdperson(player);
        else
            EnableThirdperson(player);
    }

    private static void EnableThirdperson(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.CameraServices == null) return;
        if (!player.PawnIsAlive) return;

        var camera = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (camera == null) return;
        camera.DispatchSpawn();

        Plugin.ThirdpersonCam[player.Slot] = camera;
        PositionThirdpersonCamera(pawn, camera);

        pawn.CameraServices.ViewEntity.Raw = camera.EntityHandle.Raw;
        Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");

        Utils.PrintToChat(player, $"Thirdperson: {ChatColors.Green}ON");
    }

    public static void DisableThirdperson(CCSPlayerController player)
    {
        if (Plugin.ThirdpersonCam.TryGetValue(player.Slot, out var camera))
        {
            if (camera != null && camera.IsValid)
                camera.Remove();
            Plugin.ThirdpersonCam.Remove(player.Slot);
        }

        var pawn = player.PlayerPawn.Value;
        if (pawn != null && pawn.IsValid && pawn.CameraServices != null)
        {
            // 0xFFFFFFFF is an invalid handle — the client falls back to the
            // pawn's own eyes (first person).
            pawn.CameraServices.ViewEntity.Raw = uint.MaxValue;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
        }

        if (player.IsValid)
            Utils.PrintToChat(player, $"Thirdperson: {ChatColors.Red}OFF");
    }

    // Anchor the camera prop behind + slightly above the eyes, along the view
    // direction, so the player sees themselves from over-the-shoulder. No wall
    // trace — the camera can clip through geometry; acceptable for a toggle.
    private static void PositionThirdpersonCamera(CCSPlayerPawn pawn, CDynamicProp camera)
    {
        var origin = pawn.AbsOrigin;
        if (origin == null) return;

        var eyeAngles = pawn.EyeAngles;
        eyeAngles.AngleVectors(out var fwd, out _, out _);

        const float back = 110f;
        const float up = 20f;
        float eyeZ = origin.Z + pawn.ViewOffset.Z;

        var camPos = new Vector(
            origin.X - fwd.X * back,
            origin.Y - fwd.Y * back,
            eyeZ - fwd.Z * back + up);

        camera.Teleport(camPos, eyeAngles, null);
    }

    // Called once per tick from OnTick. Keeps each active camera glued behind
    // its player and tears thirdperson down if the player died / went invalid.
    public static void UpdateThirdpersonCameras()
    {
        if (Plugin.ThirdpersonCam.Count == 0) return;

        foreach (var slot in Plugin.ThirdpersonCam.Keys.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            var pawn = player?.PlayerPawn.Value;

            if (player == null || !player.IsValid || pawn == null || !pawn.IsValid || !player.PawnIsAlive)
            {
                if (player != null && player.IsValid)
                {
                    DisableThirdperson(player);
                }
                else
                {
                    // Player gone (disconnect) — just drop the camera entity.
                    if (Plugin.ThirdpersonCam.TryGetValue(slot, out var cam) && cam != null && cam.IsValid)
                        cam.Remove();
                    Plugin.ThirdpersonCam.Remove(slot);
                }
                continue;
            }

            var camera = Plugin.ThirdpersonCam[slot];
            if (camera == null || !camera.IsValid)
            {
                DisableThirdperson(player);
                continue;
            }

            PositionThirdpersonCamera(pawn, camera);
        }
    }

    [ConsoleCommand("css_thirdperson", "Prop Hunt: toggle thirdperson camera")]
    public void OnPhThirdperson(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;
        ToggleThirdperson(player);
    }

    // ---- Admin physics debug -----------------------------------------------
    // Lock / unlock every loose physics prop on the map (NOT hider props —
    // they're tracked separately and untouchable here). Useful when a map
    // exploit lets seekers shove crates into hider hiding spots.
    private static IEnumerable<CBaseEntity> LoosePhysicsProps()
    {
        var hiderProps = new HashSet<uint>(
            Plugin.HiddenPlayers.Values
                .Where(p => p.entity != null && p.entity.IsValid)
                .Select(p => p.entity.Index));

        foreach (var name in new[] { "prop_physics_multiplayer", "prop_physics", "prop_physics_override" })
            foreach (var ent in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(name))
                if (ent.IsValid && !hiderProps.Contains(ent.Index))
                    yield return ent;
    }

    [RequiresPermissions("@css/admin")]
    [ConsoleCommand("css_ph_lockphys", "Prop Hunt admin: freeze all loose physics props")]
    public void OnPhLockPhys(CCSPlayerController? player, CommandInfo command)
    {
        int n = 0;
        foreach (var ent in LoosePhysicsProps())
        {
            ent.AcceptInput("DisableMotion");
            n++;
        }
        var who = player ?? null;
        if (who != null) Utils.PrintToChat(who, $"Locked {n} props");
        else Utils.Log($"[admin] Locked {n} props");
    }

    [RequiresPermissions("@css/admin")]
    [ConsoleCommand("css_ph_unlockphys", "Prop Hunt admin: unfreeze all loose physics props")]
    public void OnPhUnlockPhys(CCSPlayerController? player, CommandInfo command)
    {
        int n = 0;
        foreach (var ent in LoosePhysicsProps())
        {
            ent.AcceptInput("EnableMotion");
            n++;
        }
        var who = player ?? null;
        if (who != null) Utils.PrintToChat(who, $"Unlocked {n} props");
        else Utils.Log($"[admin] Unlocked {n} props");
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

            // Thirdperson toggle is available to EVERYONE (hiders + seekers),
            // so it lives before the hider-only gate. Bound to the weapon
            // Inspect key (no client config; also exposed as css_thirdperson).
            if ((pressed & PlayerButtons.Inspect) != 0) ToggleThirdperson(player);

            if (!Plugin.HiddenPlayers.TryGetValue(player.Slot, out var data)) return;

            if ((pressed & PlayerButtons.Use)     != 0) DoFreeze(player, data);
            if ((pressed & PlayerButtons.Reload)  != 0) DoSwap(player, data);
            if ((pressed & PlayerButtons.Attack2) != 0) DoTaunt(player, data);

            // While frozen, A/D nudge yaw instead of being noops (player can't
            // strafe anyway — MOVETYPE_OBSOLETE blocks movement). Lets the
            // hider align their prop with cover.
            if (data.Frozen)
            {
                if ((pressed & PlayerButtons.Moveleft)  != 0) DoRotate(player, data, -15f);
                if ((pressed & PlayerButtons.Moveright) != 0) DoRotate(player, data,  15f);
            }
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

