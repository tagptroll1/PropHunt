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

            var data = new PlayerProp(prop, model);
            ApplySizeAndCollision(prop, data);

            Plugin.HideHiderCosmetics(player);

            // Drop the pawn's own collision so seekers don't bump into an
            // invisible capsule. The prop is what they should physically
            // interact with — pawn becomes DEBRIS (walk-through, no movement
            // collision). Bullets that would hit the pawn are zeroed by
            // OnPlayerTakeDamagePre anyway, and shooting the prop still
            // triggers OnEntityTakeDamagePre → kill.
            var pawn = player.PlayerPawn.Value;
            if (pawn != null && pawn.IsValid)
                pawn.CollisionRulesChanged(CollisionGroup.COLLISION_GROUP_DEBRIS);

            Plugin.HiddenPlayers.Add(player.Slot, data);

            if (Instance.Config.Settings.Debug)
                Utils.Log($"[PropSpawner] slot={player.Slot} model={model} size={data.Size} hp={data.Hp}");
            Utils.PrintToChat(player, $"Prop: {data.Size} / {data.Hp} HP");
        }
    }

    // Classify prop by AABB volume, assign HP via linear interpolation within
    // the configured range, and set the matching collision group. Reused on
    // both initial spawn and DoSwap so size/HP track the current model.
    public static void ApplySizeAndCollision(CPhysicsProp prop, PlayerProp data)
    {
        var sizes = Instance.Config.Settings.Sizes;

        var mins = prop.Collision.Mins;
        var maxs = prop.Collision.Maxs;
        float dx = MathF.Max(1f, maxs.X - mins.X);
        float dy = MathF.Max(1f, maxs.Y - mins.Y);
        float dz = MathF.Max(1f, maxs.Z - mins.Z);
        float volume = dx * dy * dz;

        PropSize size;
        int[] range;
        float lo, hi;

        if (volume < sizes.SmallMaxVolume)
        {
            size = PropSize.Small;
            range = sizes.SmallHpRange;
            lo = 0f;
            hi = sizes.SmallMaxVolume;
        }
        else if (volume < sizes.MediumMaxVolume)
        {
            size = PropSize.Medium;
            range = sizes.MediumHpRange;
            lo = sizes.SmallMaxVolume;
            hi = sizes.MediumMaxVolume;
        }
        else
        {
            size = PropSize.Large;
            range = sizes.LargeHpRange;
            lo = sizes.MediumMaxVolume;
            hi = sizes.MediumMaxVolume * 8f; // cap interpolation; anything bigger pegs to max
        }

        float t = Math.Clamp((volume - lo) / MathF.Max(1f, hi - lo), 0f, 1f);
        int hp = (int)MathF.Round(range[0] + t * (range[1] - range[0]));

        data.Size = size;
        data.Hp = hp;
        data.MaxHp = hp;

        // Cache the model's horizontal center offset so the prop can be kept
        // centered on the player (see PlayerProp.CenterX/Y). Z is left alone:
        // the model origin sitting at the player's feet keeps the prop resting
        // on the ground.
        data.CenterX = (mins.X + maxs.X) / 2f;
        data.CenterY = (mins.Y + maxs.Y) / 2f;

        // Large props are solid + standable; small/medium use debris so bullets
        // still hit (OnEntityTakeDamagePre routes damage to the hider) but
        // players walk through.
        var group = size == PropSize.Large
            ? CollisionGroup.COLLISION_GROUP_NPC
            : CollisionGroup.COLLISION_GROUP_DEBRIS;
        prop.CollisionRulesChanged(group);
    }

    // Where to teleport a prop so its geometric center (not its model origin)
    // sits over the player's X/Y. The cached local-space center offset is
    // rotated into world space by the prop's yaw (pitch/roll are ~0 for a
    // standing pawn). Returns the player's origin unchanged when the model is
    // already origin-centered, so most props pay no cost.
    public static Vector PropFollowOrigin(Vector pawnOrigin, float yawDeg, PlayerProp data)
    {
        if (data.CenterX == 0f && data.CenterY == 0f)
            return pawnOrigin;

        double yaw = yawDeg * Math.PI / 180.0;
        float cos = (float)Math.Cos(yaw);
        float sin = (float)Math.Sin(yaw);

        float worldX = cos * data.CenterX - sin * data.CenterY;
        float worldY = sin * data.CenterX + cos * data.CenterY;

        return new Vector(pawnOrigin.X - worldX, pawnOrigin.Y - worldY, pawnOrigin.Z);
    }

    // Build entity angles that tilt a prop so its local up (+Z) aligns to the
    // surface normal `n`, while keeping the player's facing yaw. This is Source's
    // VectorAngles(forward, pseudo-up) — the forward vector is the yaw direction
    // projected onto the surface plane, and the up vector is the normal.
    public static QAngle SurfaceAngles(float yawDeg, float nx, float ny, float nz)
    {
        float nl = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
        if (nl < 1e-4f)
            return new QAngle(0f, yawDeg, 0f);
        nx /= nl; ny /= nl; nz /= nl;

        // Horizontal facing from yaw, projected onto the plane ⟂ to the normal.
        double y = yawDeg * Math.PI / 180.0;
        float fx = (float)Math.Cos(y), fy = (float)Math.Sin(y), fz = 0f;
        float d = fx * nx + fy * ny + fz * nz;
        fx -= d * nx; fy -= d * ny; fz -= d * nz;
        float fl = MathF.Sqrt(fx * fx + fy * fy + fz * fz);
        if (fl < 1e-4f)
            return new QAngle(0f, yawDeg, 0f);
        fx /= fl; fy /= fl; fz /= fl;

        // left = up × forward
        float lx = ny * fz - nz * fy;
        float ly = nz * fx - nx * fz;
        float lz = nx * fy - ny * fx;

        const float Rad2Deg = 180f / MathF.PI;
        float xyDist = MathF.Sqrt(fx * fx + fy * fy);
        float pitch, yaw, roll;
        if (xyDist > 1e-3f)
        {
            yaw = MathF.Atan2(fy, fx) * Rad2Deg;
            pitch = MathF.Atan2(-fz, xyDist) * Rad2Deg;
            roll = MathF.Atan2(lz, ly * fx - lx * fy) * Rad2Deg;
        }
        else
        {
            // forward is vertical — derive yaw from the left vector, no roll.
            yaw = MathF.Atan2(-lx, ly) * Rad2Deg;
            pitch = MathF.Atan2(-fz, xyDist) * Rad2Deg;
            roll = 0f;
        }

        return new QAngle(pitch, yaw, roll);
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
