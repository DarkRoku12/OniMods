﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using PeterHan.PLib.Options;
using static ProcGen.SubWorld;

namespace MapOverlay
{
    // Map Overlay mod for Oxygen Not Included
    // By Yannick M. Schmitt
    public class MapOverlay : OverlayModes.Mode
    {
        // Mod information
        public static readonly HashedString ID = nameof(MapOverlay);
        public const string Icon = "overlay_map";
        public const string Name = "Map Overlay";
        public const string Desc = "Displays a map view indicating positions of various POIs"; //"Displays a map view indicating positions of various POIs {Hotkey}"
        public const string Sound = "Temperature";
        public const string LocName = "MAPOVERLAY.TITLE";

        // Mode filters
        private const string FilterBiomes = "MapOverlayBiomes";
        private const string FilterBuildings = "MapOverlayBuildings";
        private const string FilterCritters = "MapOverlayCritters";
        private const string FilterGeysers = "MapOverlayGeysers";
        private const string FilterPlants = "MapOverlayPlants";
        private static string CurrentFilter = FilterGeysers;
        public static readonly Dictionary<string, string> Filters = new Dictionary<string, string>() { { FilterBiomes, "Biomes" }, { FilterBuildings, "Buildings" }, { FilterCritters, "Critters" }, { FilterGeysers, "Geysers" }, { FilterPlants, "Plants" } };

        // Maps of things to map on the map
        private static readonly Dictionary<string, MapOverlayEntry> ColorMap = new Dictionary<string, MapOverlayEntry>();
        private static readonly Dictionary<string, string> BiomeNameMap = new Dictionary<string, string>();
        private static readonly List<string> BuildingList = new List<string>() { "CryoTank", "ExobaseHeadquartersComplete", "GeneShuffler", "HeadquartersComplete", "MassiveHeatSinkComplete", "WarpConduitReceiverComplete", "WarpConduitSenderComplete", "WarpPortal", "WarpReceiver" };
        // TODO: possibly several other POIs (lockers, vending machines, satellites, Gravitas stuff, ...), all with tag "RocketOnGround", Beetafinery, ...

        // Tech stuff
        private static int WorldIdForLegend = -1;
        private static readonly SHA256 SHA256HashGenerator = SHA256.Create();
        private static readonly int TargetLayer = LayerMask.NameToLayer("MaskedOverlay");
        private static readonly int CameraLayerMask = LayerMask.GetMask("MaskedOverlay", "MaskedOverlayBG");
        private readonly List<KMonoBehaviour> LayerTargets = new List<KMonoBehaviour>();
        private readonly OverlayModes.ColorHighlightCondition[] HighlightConditions;
        private static MapOverlaySettings Settings;


        // Constructor
        public MapOverlay()
        {
            legendFilters = CreateDefaultFilters();

            Settings = POptions.ReadSettings<MapOverlaySettings>();

            // Setting up ONI's own background-coloring logic
            HighlightConditions = new OverlayModes.ColorHighlightCondition[] {
                new OverlayModes.ColorHighlightCondition(
                    highlight_condition: (obj) => (obj != null && obj.gameObject != null && obj.gameObject.name != null && ColorMap.ContainsKey(obj.gameObject.name)),
                    highlight_color: (obj) => ColorMap[obj.name].Color
                )
            };
        }

        // Detect the relevant MapEntry on the cell
        // Information gathered here is used both for building the overlay legend and actually highlighting the objects
        public static void ProcessCell(int cell)
        {
            GameObject building = Grid.Objects[cell, (int) ObjectLayer.Building];
            GameObject pickupable = Grid.Objects[cell, (int) ObjectLayer.Pickupables];

            if (IsCurrentFilter(FilterGeysers) && building != null && IsGeyserRevealed(cell) && building.GetComponent<Geyser>() != null)
            {
                UpdateMapEntry(building, building.GetComponent<Geyser>().configuration.GetElement());
            }
            else if (IsCurrentFilter(FilterGeysers) && building != null && IsGeyserRevealed(cell) && building.HasTag(GameTags.OilWell))
            {
                UpdateMapEntry(building, SimHashes.CrudeOil);
            }
            else if (IsCurrentFilter(FilterBuildings) && building != null && BuildingList.Contains(building.name))
            {
                UpdateMapEntry(building);
            }
            else if (IsCurrentFilter(FilterCritters) && pickupable != null && IsCritterRevealed(cell) && pickupable.HasTag(GameTags.Creature))
            {
                UpdateMapEntry(pickupable);
            }
            else if (IsCurrentFilter(FilterPlants) && building != null && building.HasTag(GameTags.Plant))
            {
                UpdateMapEntry(building);
                // TODO: Are sporechids no plants???
            }
            else if (IsCurrentFilter(FilterBiomes))
            {
                ZoneType biome = World.Instance.zoneRenderData.worldZoneTypes[cell];
                UpdateMapEntry(biome.ToString(), GetName(biome), biome);
            }
        }

        // Do not display highlight buried geysers unless explicitly requested (reason: can be considered cheaty, though there are in-game ways to find out)
        private static bool IsGeyserRevealed(int cell)
        {
            return (Settings.ShowBuriedGeysers || !Grid.Element[cell].IsSolid);
        }

        // Do not display highlight buried critters unless explicitly requested (reason: can be considered cheaty for shove voles, though there are in-game ways to find out)
        private static bool IsCritterRevealed(int cell)
        {
            return (Settings.ShowBuriedCritters || !Grid.Element[cell].IsSolid);
        }

        // Creates or extends a ColorMap entry, if necessary
        private static void UpdateMapEntry(GameObject go, System.Object colorReference = null)
        {
            UpdateMapEntry(go.name, go.GetProperName(), colorReference ?? go?.name, go);
        }

        // Creates or extends a ColorMap entry, if necessary
        private static void UpdateMapEntry(string key, string legend, System.Object colorReference, GameObject go = null)
        {
            if (!ColorMap.TryGetValue(key, out MapOverlayEntry entry))
            {
                entry = new MapOverlayEntry() { Name = legend, Color = GetColor(colorReference) };
                ColorMap.Add(key, entry);
            }

            if (go != null && !entry.GameObjects.ContainsKey(go.GetInstanceID()))
            {
                entry.GameObjects.Add(go.GetInstanceID(), go);
            }
        }

        // Get the color for an element, biome or object
        private static Color GetColor(System.Object obj)
        {
            Color color = Color.white;

            if (obj is SimHashes elementHash)
            {
                // Elements (e.g. neutronium or geyser outputs): Get the original color of the element
                color = ElementLoader.FindElementByHash(elementHash)?.substance?.uiColour ?? Color.clear;
                // Note: Alternatively, use element.substance.colour. But uiColor seems to be more distinctive, and neutronium is a bright pink instead of an easy-to-miss white.
            }
            else if (obj is ZoneType)
            {
                // Biomes: Get the original color for the biome
                color = World.Instance.GetComponent<SubworldZoneRenderData>().zoneColours[(int) obj];

                // Reset the alpha value so it can be shown nicely both in the map and the legend
                color.a = 1f;

                // No further adaption for already hand-picked colors, as this would merge e.g. Magma and Wasteland
                return color;
            }
            else if (obj is string name)
            {
                // Name strings: Get a random but stable color for a specific text
                // Using SHA256 is basically a random choice, but giving nicer colors for base game critters/plants than MD5 and SHA1
                byte[] hash = SHA256HashGenerator.ComputeHash(Encoding.UTF8.GetBytes(name));
                color = new Color32(hash[0], hash[1], hash[2], 255);
            }

            // Reduce palette (by rounding to .25) to make two very similar colors either reasonably different or completely the same so the legend entries will be merged
            color.r = (float) Math.Round(color.r * 4) / 4;
            color.g = (float) Math.Round(color.g * 4) / 4;
            color.b = (float) Math.Round(color.b * 4) / 4;

            // Brigthen up very dark colors that wouldn't be recognizable in the dark background
            // Tint them blue so black doesn't simply become dark grey
            if (color.maxColorComponent < 0.3f)
            {
                color.b += 0.25f;
                color.g += 0.1f;
                color.b += 0.1f;
            }

            return color;
        }

        // Get the name for a biome, for use in the overlay legend
        // Mapping is done manually (at least for now), as there is no in-game mapping between zone types (the only biome-related information given per cell, and also what the biome background coloring is based on) and the biome names.
        // The game knows biome names in the Spaced Out planetoid info box, but they are based on the subworld groups present on that planetoid, and those consist of several subworlds that have different zone types, so no 1:1 mapping from zone type to subworld group name is possible.
        // Note: subworld information incl. zone type is stored in SettingsCache.subworlds; names are in directory style (e.g. subworlds/frozen/FrozenCore), where the middle part could be used as a key to look up name in STRINGS.SUBWORLDS; subworld structure is in the StreamingAssets of the game data.
        private static string GetName(ZoneType biome)
        {

            switch( biome )
            {
                case ZoneType.FrozenWastes:
                    return STRINGS.SUBWORLDS.FROZEN.NAME;
                case ZoneType.CrystalCaverns:
                    return STRINGS.SUBWORLDS.NIOBIUM.NAME; // TODO: Check. Per YAML files, niobium subworld should actually be ZoneType.OilField and CrystalCaverns would remain unused.
                case ZoneType.BoggyMarsh:
                    return STRINGS.SUBWORLDS.MARSH.NAME;
                case ZoneType.Sandstone:
                    return STRINGS.SUBWORLDS.SANDSTONE.NAME;
                case ZoneType.ToxicJungle:
                    return STRINGS.SUBWORLDS.JUNGLE.NAME;
                case ZoneType.MagmaCore:
                    return STRINGS.SUBWORLDS.MAGMA.NAME;
                case ZoneType.OilField:
                    return STRINGS.SUBWORLDS.OIL.NAME;
                case ZoneType.Space:
                    return STRINGS.SUBWORLDS.SPACE.NAME;
                case ZoneType.Ocean:
                    return STRINGS.SUBWORLDS.OCEAN.NAME;
                case ZoneType.Rust:
                    return STRINGS.SUBWORLDS.RUST.NAME;
                case ZoneType.Forest:
                    return STRINGS.SUBWORLDS.FOREST.NAME;
                case ZoneType.Radioactive:
                    return STRINGS.SUBWORLDS.RADIOACTIVE.NAME;
                case ZoneType.Swamp:
                    return STRINGS.SUBWORLDS.SWAMP.NAME;
                case ZoneType.Wasteland:
                    return STRINGS.SUBWORLDS.WASTELAND.NAME;
                case ZoneType.Metallic:
                    return STRINGS.SUBWORLDS.METALLIC.NAME;
                case ZoneType.Barren:
                    return STRINGS.SUBWORLDS.BARREN.NAME;
                case ZoneType.Moo:
                    return STRINGS.SUBWORLDS.MOO.NAME;
                case ZoneType.IceCaves:
                    return STRINGS.SUBWORLDS.ICECAVES.NAME;
                case ZoneType.CarrotQuarry:
                    return STRINGS.SUBWORLDS.CARROTQUARRY.NAME;
                case ZoneType.SugarWoods:
                    return STRINGS.SUBWORLDS.SUGARWOODS.NAME;
                case ZoneType.RocketInterior:
                    return "Rocket Interior";
                case ZoneType.PrehistoricGarden:
                    return STRINGS.SUBWORLDS.GARDEN.NAME;
                case ZoneType.PrehistoricRaptor:
                    return STRINGS.SUBWORLDS.RAPTOR.NAME;
                case ZoneType.PrehistoricWetlands:
                    return STRINGS.SUBWORLDS.WETLANDS.NAME;
                default:
                    return "Unknown";
            }
        }

        // Build legend (this is done every time the overlay is opened or the mode is changed)
        public override List<LegendEntry> GetCustomLegendData()
        {
            List<LegendEntry> entries = new List<LegendEntry>();
            WorldIdForLegend = ClusterManager.Instance.activeWorldId;

            // Re-build ColorMap, so that only the relevant items are displayed (present and already discovered on this planetoid)
            ColorMap.Clear();

            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (Grid.IsActiveWorld(cell))
                {
                    ProcessCell(cell);
                }
            }

            // Collect the legend entries
            foreach (KeyValuePair<string, MapOverlayEntry> entry in ColorMap.OrderBy(e => STRINGS.UI.StripLinkFormatting(e.Value.Name.text)).ToList())
            {
                // If multiple entries with the same color exist, merge them in one legend entry
                LegendEntry existingLegendEntry = entries.Find(legend => legend.colour == entry.Value.Color);

                if (existingLegendEntry == null)
                {
                    entries.Add(new LegendEntry(GetLegendName(entry.Value), "", entry.Value.Color));
                }
                else
                {
                    existingLegendEntry.name = ((LocString) existingLegendEntry.name) + "\n" + GetLegendName(entry.Value);
                }
            }

            return entries;
        }

        // Get the name for an entry in the overlay legend
        // If setting to add count is used, add count for countable objects (not e.g. biomes)
        private string GetLegendName(MapOverlayEntry entry)
        {
            if (Settings.CountObjects && entry.GameObjects.Count > 0)
            {
                return $"{entry.Name.text} ({entry.GameObjects.Count})";
            }
            else
            {
                return entry.Name;
            }
        }

        // Enable overlay - init mask
        public override void Enable()
        {
            base.Enable();

            Camera.main.cullingMask |= CameraLayerMask;
            SelectTool.Instance.SetLayerMask(SelectTool.Instance.GetDefaultLayerMask() | TargetLayer);
        }

        // Disable overlay - reset mask, reset LayerTargets and highlighting
        public override void Disable()
        {
            base.Disable();

            DisableHighlightTypeOverlay<KMonoBehaviour>((ICollection<KMonoBehaviour>) LayerTargets);
            LayerTargets.Clear();

            Camera.main.cullingMask &= ~CameraLayerMask;
            SelectTool.Instance.ClearLayerMask();
        }

        // Update every frame - apply highlighting
        public override void Update()
        {
            if (WorldIdForLegend != ClusterManager.Instance.activeWorldId)
            {
                // New world showing - refresh legend (and by this, the ColorMap) to list only those objects present (and discovered) here
                OverlayLegend.Instance.SetLegend(this, true);
            }

            Vector2I origin = new Vector2I(0, 0);
            OverlayModes.Mode.RemoveOffscreenTargets<KMonoBehaviour>((ICollection<KMonoBehaviour>) LayerTargets, origin, origin);
            LayerTargets.AddRange(ColorMap.Values.SelectMany(entry => entry.GameObjects.Values).Select(go => go.GetComponent<KMonoBehaviour>()).OfType<KMonoBehaviour>());

            Grid.GetVisibleExtents(out Vector2I min, out Vector2I max);
            UpdateHighlightTypeOverlay<KMonoBehaviour>(min, max, (ICollection<KMonoBehaviour>) LayerTargets, null, HighlightConditions, OverlayModes.BringToFrontLayerSetting.Conditional, TargetLayer);
        }

        // Apply background coloring for biomes and neutronium
        public static Color GetBackgroundColor(int cell)
        {
            if (IsCurrentFilter(FilterGeysers) && Grid.Element[cell] != null && Grid.Element[cell].id.ToString().Equals("Unobtanium"))
            {
                return GetColor(SimHashes.Unobtanium);
            }
            else if (IsCurrentFilter(FilterBiomes) && ColorMap.TryGetValue(World.Instance.zoneRenderData.worldZoneTypes[cell].ToString(), out MapOverlayEntry entry))
            {
                return entry.Color;
            }

            return Color.black;
        }

        // Build legend filter sections
        public override Dictionary<string, ToolParameterMenu.ToggleState> CreateDefaultFilters()
        {
            Dictionary<string, ToolParameterMenu.ToggleState> filters = new Dictionary<string, ToolParameterMenu.ToggleState>();

            foreach (string mode in Filters.Keys)
            {
                filters.Add(mode, mode.Equals(CurrentFilter) ? ToolParameterMenu.ToggleState.On : ToolParameterMenu.ToggleState.Off);
            }

            return filters;
        }

        // Legend filter behaviour
        public override void OnFiltersChanged()
        {
            CurrentFilter = legendFilters.Where(entry => entry.Value == ToolParameterMenu.ToggleState.On).Select(entry => entry.Key).First();
        }

        // Check if we currently are in that filter mode
        private static bool IsCurrentFilter(string modeToCheck)
        {
            return CurrentFilter.Equals(modeToCheck);
        }

        // ID, as used internally by ONI to distinguish the overlays
        public override HashedString ViewMode()
        {
            return ID;
        }

        // Sound to play when opening the overlay
        public override string GetSoundName()
        {
            return Sound;
        }
    }
}

// TODO: Future improvement ideas
// - Print object name in the map
// - Make colors configurable, possibly also which objects to map
// - Optionally reveal even objects hidden behind POW?
// - Minimap version of the mod, so the map can be shown while playing

// Possible options:
// - Merge similar colors
// - Include secondary POIs (e.g. lockers) in buildings
// - Use UI colors for elements
// - Include eggs in the critters filter