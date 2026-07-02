using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CosmeticPetMod
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.NAME, PluginInfo.VERSION)]
    [BepInProcess("CasualtiesUnknown.exe")]
    [BepInDependency("com.kanisuko.scavlib")] // Depend on ScavLib!
    [BepInDependency("me.danimineiro.modsettings", BepInDependency.DependencyFlags.SoftDependency)] // Soft depend on ModSettingsLib
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; } = null!;
        internal new static ManualLogSource Logger { get; private set; } = null!;
        private Harmony _harmony = null!;

        internal static ModConfig Cfg { get; private set; } = null!;
        public static string PetsDirectory { get; private set; } = "";
        public static string AudioPacksDirectory { get; private set; } = "";

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;

            // 1. Determine Pets and AudioPacks directories (do NOT auto-create them)
            string modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            PetsDirectory = Path.Combine(modDir, "CosmeticPetMod", "Pets");
            AudioPacksDirectory = Path.Combine(modDir, "CosmeticPetMod", "AudioPacks");

            // 2. Initialize Configuration
            Cfg = new ModConfig(Config);

            if (!Cfg.ModEnabled.Value)
            {
                Logger.LogInfo($"[{PluginInfo.NAME}] Initialized as disabled.");
            }

            // 3. Initialize Harmony for safe metadata registration
            _harmony = new Harmony(PluginInfo.GUID);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            // 4. Try to register settings and compatibility patches
            if (IsModSettingsInstalled())
            {
                try
                {
                    RegisterModSettings();
                    ApplyModSettingsCompatibilityPatches();
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning($"Failed to register native Mod Settings page or apply compatibility patches: {ex}");
                }
            }

            // 5. Register with ScavLib ModRegistry
            try
            {
                ScavLib.mods.ModRegistry.Register(new ScavLib.mods.ModInfo(
                    PluginInfo.NAME,
                    PluginInfo.VERSION,
                    "A cosmetic pet mod with smooth physics-following and walk wiggle animations.",
                    "Antigravity"
                ));
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning($"ScavLib ModRegistry registration failed: {ex.Message}");
            }

            // 6. Register GUI window in ScavLib
            try
            {
                ScavLib.gui.imgui.ImguiMenuManager.Register(new CosmeticPetWindow());
                Logger.LogInfo("Successfully registered CosmeticPetWindow with ScavLib ImguiMenuManager");
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning($"ScavLib ImguiMenuManager registration failed: {ex.Message}");
            }

            // 7. Add PetController MonoBehaviour
            gameObject.AddComponent<PetController>();

            Logger.LogInfo($"[{PluginInfo.NAME} v{PluginInfo.VERSION}] Loaded successfully!");
        }


        private bool IsModSettingsInstalled()
        {
            try
            {
                return BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("me.danimineiro.modsettings");
            }
            catch
            {
                return false;
            }
        }

        private void ApplyModSettingsCompatibilityPatches()
        {
            try
            {
                var targetMethod = AccessTools.Method("CU_ModSettings.HarmonyPatching.Patches.SettingsMenuPatches:SelectTabPatch");
                if (targetMethod != null)
                {
                    var prefixMethod = new HarmonyMethod(AccessTools.Method(typeof(ModSettingsCompatibilityPatches), nameof(ModSettingsCompatibilityPatches.SelectTabPatchPrefix)));
                    _harmony.Patch(targetMethod, prefix: prefixMethod);
                    Logger.LogInfo("Successfully applied compatibility prefix patch on CU_ModSettings SelectTabPatch");
                }
                else
                {
                    Logger.LogWarning("Could not find CU_ModSettings SelectTabPatch method to apply compatibility patch");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error applying Mod Settings compatibility patches: {ex}");
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void RegisterModSettings()
        {
            try
            {
                var pngFiles = new System.Collections.Generic.List<string>();
                if (Directory.Exists(PetsDirectory))
                {
                    foreach (var file in Directory.GetFiles(PetsDirectory, "*.png"))
                    {
                        pngFiles.Add(Path.GetFileName(file));
                    }
                }

                if (pngFiles.Count == 0)
                {
                    pngFiles.Add("default_slime.png");
                }

                int selectedImageIndex = pngFiles.IndexOf(Cfg.SelectedPetImage.Value);
                if (selectedImageIndex < 0) selectedImageIndex = 0;

                var audioPacks = new System.Collections.Generic.List<string>();
                audioPacks.Add("None");
                if (Directory.Exists(AudioPacksDirectory))
                {
                    foreach (var dir in Directory.GetDirectories(AudioPacksDirectory))
                    {
                        audioPacks.Add(Path.GetFileName(dir));
                    }
                }

                int selectedAudioPackIndex = audioPacks.IndexOf(Cfg.SelectedAudioPack.Value);
                if (selectedAudioPackIndex < 0) selectedAudioPackIndex = 0;

                var settingsList = new System.Collections.Generic.List<Setting>
                {
                    new SettingBool
                    {
                        name = "cosmeticpet.enabled",
                        value = Cfg.ModEnabled.Value,
                        apply = () => {
                            var s = Settings.Get<SettingBool>("cosmeticpet.enabled");
                            if (s != null)
                            {
                                Cfg.ModEnabled.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    },
                    new SettingBool
                    {
                        name = "cosmeticpet.simplemode",
                        value = Cfg.SimpleMode.Value,
                        apply = () => {
                            var s = Settings.Get<SettingBool>("cosmeticpet.simplemode");
                            if (s != null)
                            {
                                Cfg.SimpleMode.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    },
                    new SettingKeybind
                    {
                        name = "cosmeticpet.togglekey",
                        value = Cfg.TogglePetKey.Value,
                        apply = () => {
                            var s = Settings.Get<SettingKeybind>("cosmeticpet.togglekey");
                            if (s != null)
                            {
                                Cfg.TogglePetKey.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    },
                    new SettingKeybind
                    {
                        name = "cosmeticpet.togglemenukey",
                        value = Cfg.ToggleMenuKey.Value,
                        apply = () => {
                            var s = Settings.Get<SettingKeybind>("cosmeticpet.togglemenukey");
                            if (s != null)
                            {
                                Cfg.ToggleMenuKey.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    },
                    new SettingDropdown
                    {
                        name = "cosmeticpet.selectedimage",
                        choices = pngFiles.ToArray(),
                        value = selectedImageIndex,
                        apply = () => {
                            var s = Settings.Get<SettingDropdown>("cosmeticpet.selectedimage");
                            if (s != null && s.value >= 0 && s.value < pngFiles.Count)
                            {
                                Cfg.SelectedPetImage.Value = pngFiles[s.value];
                                Instance.Config.Save();
                                PetController.Instance.LoadConfiguredPet();
                            }
                        }
                    },
                    new SettingDropdown
                    {
                        name = "cosmeticpet.selectedaudiopack",
                        choices = audioPacks.ToArray(),
                        value = selectedAudioPackIndex,
                        apply = () => {
                            var s = Settings.Get<SettingDropdown>("cosmeticpet.selectedaudiopack");
                            if (s != null && s.value >= 0 && s.value < audioPacks.Count)
                            {
                                Cfg.SelectedAudioPack.Value = audioPacks[s.value];
                                Instance.Config.Save();
                                PetController.Instance.LoadAudioPack();
                            }
                        }
                    },
                    new SettingFloat
                    {
                        name = "cosmeticpet.scale",
                        value = Cfg.PetScale.Value,
                        min = 0.01f,
                        max = 15.0f,
                        formatValue = (val) => $"{val:F2}x",
                        apply = () => {
                            var s = Settings.Get<SettingFloat>("cosmeticpet.scale");
                            if (s != null)
                            {
                                Cfg.PetScale.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    },
                    new SettingFloat
                    {
                        name = "cosmeticpet.followspeed",
                        value = Cfg.PetFollowSpeed.Value,
                        min = 0.05f,
                        max = 50.0f,
                        formatValue = (val) => $"{val:F1}",
                        apply = () => {
                            var s = Settings.Get<SettingFloat>("cosmeticpet.followspeed");
                            if (s != null)
                            {
                                Cfg.PetFollowSpeed.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    },
                    new SettingFloat
                    {
                        name = "cosmeticpet.followdistance",
                        value = Cfg.FollowDistance.Value,
                        min = 0.0f,
                        max = 20.0f,
                        formatValue = (val) => $"{val:F2}",
                        apply = () => {
                            var s = Settings.Get<SettingFloat>("cosmeticpet.followdistance");
                            if (s != null)
                            {
                                Cfg.FollowDistance.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    },
                    new SettingFloat
                    {
                        name = "cosmeticpet.wigglespeed",
                        value = Cfg.WiggleSpeed.Value,
                        min = 0.0f,
                        max = 100.0f,
                        formatValue = (val) => $"{val:F1}",
                        apply = () => {
                            var s = Settings.Get<SettingFloat>("cosmeticpet.wigglespeed");
                            if (s != null)
                            {
                                Cfg.WiggleSpeed.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    },
                    new SettingFloat
                    {
                        name = "cosmeticpet.wiggleintensity",
                        value = Cfg.WiggleIntensity.Value,
                        min = 0.0f,
                        max = 360.0f,
                        formatValue = (val) => $"{val:F1}°",
                        apply = () => {
                            var s = Settings.Get<SettingFloat>("cosmeticpet.wiggleintensity");
                            if (s != null)
                            {
                                Cfg.WiggleIntensity.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    },
                    new SettingFloat
                    {
                        name = "cosmeticpet.heightoffset",
                        value = Cfg.PetHeightOffset.Value,
                        min = -10.0f,
                        max = 10.0f,
                        formatValue = (val) => $"{val:F2}",
                        apply = () => {
                            var s = Settings.Get<SettingFloat>("cosmeticpet.heightoffset");
                            if (s != null)
                            {
                                Cfg.PetHeightOffset.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    },
                    new SettingFloat
                    {
                        name = "cosmeticpet.maxgrounddistance",
                        value = Cfg.MaxGroundDistance.Value,
                        min = 0.1f,
                        max = 30.0f,
                        formatValue = (val) => $"{val:F1}",
                        apply = () => {
                            var s = Settings.Get<SettingFloat>("cosmeticpet.maxgrounddistance");
                            if (s != null)
                            {
                                Cfg.MaxGroundDistance.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    },
                    new SettingFloat
                    {
                        name = "cosmeticpet.audiovolume",
                        value = Cfg.AudioVolume.Value,
                        min = 0.0f,
                        max = 5.0f,
                        formatValue = (val) => $"{val * 100f:F0}%",
                        apply = () => {
                            var s = Settings.Get<SettingFloat>("cosmeticpet.audiovolume");
                            if (s != null)
                            {
                                Cfg.AudioVolume.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    },
                    new SettingFloat
                    {
                        name = "cosmeticpet.audiomininterval",
                        value = Cfg.AudioMinInterval.Value,
                        min = 1.0f,
                        max = 120.0f,
                        formatValue = (val) => $"{val:F1}s",
                        apply = () => {
                            var s = Settings.Get<SettingFloat>("cosmeticpet.audiomininterval");
                            if (s != null)
                            {
                                Cfg.AudioMinInterval.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    },
                    new SettingFloat
                    {
                        name = "cosmeticpet.audiomaxinterval",
                        value = Cfg.AudioMaxInterval.Value,
                        min = 1.0f,
                        max = 120.0f,
                        formatValue = (val) => $"{val:F1}s",
                        apply = () => {
                            var s = Settings.Get<SettingFloat>("cosmeticpet.audiomaxinterval");
                            if (s != null)
                            {
                                Cfg.AudioMaxInterval.Value = s.value;
                                Instance.Config.Save();
                            }
                        }
                    }
                };

                CU_ModSettings.ModSettingsPlugin.AddModSettingsPageDefaults("cosmeticpet.title", settingsList);
                Logger.LogInfo("Successfully registered CosmeticPetMod settings page in native Mod Settings menu.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to register native Mod Settings page: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }


    }

    [HarmonyPatch(typeof(Locale), nameof(Locale.LoadLanguage))]
    public static class LocalePatches
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                if (Locale.currentLang == null || Locale.currentLang.other == null) return;

                Dictionary<string, string> other = Locale.currentLang.other;

                other["cosmeticpet.title"] = "Cosmetic Pets";

                other["gamesetcosmeticpet.enabled"] = "Enable Mod";
                other["gamesetcosmeticpet.enabledsc"] = "Whether the cosmetic pet companion should be visible and follow you.";

                other["gamesetcosmeticpet.togglekey"] = "Toggle Visibility Key";
                other["gamesetcosmeticpet.togglekeysc"] = "Keyboard shortcut to quickly show or hide your pet companion.";

                other["gamesetcosmeticpet.togglemenukey"] = "Toggle Settings Menu Key";
                other["gamesetcosmeticpet.togglemenukeysc"] = "Keyboard shortcut to open or close the cosmetic pet in-game settings window.";

                other["gamesetcosmeticpet.selectedimage"] = "Select Pet Companion";
                other["gamesetcosmeticpet.selectedimagesc"] = "Choose which pet companion sprite to display from your local Pets/ folder.";

                other["gamesetcosmeticpet.scale"] = "Companion Scale";
                other["gamesetcosmeticpet.scalesc"] = "The scale size multiplier of your pet companion.";

                other["gamesetcosmeticpet.followspeed"] = "Follow Speed";
                other["gamesetcosmeticpet.followspeedsc"] = "How fast your pet catches up when you move.";

                other["gamesetcosmeticpet.followdistance"] = "Follow Distance";
                other["gamesetcosmeticpet.followdistancesc"] = "The ideal horizontal distance your pet keeps behind you.";

                other["gamesetcosmeticpet.wigglespeed"] = "Walking Wiggle Speed";
                other["gamesetcosmeticpet.wigglespeedsc"] = "How fast your pet wiggles and squishes when you walk.";

                other["gamesetcosmeticpet.wiggleintensity"] = "Walking Wiggle Angle";
                other["gamesetcosmeticpet.wiggleintensitysc"] = "The maximum rotational tilt angle of your pet's wiggling.";

                other["gamesetcosmeticpet.heightoffset"] = "Ground Height Offset";
                other["gamesetcosmeticpet.heightoffsetsc"] = "How high above the ground your pet companion hovers/stands.";

                other["gamesetcosmeticpet.maxgrounddistance"] = "Max Ground Snapping Distance";
                other["gamesetcosmeticpet.maxgrounddistancesc"] = "The maximum vertical distance below you that your pet companion will try to stick to the ground.";

                other["gamesetcosmeticpet.selectedaudiopack"] = "Pet Audio Pack";
                other["gamesetcosmeticpet.selectedaudiopacksc"] = "Select the audio pack that the pet plays sounds from.";

                other["gamesetcosmeticpet.audiovolume"] = "Pet Audio Volume";
                other["gamesetcosmeticpet.audiovolumesc"] = "How loud the pet plays its sounds/voice.";

                other["gamesetcosmeticpet.audiomininterval"] = "Min Audio Interval";
                other["gamesetcosmeticpet.audiominintervalsc"] = "Minimum time in seconds between random pet sounds.";

                other["gamesetcosmeticpet.audiomaxinterval"] = "Max Audio Interval";
                other["gamesetcosmeticpet.audiomaxintervalsc"] = "Maximum time in seconds between random pet sounds.";

                // Also populate translations for dropdown choices dynamically!
                if (Directory.Exists(Plugin.PetsDirectory))
                {
                    foreach (var file in Directory.GetFiles(Plugin.PetsDirectory, "*.png"))
                    {
                        string name = Path.GetFileName(file);
                        other["gamesetcosmeticpet.selectedimage" + name] = name;
                    }
                }

                if (Directory.Exists(Plugin.AudioPacksDirectory))
                {
                    other["gamesetcosmeticpet.selectedaudiopackNone"] = "None";
                    foreach (var dir in Directory.GetDirectories(Plugin.AudioPacksDirectory))
                    {
                        string name = Path.GetFileName(dir);
                        other["gamesetcosmeticpet.selectedaudiopack" + name] = name;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error in Locale.LoadLanguage patch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(SettingsMenu), "SelectTab", new Type[] { typeof(Setting.SettingCategory) })]
    public static class SettingsMenuSelectTabPatch
    {
        [HarmonyPostfix]
        public static void Postfix(SettingsMenu __instance, Setting.SettingCategory category)
        {
            ModSettingsCompatibilityPatches.OnSettingsMenuSelectTab(__instance, (int)category);
        }
    }

    public static class ModSettingsCompatibilityPatches
    {
        public static bool SelectTabPatchPrefix(int index)
        {
            try
            {
                var modSettingsPluginType = AccessTools.TypeByName("CU_ModSettings.ModSettingsPlugin");
                if (modSettingsPluginType != null)
                {
                    var tabIndexProp = modSettingsPluginType.GetProperty("MOD_SETTINGS_TAB_INDEX", BindingFlags.Public | BindingFlags.Static);
                    if (tabIndexProp != null)
                    {
                        int modSettingsTabIndex = (int)tabIndexProp.GetValue(null);
                        if (index == modSettingsTabIndex)
                        {
                            return true;
                        }
                    }

                    var translationKeysProp = modSettingsPluginType.GetProperty("ModNameTranslationKeys", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (translationKeysProp != null)
                    {
                        var keys = (List<string>)translationKeysProp.GetValue(null);
                        if (keys != null)
                        {
                            foreach (var key in keys)
                            {
                                if (StringComparer.Ordinal.GetHashCode(key) == index)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error in Mod Settings SelectTabPatchPrefix: {ex}");
            }
            return false;
        }

        public static void OnSettingsMenuSelectTab(SettingsMenu instance, int index)
        {
            try
            {
                if (instance == null || instance.buttons == null) return;

                bool isModSettingsIndex = false;
                var modSettingsPluginType = AccessTools.TypeByName("CU_ModSettings.ModSettingsPlugin");
                if (modSettingsPluginType != null)
                {
                    var tabIndexProp = modSettingsPluginType.GetProperty("MOD_SETTINGS_TAB_INDEX", BindingFlags.Public | BindingFlags.Static);
                    if (tabIndexProp != null)
                    {
                        int modSettingsTabIndex = (int)tabIndexProp.GetValue(null);
                        if (index == modSettingsTabIndex)
                        {
                            isModSettingsIndex = true;
                        }
                    }

                    if (!isModSettingsIndex)
                    {
                        var translationKeysProp = modSettingsPluginType.GetProperty("ModNameTranslationKeys", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (translationKeysProp != null)
                        {
                            var keys = (List<string>)translationKeysProp.GetValue(null);
                            if (keys != null)
                            {
                                foreach (var key in keys)
                                {
                                    if (StringComparer.Ordinal.GetHashCode(key) == index)
                                    {
                                        isModSettingsIndex = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                for (int i = 0; i < instance.buttons.Count; i++)
                {
                    var btn = instance.buttons[i];
                    if (btn == null) continue;
                    var image = btn.GetComponent<UnityEngine.UI.Image>();
                    if (image == null) continue;

                    var textComp = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                    string text = textComp != null ? textComp.text : "";

                    if (isModSettingsIndex)
                    {
                        if (text == "Mod Settings")
                        {
                            image.sprite = instance.buttonOpen;
                        }
                        else
                        {
                            image.sprite = instance.buttonClosed;
                        }
                    }
                    else
                    {
                        if (text == "Mod Settings")
                        {
                            image.sprite = instance.buttonClosed;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error in OnSettingsMenuSelectTab: {ex}");
            }
        }
    }
}
