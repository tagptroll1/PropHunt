using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;

public static class Events
{
    private static Plugin Instance = Plugin.Instance;
    private static Config Config = Instance.Config;

    public static void Register()
    {

        Instance.RegisterListener<Listeners.OnMapStart>(OnMapStart);
        Instance.RegisterListener<Listeners.OnServerPrecacheResources>(OnServerPrecacheResources);
        Instance.RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawned);
        Instance.RegisterListener<Listeners.CheckTransmit>(CheckTransmit);
        Instance.RegisterListener<Listeners.OnTick>(OnTick);
        Instance.RegisterListener<Listeners.OnEntityTakeDamagePre>(OnEntityTakeDamagePre);
        Instance.RegisterListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre);

        Instance.RegisterEventHandler<EventRoundStart>(EventRoundStart);
        Instance.RegisterEventHandler<EventPlayerSpawn>(EventPlayerSpawn);
        Instance.RegisterEventHandler<EventRoundEnd>(EventRoundEnd);
        Instance.RegisterEventHandler<EventRoundPrestart>(EventRoundPrestart);
        Instance.RegisterEventHandler<EventPlayerDeath>(EventPlayerDeath, HookMode.Pre);
    }

    public static void Deregister()
    {
        Instance.RemoveListener<Listeners.OnMapStart>(OnMapStart);
        Instance.RemoveListener<Listeners.OnServerPrecacheResources>(OnServerPrecacheResources);
        Instance.RemoveListener<Listeners.OnEntitySpawned>(OnEntitySpawned);
        Instance.RemoveListener<Listeners.CheckTransmit>(CheckTransmit);
        Instance.RemoveListener<Listeners.OnTick>(OnTick);
        Instance.RemoveListener<Listeners.OnEntityTakeDamagePre>(OnEntityTakeDamagePre);
        Instance.RemoveListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre);

        Instance.DeregisterEventHandler<EventRoundStart>(EventRoundStart);
        Instance.DeregisterEventHandler<EventPlayerSpawn>(EventPlayerSpawn);
        Instance.DeregisterEventHandler<EventRoundEnd>(EventRoundEnd);
        Instance.DeregisterEventHandler<EventRoundPrestart>(EventRoundPrestart);
        Instance.DeregisterEventHandler<EventPlayerDeath>(EventPlayerDeath, HookMode.Pre);
    }

    private static void OnMapStart(string mapname)
    {
        Server.ExecuteCommand("mp_give_player_c4 0");

        CsTeam team = Utils.TeamFromText(Config.Settings.Hiding.Team);
        Server.ExecuteCommand($"mp_teamname_1 {(team == CsTeam.Terrorist ? "Seekers" : "Hiders")}");
        Server.ExecuteCommand($"mp_teamname_2 {(team == CsTeam.Terrorist ? "Hiders" : "Seekers")}");

        Plugin.HiddenPlayers.Clear();

        Utils.AddMapModels(mapname);
    }

    private static void OnServerPrecacheResources(ResourceManifest manifest)
    {
        Utils.AddMapModels(Server.MapName);

        List<string> resources =
        [
            Config.Sounds.SoundEvents,
        ];

        foreach (var model in Plugin.models)
        {
            if (!resources.Contains(model))
                resources.Add(model);
        }

        foreach (var resource in resources)
        {
            if (!string.IsNullOrEmpty(resource))
                manifest.AddResource(resource);
        }
    }

    private static void OnEntitySpawned(CEntityInstance entity)
    {
        if (entity.DesignerName == "prop_physics_multiplayer" || entity.DesignerName == "prop_dynamic")
        {
            var prop = new CBaseModelEntity(entity.Handle);

            string model = prop.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName;

            if (!Plugin.models.Contains(model))
            {
                Plugin.models.Add(model);
                Utils.Log("(OnEntitySpawned) added: " + model);
            }
        }

        if (entity.DesignerName == "func_buyzone")
            entity.Remove();
    }

    private static void CheckTransmit(CCheckTransmitInfoList infoList)
    {
        foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
        {
            if (player == null || player.IsBot || !player.PawnIsAlive)
                continue;

            foreach (var slot in Plugin.HiddenPlayers.Keys)
            {
                var hiddenPlayer = Utilities.GetPlayerFromSlot(slot);
                if (hiddenPlayer == null || player == hiddenPlayer ||
                    player.Pawn.Value?.As<CCSPlayerPawnBase>().PlayerState == CSPlayerState.STATE_OBSERVER_MODE)
                    continue;

                var remove = hiddenPlayer.Pawn.Value;
                if (remove == null) continue;

                info.TransmitEntities.Remove(remove);
            }
        }
    }

    private static void OnTick()
    {
        var players = Utilities.GetPlayers();

        foreach (var player in players.Where(x => x.PawnIsAlive && x.Team == Utils.TeamFromText(Config.Settings.Hiding.Team)))
        {
            var pawn = player.Pawn();
            if (pawn == null) continue;

            if (Plugin.HiddenPlayers.TryGetValue(player.Slot, out var hidden))
            {
                var prop = hidden.entity;

                if (!hidden.Frozen)
                    prop.Teleport(pawn.AbsOrigin, pawn.AbsRotation);
            }
        }

        bool hiding = Plugin.hideTime.CompareTo(DateTime.Now) > 0;
        if (hiding)
        {
            string timeLeft = Plugin.hideTime.Subtract(DateTime.Now).ToString("mm\\:ss");
            foreach (var player in players)
                player.PrintToCenterAlert($"Hiding time: {timeLeft}");
        }
    }

    private static HookResult EventRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        float seconds = Config.Settings.Hiding.Time;
        Plugin.hideTime = DateTime.Now.AddSeconds(seconds);
        Plugin.HiddenPlayers.Clear();
        Plugin.roundStarted = false;
        killedPlayers.Clear();

        foreach (var timer in Instance.Timers)
            timer?.Kill();

        var seekers = Utilities.GetPlayers().Where(x => x.Team != Utils.TeamFromText(Config.Settings.Hiding.Team));

        Instance.AddTimer(seconds, () =>
        {
            var sounds = Instance.Config.Sounds.RoundStart;
            Utils.PlaySoundAll(sounds[Random.Shared.Next(sounds.Count)]);
            Utils.PrintToChatAll("Releasing the seekers!");
            Plugin.roundStarted = true;

            foreach (var player in seekers)
                player.UnFreeze();

        }, TimerFlags.STOP_ON_MAPCHANGE);

        return HookResult.Continue;
    }

    private static HookResult EventPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null) return HookResult.Continue;

        Server.NextFrame(() =>
        {
            player.RemoveWeapons();
            player.PlayerPawn.Value!.Render = Color.FromArgb(254, 255, 255, 255);
            Utilities.SetStateChanged(player.PlayerPawn.Value!, "CBaseModelEntity", "m_clrRender");

            if (player.Team == Utils.TeamFromText(Config.Settings.Hiding.Team))
            {
                Utils.PropSpawner(player);
            }
            else
            {
                player.GiveWeapon("weapon_knife");
                player.GiveWeapon("weapon_ak47");

                if (!Plugin.roundStarted)
                {
                    player.Freeze();
                    player.ColorScreen(Color.Black, Config.Settings.Hiding.Time, 0.5f, EntityExtends.FadeFlags.FADE_OUT);
                }

                else player.UnFreeze();
            }
        });

        return HookResult.Continue;
    }

    private static HookResult EventRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        CsTeam hiders = Utils.TeamFromText(Config.Settings.Hiding.Team);
        CsTeam seekers = hiders == CsTeam.Terrorist
            ? CsTeam.CounterTerrorist
            : CsTeam.Terrorist;

        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
        if ((gameRules.RoundEndTimerTime - gameRules.RoundTime) == 0)
        {
            gameRules.RoundEndReason = 9;
            gameRules.RoundWinStatus = (int)hiders;
            gameRules.RoundEndWinnerTeam = (int)hiders;

            if (gameRules.RoundWinReason != 10)
                CCSMatch.AddTeamScore(gameRules, seekers, -1);

            CCSMatch.AddTeamScore(gameRules, hiders);

            foreach (var player in Utilities.GetPlayers().Where(x => x.Team == seekers && x.IsAlive()))
                player.CommitSuicide(false, true);

            Utils.PrintToChatAll("Hiders has won!");
        }
        else Utils.PrintToChatAll("Seekers has won!");

        return HookResult.Continue;
    }

    private static HookResult EventRoundPrestart(EventRoundPrestart @event, GameEventInfo info)
    {
        if (!Config.Settings.TeamScramble)
            return HookResult.Continue;

        //Utils.PrintToChatAll("Scrambling teams!");

        var players = Utilities.GetPlayers()
            .Where(p => p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
            .ToList();

        if (players.Count < 2)
            return HookResult.Continue;

        players.Shuffle();

        CsTeam teamA = Random.Shared.Next(2) == 0 ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
        CsTeam teamB = teamA == CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        int countA = (players.Count + 1) / 2;
        int countB = players.Count / 2;

        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (i < countA)
                player.SwitchTeam(teamA);
            else
                player.SwitchTeam(teamB);
        }

        return HookResult.Continue;
    }


    private static HashSet<CCSPlayerController> killedPlayers = new();
    private static HookResult EventPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null) return HookResult.Continue;

        if (killedPlayers.Contains(player))
        {
            killedPlayers.Remove(player);
            info.DontBroadcast = true;
        }

        return HookResult.Continue;
    }

    private static HookResult OnEntityTakeDamagePre(CBaseEntity entity, CTakeDamageInfo info)
    {
        if (entity.DesignerName != "prop_physics_override" || info.Attacker.Value?.DesignerName != "player")
            return HookResult.Continue;

        var prop = entity.As<CBaseProp>();

        foreach (var hidden in Plugin.HiddenPlayers)
        {
            if (hidden.Value.entity == prop)
            {
                CCSPlayerController? target = Utilities.GetPlayerFromSlot(hidden.Key);
                if (target == null) break;

                var attackerPawn = info.Attacker.Value.As<CCSPlayerPawn>();
                if (attackerPawn == null) break;

                var attacker = attackerPawn.OriginalController.Value;
                if (attacker == null) break;

                // Subtract bullet damage from prop HP. Survivors get a chat
                // update; only kill when HP runs out.
                int dmg = (int)MathF.Max(1f, info.Damage);
                hidden.Value.Hp -= dmg;

                if (hidden.Value.Hp > 0)
                {
                    Utils.PrintToChat(target, $"{hidden.Value.Size}: {hidden.Value.Hp}/{hidden.Value.MaxHp}");
                    return HookResult.Continue;
                }

                if (prop != null && prop.IsValid)
                    prop.Remove();

                Plugin.HiddenPlayers.Remove(target.Slot);

                nint death = NativeAPI.CreateEvent("player_death", true);

                NativeAPI.SetEventPlayerController(death, "userid", target.Handle);
                NativeAPI.SetEventPlayerController(death, "attacker", attacker.Handle);
                NativeAPI.SetEventPlayerController(death, "assister", 0);

                NativeAPI.SetEventString(death, "weapon", "movelinear");

                NativeAPI.FireEvent(death, false);

                Server.NextFrame(() =>
                {
                    if (!killedPlayers.Contains(target))
                        killedPlayers.Add(target);

                    target.CommitSuicide(false, true);
                });

                break;
            }
        }

        return HookResult.Continue;
    }

    private static HookResult OnPlayerTakeDamagePre(CCSPlayerPawn pawn, CTakeDamageInfo info)
    {
        if (pawn.DesignerName != "player" && info.Attacker.Value?.DesignerName != "player")
            return HookResult.Continue;

        if (Plugin.HiddenPlayers.ContainsKey(pawn.OriginalController.Value!.Slot))
        {
            info.ShouldBleed = false;
            info.ShouldSpark = false;
            info.Damage = 0;
            return HookResult.Handled;
        }

        return HookResult.Continue;
    }

    private static HookResult CMsgSosStartSoundEvent(UserMessage um)
    {
        int entIndex = um.ReadInt("source_entity_index");
        var entHandle = NativeAPI.GetEntityFromIndex(entIndex);

        var pawn = new CBasePlayerPawn(entHandle);
        if (pawn == null || !pawn.IsValid || pawn.DesignerName != "player") return HookResult.Continue;

        var player = pawn.Controller?.Value?.As<CCSPlayerController>();
        if (player == null || !player.IsValid) return HookResult.Continue;

        if (Plugin.HiddenPlayers.ContainsKey(player.Slot))
        {
            foreach (var target in Utilities.GetPlayers().Where(x => !x.IsBot))
            {
                if (target == player) continue;
                um.Recipients.Remove(target);
            }
        }

        return HookResult.Continue;
    }
}
