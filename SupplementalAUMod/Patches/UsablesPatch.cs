using HarmonyLib;
using System;
using Hazel;
using UnityEngine;
using System.Linq;
using static AUMod.Roles;
using static AUMod.GameHistory;
using static AUMod.MapOptions;
using System.Collections.Generic;

namespace AUMod.Patches
{

    [HarmonyPatch(typeof(Vent), "CanUse")]
    public static class VentCanUsePatch {
        public static bool Prefix(Vent __instance,
            ref float __result,
            [HarmonyArgument(0)] GameData.PlayerInfo pc,
            [HarmonyArgument(1)] out bool canUse,
            [HarmonyArgument(2)] out bool couldUse)
        {
            if (pc.Role.Role == RoleTypes.Engineer) {
                canUse = true;
                couldUse = true;
                return true;
            }

            float num = float.MaxValue;
            PlayerControl @object = pc.Object;

            bool roleCouldUse = false;
            if (Madmate.canEnterVents && Madmate.madmate != null && Madmate.madmate == @object)
                roleCouldUse = true;
            else if (pc.Role.IsImpostor)
                roleCouldUse = true;

            var usableDistance = __instance.UsableDistance;

            couldUse = (@object.inVent || roleCouldUse) && !pc.IsDead && (@object.CanMove || @object.inVent);
            canUse = couldUse;
            if (canUse) {
                Vector2 truePosition = @object.GetTruePosition();
                Vector3 position = __instance.transform.position;
                num = Vector2.Distance(truePosition, position);

                canUse &= (num <= usableDistance && !PhysicsHelpers.AnythingBetween(truePosition, position, Constants.ShipOnlyMask, false));
            }
            __result = num;
            return false;
        }
    }

    [HarmonyPatch(typeof(VentButton), nameof(VentButton.DoClick))]
    public static class VentButtonDoClickPatch {
        public static bool Prefix(VentButton __instance)
        {
            if (__instance.currentTarget != null)
                __instance.currentTarget.Use();
            return false;
        }
    }

    [HarmonyPatch(typeof(Vent), "Use")]
    public static class VentUsePatch {
        public static bool Prefix(Vent __instance)
        {
            bool canUse;
            bool couldUse;
            __instance.CanUse(PlayerControl.LocalPlayer.Data, out canUse, out couldUse);
            if (!canUse)
                return false; // No need to execute the native method as using is disallowed anyways

            bool canMoveInVents = true;
            if (Madmate.madmate == PlayerControl.LocalPlayer) {
                canMoveInVents = false;
            }

            bool isEnter = !PlayerControl.LocalPlayer.inVent;
            if (isEnter) {
                PlayerControl.LocalPlayer.MyPhysics.RpcEnterVent(__instance.Id);
            } else {
                PlayerControl.LocalPlayer.MyPhysics.RpcExitVent(__instance.Id);
            }
            __instance.SetButtons(isEnter && canMoveInVents);
            return false;
        }
    }

    [HarmonyPatch(typeof(Vent), nameof(Vent.EnterVent))]
    public static class EnterVentAnimPatch {
        public static bool Prefix(Vent __instance, [HarmonyArgument(0)] PlayerControl pc)
        {
            return pc.AmOwner;
        }
    }

    [HarmonyPatch(typeof(Vent), nameof(Vent.ExitVent))]
    public static class ExitVentAnimPatch {
        public static bool Prefix(Vent __instance, [HarmonyArgument(0)] PlayerControl pc)
        {
            return pc.AmOwner;
        }
    }

    [HarmonyPatch(typeof(UseButton), nameof(UseButton.SetTarget))]
    public static class UseButtonSetTargetPatch {
        private static bool isVitals(string name)
        {
            return name == "panel_vitals";
        }

        private static bool isCameras(string name)
        {
            return name == "task_cams" || name == "Surv_Panel" || name == "SurvLogConsole" || name == "SurvConsole";
        }

        private static bool isBlocked(PlayerControl pc, IUsable target)
        {
            if (target == null)
                return false;

            SystemConsole targetSysConsole = target.TryCast<SystemConsole>();
            if (targetSysConsole != null) {
                if (!MapOptions.canUseVitals && isVitals(targetSysConsole.name))
                    return true;
                if (!MapOptions.canUseCameras && isCameras(targetSysConsole.name))
                    return true;
            }

            MapConsole targetMapConsole = target.TryCast<MapConsole>();
            if (targetMapConsole != null && !MapOptions.canUseAdmin)
                return true;
            return false;
        }

        public static bool Prefix(UseButton __instance, [HarmonyArgument(0)] IUsable target)
        {
            PlayerControl pc = PlayerControl.LocalPlayer;

            if (isBlocked(pc, target)) {
                __instance.currentTarget = null;
                __instance.enabled = false;
                __instance.graphic.color = Palette.DisabledClear;
                __instance.graphic.material.SetFloat("_Desat", 0f);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch]
    public static class ConsolePatch {
        private static bool MadmateCanUse(ref Console __instance)
        {
            return !__instance.TaskTypes.Any(x => x == TaskTypes.FixLights || (x == TaskTypes.FixComms && !Madmate.canFixComm));
        }

        [HarmonyPatch(typeof(Console), nameof(Console.CanUse))]
        public static class ConsoleCanUsePatch {
            public static bool Prefix(ref float __result,
                Console __instance,
                [HarmonyArgument(0)] GameData.PlayerInfo pc,
                [HarmonyArgument(1)] out bool canUse,
                [HarmonyArgument(2)] out bool couldUse)
            {
                canUse = couldUse = false;
                if (Madmate.madmate != null && Madmate.madmate == PlayerControl.LocalPlayer
                    && __instance.AllowImpostor)
                    return MadmateCanUse(ref __instance);
                if (__instance.AllowImpostor)
                    return true;
                if (!Helpers.hasFakeTasks(pc.Object))
                    return true;
                __result = float.MaxValue;
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(TuneRadioMinigame), nameof(TuneRadioMinigame.Begin))]
    class CommsMinigameBeginPatch {
        static void Postfix(TuneRadioMinigame __instance)
        {
            if (!Madmate.canFixComm && Madmate.madmate != null && Madmate.madmate == PlayerControl.LocalPlayer) {
                __instance.Close();
            }
        }
    }

    [HarmonyPatch(typeof(SwitchMinigame), nameof(SwitchMinigame.Begin))]
    class LightsMinigameBeginPatch {
        static void Postfix(SwitchMinigame __instance)
        {
            if (Madmate.madmate != null && Madmate.madmate == PlayerControl.LocalPlayer) {
                __instance.Close();
            }
        }
    }

    [HarmonyPatch]
    class AdminPanelPatch {
        static float adminTimer = 0f;
        public static bool isEvilHackerAdmin = false;

        static void ConsumeAdminTime()
        {
            if (isEvilHackerAdmin)
                return;
            if (PlayerControl.LocalPlayer.Data.IsDead)
                return;
            if (!MapOptions.canUseAdmin)
                return;

            // Consume the time via RPC
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)CustomRPC.ConsumeAdminTime,
                Hazel.SendOption.Reliable,
                -1);
            writer.Write(adminTimer);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
            RPCProcedure.consumeAdminTime(adminTimer);

            // reset timer
            adminTimer = 0f;
        }

        [HarmonyPatch(typeof(MapConsole), nameof(MapConsole.CanUse))]
        class MapConsoleCanUsePatch {
            public static bool Prefix(MapConsole __instance,
                ref float __result,
                [HarmonyArgument(0)] GameData.PlayerInfo pc,
                [HarmonyArgument(1)] out bool canUse,
                [HarmonyArgument(2)] out bool couldUse)
            {
                canUse = couldUse = false;
                return true;
            }
        }

        [HarmonyPatch(typeof(MapConsole), nameof(MapConsole.Use))]
        class MapConsoleUsePatch {
            public static bool Prefix(MapConsole __instance)
            {
                return MapOptions.canUseAdmin;
            }
        }

        [HarmonyPatch(typeof(MapCountOverlay), nameof(MapCountOverlay.OnDisable))]
        class MapCountOverlayOnDisablePatch {
            public static void Prefix(MapCountOverlay __instance)
            {
                ConsumeAdminTime();
                isEvilHackerAdmin = false;
            }
        }

        [HarmonyPatch(typeof(MapCountOverlay), nameof(MapCountOverlay.Update))]
        class MapCountOverlayUpdatePatch {
            static bool Prefix(MapCountOverlay __instance)
            {
                adminTimer += Time.deltaTime;
                if (adminTimer > 0.1f)
                    ConsumeAdminTime();

                if (!PlayerControl.LocalPlayer.Data.IsDead && !MapOptions.canUseAdmin && !isEvilHackerAdmin) {
                    __instance.isSab = true;
                    __instance.BackgroundColor.SetColor(Palette.DisabledGrey);
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(MapBehaviour), "get_IsOpenStopped")]
        class MapBehaviourGetIsOpenStoppedPatch {
            static bool Prefix(ref bool __result, MapBehaviour __instance)
            {
                if (EvilHacker.evilHacker != null && PlayerControl.LocalPlayer == EvilHacker.evilHacker) {
                    __result = false;
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(MapBehaviour), nameof(MapBehaviour.ShowSabotageMap))]
        class ShowSabotageMapPatch {
            static void Postfix(MapBehaviour __instance)
            {
                __instance.taskOverlay.Hide();
            }
        }
    }

    [HarmonyPatch]
    class CamerasPatch {
        static float camerasTimer = 0f;

        public static void ConsumeCamerasTime()
        {
            if (PlayerControl.LocalPlayer.Data.IsDead)
                return;
            if (!MapOptions.canUseCameras)
                return;

            // Consume the time via RPC
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)CustomRPC.ConsumeCamerasTime,
                Hazel.SendOption.Reliable,
                -1);
            writer.Write(camerasTimer);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
            RPCProcedure.consumeCamerasTime(camerasTimer);

            camerasTimer = 0f;
        }

        [HarmonyPatch]
        class SurveillanceMinigamePatch {
            [HarmonyPatch(typeof(SurveillanceMinigame), nameof(SurveillanceMinigame.Begin))]
            class SurveillanceMinigameBeginPatch {
                public static void Prefix(SurveillanceMinigame __instance)
                {
                    camerasTimer = 0f;
                }
            }

            [HarmonyPatch(typeof(SurveillanceMinigame), nameof(SurveillanceMinigame.Update))]
            class SurveillanceMinigameUpdatePatch {
                public static bool Prefix(SurveillanceMinigame __instance)
                {
                    camerasTimer += Time.deltaTime;
                    if (camerasTimer > 0.1f)
                        ConsumeCamerasTime();

                    if (!PlayerControl.LocalPlayer.Data.IsDead && !MapOptions.canUseCameras) {
                        __instance.Close();
                        return false;
                    }

                    return false;
                }
            }

            [HarmonyPatch(typeof(SurveillanceMinigame), nameof(SurveillanceMinigame.Close))]
            class SurveillanceMinigameClosePatch {
                static void Prefix(SurveillanceMinigame __instance)
                {
                    ConsumeCamerasTime();
                }
            }
        }

        [HarmonyPatch]
        class PlanetSurveillanceMinigamePatch {
            [HarmonyPatch(typeof(PlanetSurveillanceMinigame), nameof(PlanetSurveillanceMinigame.Begin))]
            class PlanetSurveillanceMinigameBeginPatch {
                public static void Prefix(PlanetSurveillanceMinigame __instance)
                {
                    camerasTimer = 0f;
                }
            }

            [HarmonyPatch(typeof(PlanetSurveillanceMinigame), nameof(PlanetSurveillanceMinigame.Update))]
            class PlanetSurveillanceMinigameUpdatePatch {
                public static bool Prefix(PlanetSurveillanceMinigame __instance)
                {
                    camerasTimer += Time.deltaTime;
                    if (camerasTimer > 0.1f)
                        ConsumeCamerasTime();

                    if (!PlayerControl.LocalPlayer.Data.IsDead && !MapOptions.canUseCameras) {
                        __instance.Close();
                        return false;
                    }

                    return false;
                }
            }

            [HarmonyPatch(typeof(PlanetSurveillanceMinigame), nameof(PlanetSurveillanceMinigame.Close))]
            class PlanetSurveillanceMinigameClosePatch {
                static void Prefix(PlanetSurveillanceMinigame __instance)
                {
                    ConsumeCamerasTime();
                }
            }
        }
    }

    [HarmonyPatch]
    class VitalsPatch {
        static float vitalsTimer = 0f;

        private static void ConsumeVitalsTime()
        {
            if (PlayerControl.LocalPlayer.Data.IsDead)
                return;
            if (!MapOptions.canUseVitals)
                return;

            // Consume the time via RPC
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)CustomRPC.ConsumeVitalsTime,
                Hazel.SendOption.Reliable,
                -1);
            writer.Write(vitalsTimer);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
            RPCProcedure.consumeVitalsTime(vitalsTimer);

            vitalsTimer = 0f;
        }

        [HarmonyPatch(typeof(VitalsMinigame), nameof(VitalsMinigame.Begin))]
        class VitalsMinigameBeginPatch {
            static void Postfix(VitalsMinigame __instance)
            {
                vitalsTimer = 0f;
            }
        }

        [HarmonyPatch(typeof(VitalsMinigame), nameof(VitalsMinigame.Update))]
        class VitalsMinigameUpdatePatch {
            static bool Prefix(VitalsMinigame __instance)
            {
                vitalsTimer += Time.deltaTime;
                if (vitalsTimer > 0.05f)
                    ConsumeVitalsTime();

                if (!PlayerControl.LocalPlayer.Data.IsDead && !MapOptions.canUseVitals) {
                    __instance.Close();
                    return false;
                }
                return true;
            }
        }
    }
}
