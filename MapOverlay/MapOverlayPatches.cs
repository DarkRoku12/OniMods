﻿using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using PeterHan.PLib.Options;
using PeterHan.PLib.Core;

namespace MapOverlay
{
    public class MapOverlayPatches : KMod.UserMod2 {
        // Mod entry point
        public override void OnLoad(Harmony harmony)
        {
            PUtil.InitLibrary(true);
            new POptions().RegisterOptions(this, typeof(MapOverlaySettings));

            base.OnLoad(harmony);
        }

        // Add assets needed for the mod to the game
        [HarmonyPatch(typeof(Db), "Initialize")]
        public static class Db_Initialize_Patch
        {
            public static void Postfix()
            {
                // Add sprite for overlay icon
                AddSpriteFromFile(MapOverlay.Icon);

                // Add translations (some of them needed as ONI would otherwise display MISSING.STRINGS)
                foreach (KeyValuePair<string, string> entry in MapOverlay.Filters)
                {
                    Strings.Add( $"STRINGS.UI.TOOLS.FILTERLAYERS.{entry.Key}.NAME" , entry.Value);
                    Strings.Add( $"STRINGS.UI.TOOLS.FILTERLAYERS.{entry.Key}.DESC" , entry.Value );
                    Strings.Add( $"STRINGS.UI.TOOLS.FILTERLAYERS.{entry.Key}.TOOLTIP" , entry.Value );
                }
                
                Strings.Add(MapOverlay.LocName, MapOverlay.Name);
            }

            private static void AddSpriteFromFile(string name)
            {
                string filename = Path.Combine(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assets"), $"{name}.png");
                Texture2D texture = null;

                try
                {
                    byte[] data = File.ReadAllBytes(filename);
                    texture = new Texture2D(1, 1);
                    texture.LoadImage(data);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Could not load texture at {filename}");
                    Debug.LogException(e);
                }

                Assets.Sprites.Add(name, Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(texture.width / 2f, texture.height / 2f)));
            }
        }

        // Register the overlay, part 1
        [HarmonyPatch(typeof(OverlayMenu), "InitializeToggles")]
        public static class OverlayMenu_InitializeToggles_Patch
        {
            public static void Postfix(List<KIconToggleMenu.ToggleInfo> ___overlayToggleInfos)
            {
                var constructor = AccessTools.Constructor(
                    AccessTools.Inner(typeof(OverlayMenu), "OverlayToggleInfo"),
                    new[] {
                        typeof(string), // text
                        typeof(string), // icon_name
                        typeof(HashedString), // sim_view
                        typeof(string), // required_tech_item
                        typeof(Action), // hotKey
                        typeof(string), // tooltip
                        typeof(string) // tooltip_header
                    }
                );

                var obj = constructor.Invoke(
                    new object[] {
                        MapOverlay.Name,
                        MapOverlay.Icon,
                        MapOverlay.ID,
                        "",
                        Action.NumActions, //POptions.ReadSettings<MapOverlaySettings>().Hotkey, // TODO: Not working if already used for anything else. Also, the Action enum destroys the layout of the options dialog.
                        MapOverlay.Desc,
                        MapOverlay.Name
                    }
                );

                ___overlayToggleInfos.Add((KIconToggleMenu.ToggleInfo) obj);
            }
        }

        // Register the overlay, part 2
        [HarmonyPatch(typeof(OverlayScreen), "RegisterModes")]
        public static class OverlayScreen_RegisterModes_Patch
        {
            public static void Postfix(OverlayScreen __instance)
            {
                 Traverse.Create(__instance).Method("RegisterMode", new MapOverlay()).GetValue();
            }
        }

        // Register the overlay, part 3
        [HarmonyPatch(typeof(StatusItem), "GetStatusItemOverlayBySimViewMode")]
        public static class StatusItem_GetStatusItemOverlayBySimViewMode_Patch
        {
            public static void Prefix(Dictionary<HashedString, StatusItem.StatusItemOverlays> ___overlayBitfieldMap)
            {
                if (!___overlayBitfieldMap.ContainsKey(MapOverlay.ID))
                {
                    ___overlayBitfieldMap.Add(MapOverlay.ID, StatusItem.StatusItemOverlays.None);
                }
            }
        }

        // Enable the overlay legend
        [HarmonyPatch(typeof(OverlayLegend), "OnSpawn")]
        public static class OverlayLegend_OnSpawn_Patch
        {
            public static void Prefix(OverlayLegend __instance)
            {
                var instance = Traverse.Create(__instance);

                if (instance.Field("overlayInfoList").FieldExists() && instance.Field("overlayInfoList").GetValue<List<OverlayLegend.OverlayInfo>>() != null)
                {
                    var info = new OverlayLegend.OverlayInfo
                    {
                        name = MapOverlay.LocName,
                        mode = MapOverlay.ID,
                        infoUnits = new List<OverlayLegend.OverlayInfoUnit>(),
                        isProgrammaticallyPopulated = true
                    };

                    instance.Field("overlayInfoList").GetValue<List<OverlayLegend.OverlayInfo>>().Add(info);
                }
            }
        }

        // Color the cells
        [HarmonyPatch(typeof(SimDebugView), "OnPrefabInit")]
        public static class SimDebugView_OnPrefabInit_Patch
        {
            public static void Postfix(Dictionary<HashedString, Func<SimDebugView, int, Color>> ___getColourFuncs)
            {
                ___getColourFuncs.Add(MapOverlay.ID, (instance, cell) => MapOverlay.GetBackgroundColor(cell));
            }
        }
    }
}