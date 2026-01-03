using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.UserMessages;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;

public static class EntityExtends
{
    public static bool IsLegal([NotNullWhen(true)] this CCSPlayerController player)
    {
        return player != null && player.IsValid && player.PlayerPawn.IsValid && player.PlayerPawn.Value?.IsValid == true && !player.IsBot;
    }

    public static CCSPlayerPawn? Pawn(this CCSPlayerController player)
    {
        if (!player.IsLegal() && !player.IsAlive())
            return null;

        CCSPlayerPawn? pawn = player.PlayerPawn.Value;

        return pawn;
    }

    public static bool IsAlive([NotNullWhen(true)] this CCSPlayerController player)
    {
        return player.PawnIsAlive && player.PlayerPawn.Value?.LifeState == (byte)LifeState_t.LIFE_ALIVE;
    }

    public static void GiveWeapon(this CCSPlayerController player, String weaponName)
    {
        if (player.IsAlive())
            player.GiveNamedItem(weaponName);
    }

    public static void Freeze(this CCSPlayerController player)
    {
        CCSPlayerPawn? playerPawn = player.PlayerPawn.Value;

        if (playerPawn == null)
            return;

        playerPawn.MoveType = MoveType_t.MOVETYPE_OBSOLETE;
        playerPawn.ActualMoveType = MoveType_t.MOVETYPE_OBSOLETE;
        Schema.SetSchemaValue(playerPawn.Handle, "CBaseEntity", "m_nActualMoveType", 0);
        Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_MoveType");
    }

    public static void UnFreeze(this CCSPlayerController player)
    {
        CCSPlayerPawn? playerPawn = player.PlayerPawn.Value;

        if (playerPawn == null)
            return;

        playerPawn.MoveType = MoveType_t.MOVETYPE_WALK;
        playerPawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
        Schema.SetSchemaValue(playerPawn.Handle, "CBaseEntity", "m_nActualMoveType", 2);
        Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_MoveType");
    }

    public enum FadeFlags
    {
        FADE_IN,
        FADE_OUT,
        FADE_STAYOUT
    }
    public static void ColorScreen(this CCSPlayerController player, Color color, float hold = 0.1f, float fade = 0.2f, FadeFlags flags = FadeFlags.FADE_IN, bool withPurge = true)
    {
        var fadeMsg = UserMessage.FromPartialName("Fade");

        fadeMsg.SetInt("duration", Convert.ToInt32(fade * 512));
        fadeMsg.SetInt("hold_time", Convert.ToInt32(hold * 512));

        var flag = flags switch
        {
            FadeFlags.FADE_OUT => 0x0001,
            FadeFlags.FADE_IN => 0x0002,
            FadeFlags.FADE_STAYOUT => 0x0008,
            _ => (0x0001 | 0x0010),
        };

        if (withPurge)
            flag |= 0x0010;

        fadeMsg.SetInt("flags", flag);
        fadeMsg.SetInt("color", color.R | color.G << 8 | color.B << 16 | color.A << 24);
        fadeMsg.Send(player);
    }

    public static void CollisionRulesChanged(this CBaseEntity entity, CollisionGroup group)
    {
        if (entity.Collision == null)
        {
            Utils.Log("(CollisionRulesChanged) Collision is null");
            return;
        }
 
        entity.Collision.CollisionGroup = (byte)group;
        entity.Collision.CollisionAttribute.CollisionGroup = (byte)group;

        VirtualFunctionVoid<nint> collisionRulesChanged = new VirtualFunctionVoid<nint>(entity.Handle, GameData.GetOffset("CBaseEntity_CollisionRulesChanged"));
        collisionRulesChanged.Invoke(entity.Handle);
    }
}