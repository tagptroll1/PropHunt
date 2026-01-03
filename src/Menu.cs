using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Enum;
using CS2MenuManager.API.Interface;

public partial class Menu
{
    private static Plugin Instance = Plugin.Instance;

    public static IMenu Create(string title, string menuType, IMenu? prevMenu = null)
    {
        IMenu menu = MenuManager.MenuByType(menuType, title, Instance);

        if (prevMenu != null)
            menu.PrevMenu = prevMenu;

        menu.ExitButton = false;

        return menu;
    }

    public static void Open(CCSPlayerController player)
    {
        string menuType = Instance.Config.Settings.MenuType;

        if (!Plugin.HiddenPlayers.TryGetValue(player.Slot, out var data))
            return;

        IMenu menu = Create("Prop Hunt", menuType);

        menu.AddItem("Freeze", (player, option) =>
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
                data.entity.CollisionRulesChanged(CollisionGroup.COLLISION_GROUP_TRIGGER);
                player.UnFreeze();
            }

            data.entity.Teleport(player.PlayerPawn.Value?.AbsOrigin, player.PlayerPawn.Value?.AbsRotation);
            Utils.PrintToChat(player, $"{ChatColors.Grey}Freeze: {(data.Frozen ? $"{ChatColors.Green}ON" : $"{ChatColors.Red}OFF")}");

            Open(player);
        });

        menu.AddItem("Decoy", (player, option) =>
        {
            data.Decoys--;

            var decoy = Utilities.CreateEntityByName<CPhysicsPropOverride>("prop_physics_override")!;

            decoy.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(uint)(1 << 2);
            decoy.SetModel(data.entity.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName);
            decoy.Teleport(data.entity.AbsOrigin, data.entity.AbsRotation);
            decoy.DispatchSpawn();

            Utils.PrintToChat(player, $"{ChatColors.Grey}Placed decoy. You have {data.Decoys} left");

            Open(player);
        }, data.Decoys == 0 ? DisableOption.DisableHideNumber : DisableOption.None);

        menu.AddItem("Taunt", (player, option) =>
        {
            data.Taunts--;
            var sounds = Instance.Config.Sounds.Taunt;
            player.EmitSound(sounds[Random.Shared.Next(sounds.Count)]);
            Utils.PrintToChat(player, $"{ChatColors.Grey}Used taunt. You have {data.Taunts} left");
            Open(player);
        }, data.Taunts == 0 ? DisableOption.DisableHideNumber : DisableOption.None);

        menu.AddItem("Swap", (player, option) =>
        {
            data.Swaps--;

            var models = Plugin.models;
            string model = models[Random.Shared.Next(models.Count)];
            if (data.entity != null && data.entity.IsValid)
                data.entity.SetModel(model);

            Utils.PrintToChat(player, $"{ChatColors.Grey}Swapped model. You have {data.Swaps} left");

            Open(player);
        }, data.Swaps == 0 ? DisableOption.DisableHideNumber : DisableOption.None);

        menu.Display(player, 0);
    }
}