using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using System.IO.Compression;
using ScavLib.gui.imgui;

namespace CosmeticPetMod
{
    public class CosmeticPetWindow : ImguiWindow
    {
        public override string Title => "Cosmetic Pet Mod Settings";

        public override KeyCode ToggleKey => Plugin.Cfg.ToggleMenuKey.Value;

        public override float Width => 380f;

        public override bool ShowInMenu => true;

        private string _customImagePath = "";
        private string _importMessage = "";
        private bool _importSuccess = true;
        private Vector2 _scrollPos;
        private Vector2 _audioScrollPos;
        private int _selectedTab = 0;

        // Thread-safe dispatch state variables
        private string? _pendingPngToLoad = null;
        private string? _pendingAudioPackToLoad = null;
        private readonly object _lock = new object();

        // Async feedback messages
        private string? _asyncImportMessage = null;
        private bool _asyncImportSuccess = true;

        protected override void DrawContent()
        {
            // Thread-safe dispatcher check on main thread
            string? pngToLoad = null;
            string? audioPackToLoad = null;
            string? asyncMsg = null;
            bool asyncSuccess = true;

            lock (_lock)
            {
                if (_pendingPngToLoad != null)
                {
                    pngToLoad = _pendingPngToLoad;
                    _pendingPngToLoad = null;
                }
                if (_pendingAudioPackToLoad != null)
                {
                    audioPackToLoad = _pendingAudioPackToLoad;
                    _pendingAudioPackToLoad = null;
                }
                if (_asyncImportMessage != null)
                {
                    asyncMsg = _asyncImportMessage;
                    asyncSuccess = _asyncImportSuccess;
                    _asyncImportMessage = null;
                }
            }

            if (pngToLoad != null)
            {
                try
                {
                    Plugin.Cfg.SelectedPetImage.Value = Path.GetFileName(pngToLoad);
                    Plugin.Instance.Config.Save();
                    PetController.Instance?.LoadPetFromFile(pngToLoad);
                    
                    _importSuccess = true;
                    _importMessage = "Successfully imported and selected " + Path.GetFileName(pngToLoad);
                }
                catch (System.Exception ex)
                {
                    _importSuccess = false;
                    _importMessage = "Failed to load imported PNG: " + ex.Message;
                }
            }

            if (audioPackToLoad != null)
            {
                try
                {
                    Plugin.Cfg.SelectedAudioPack.Value = audioPackToLoad;
                    Plugin.Instance.Config.Save();
                    PetController.Instance?.LoadAudioPack();
                    
                    _importSuccess = true;
                    _importMessage = "Successfully extracted and selected audio pack: " + audioPackToLoad;
                }
                catch (System.Exception ex)
                {
                    _importSuccess = false;
                    _importMessage = "Failed to load extracted audio pack: " + ex.Message;
                }
            }

            if (asyncMsg != null)
            {
                _importMessage = asyncMsg;
                _importSuccess = asyncSuccess;
            }

            GUILayout.Label($"Menu: {ToggleKey}   |   Toggle Pet: {Plugin.Cfg.TogglePetKey.Value}", "CenteredLabel");
            GUILayout.Space(5f);

            // Tab selection toolbar to keep the UI compact
            string[] tabs = { "General", "Movement", "Physics", "Audio" };
            _selectedTab = GUILayout.Toolbar(_selectedTab, tabs);
            GUILayout.Space(8f);

            if (_selectedTab == 0)
            {
                // Tab 0: General Settings & Image Import
                bool enabled = GUILayout.Toggle(Plugin.Cfg.ModEnabled.Value, " Enable Cosmetic Pet");
                if (enabled != Plugin.Cfg.ModEnabled.Value)
                {
                    Plugin.Cfg.ModEnabled.Value = enabled;
                }

                bool simpleMode = GUILayout.Toggle(Plugin.Cfg.SimpleMode.Value, " Simple Mode (No Collision)");
                if (simpleMode != Plugin.Cfg.SimpleMode.Value)
                {
                    Plugin.Cfg.SimpleMode.Value = simpleMode;
                }

                bool showMine = GUILayout.Toggle(Plugin.Cfg.ShowMyPet.Value, " Show My Pet (visible to you & others)");
                if (showMine != Plugin.Cfg.ShowMyPet.Value)
                {
                    Plugin.Cfg.ShowMyPet.Value = showMine;
                    Plugin.Instance.Config.Save();
                    MpPetManager.ForcePublishLocalPetSettings();
                }

                bool showOthers = GUILayout.Toggle(Plugin.Cfg.ShowOthersPets.Value, " Show Others' Pets");
                if (showOthers != Plugin.Cfg.ShowOthersPets.Value)
                {
                    Plugin.Cfg.ShowOthersPets.Value = showOthers;
                    Plugin.Instance.Config.Save();
                }

                GUILayout.Space(8f);
                GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1f));
                GUILayout.Space(4f);

                GUILayout.Label("Import / Select Pet Image", "BoldLabel");
                GUILayout.Space(4f);

                // Listing of Pets folder files
                string petsDir = Plugin.PetsDirectory;
                GUILayout.Label("Pets Folder: " + petsDir, "MiniLabel");
                
                if (Directory.Exists(petsDir))
                {
                    string[] files = Directory.GetFiles(petsDir, "*.png");
                    if (files.Length > 0)
                    {
                        GUILayout.Label("Select from installed pets:", "BoldLabel");
                        _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(90f));
                        foreach (string file in files)
                        {
                            string fileName = Path.GetFileName(file);
                            bool isSelected = Plugin.Cfg.SelectedPetImage.Value == fileName;
                            if (GUILayout.Toggle(isSelected, " " + fileName, "Button"))
                            {
                                if (!isSelected)
                                {
                                    Plugin.Cfg.SelectedPetImage.Value = fileName;
                                    PetController.Instance?.LoadPetFromFile(file);
                                }
                            }
                        }
                        GUILayout.EndScrollView();
                    }
                    else
                    {
                        GUILayout.Label("No PNG images found in Pets folder.", "MiniLabel");
                    }
                }

                GUILayout.Space(4f);

                // Import Custom Image via absolute path or direct URL
                GUILayout.Label("Import custom image by absolute path or URL:", "BoldLabel");
                _customImagePath = GUILayout.TextField(_customImagePath);
                
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Import Image File / URL"))
                {
                    string input = _customImagePath.Trim();
                    if (input.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase) ||
                        input.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            Plugin.Cfg.SelectedPetImage.Value = input;
                            Plugin.Instance.Config.Save();
                            PetController.Instance?.LoadConfiguredPet();
                            
                            _customImagePath = "";
                            _importSuccess = true;
                            _importMessage = "Successfully set custom pet URL! Downloading...";
                        }
                        catch (System.Exception ex)
                        {
                            _importSuccess = false;
                            _importMessage = "Error: " + ex.Message;
                        }
                    }
                    else if (File.Exists(input))
                    {
                        try
                        {
                            string fileName = Path.GetFileName(input);
                            string destPath = Path.Combine(petsDir, fileName);
                            
                            Directory.CreateDirectory(petsDir);
                            File.Copy(input, destPath, true);
                            
                            Plugin.Cfg.SelectedPetImage.Value = fileName;
                            Plugin.Instance.Config.Save();
                            PetController.Instance?.LoadPetFromFile(destPath);
                            
                            _customImagePath = "";
                            _importSuccess = true;
                            _importMessage = "Successfully imported " + fileName;
                        }
                        catch (System.Exception ex)
                        {
                            _importSuccess = false;
                            _importMessage = "Error: " + ex.Message;
                        }
                    }
                    else
                    {
                        _importSuccess = false;
                        _importMessage = "File does not exist or URL is invalid!";
                    }
                }

                if (GUILayout.Button("📁 Browse PNG File", GUILayout.Width(130f)))
                {
                    StartBrowsePngThread();
                }
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_importMessage))
                {
                    GUI.color = _importSuccess ? Color.green : Color.red;
                    GUILayout.Label(_importMessage, "MiniLabel");
                    GUI.color = Color.white;
                }
            }
            else if (_selectedTab == 1)
            {
                // Tab 1: Pet Customization (Movement & Animation)
                GUILayout.Label("Pet Customization", "BoldLabel");
                GUILayout.Space(5f);

                // Pet Scale
                GUILayout.Label($"Pet Scale: {Plugin.Cfg.PetScale.Value:F2}x");
                float scale = GUILayout.HorizontalSlider(Plugin.Cfg.PetScale.Value, 0.01f, 15.0f);
                if (scale != Plugin.Cfg.PetScale.Value) Plugin.Cfg.PetScale.Value = scale;

                // Follow Speed
                GUILayout.Label($"Follow Speed: {Plugin.Cfg.PetFollowSpeed.Value:F1}");
                float fSpeed = GUILayout.HorizontalSlider(Plugin.Cfg.PetFollowSpeed.Value, 0.05f, 50.0f);
                if (fSpeed != Plugin.Cfg.PetFollowSpeed.Value) Plugin.Cfg.PetFollowSpeed.Value = fSpeed;

                // Follow Distance
                GUILayout.Label($"Follow Distance: {Plugin.Cfg.FollowDistance.Value:F2} units");
                float fDist = GUILayout.HorizontalSlider(Plugin.Cfg.FollowDistance.Value, 0.0f, 20.0f);
                if (fDist != Plugin.Cfg.FollowDistance.Value) Plugin.Cfg.FollowDistance.Value = fDist;

                // Wiggle Speed
                GUILayout.Label($"Wiggle Speed: {Plugin.Cfg.WiggleSpeed.Value:F1}");
                float wSpeed = GUILayout.HorizontalSlider(Plugin.Cfg.WiggleSpeed.Value, 0.0f, 100.0f);
                if (wSpeed != Plugin.Cfg.WiggleSpeed.Value) Plugin.Cfg.WiggleSpeed.Value = wSpeed;

                // Wiggle Intensity
                GUILayout.Label($"Wiggle Intensity: {Plugin.Cfg.WiggleIntensity.Value:F1}°");
                float wIntensity = GUILayout.HorizontalSlider(Plugin.Cfg.WiggleIntensity.Value, 0.0f, 360.0f);
                if (wIntensity != Plugin.Cfg.WiggleIntensity.Value) Plugin.Cfg.WiggleIntensity.Value = wIntensity;
            }
            else if (_selectedTab == 2)
            {
                // Tab 2: Physics Settings
                GUILayout.Label("Physics & Auto-Leveling", "BoldLabel");
                GUILayout.Space(5f);

                // Hover Bobbing Amplitude
                GUILayout.Label($"Hover Bobbing Amplitude: {Plugin.Cfg.BobbingAmplitude.Value:F2} units");
                float bAmp = GUILayout.HorizontalSlider(Plugin.Cfg.BobbingAmplitude.Value, 0.0f, 2.0f);
                if (bAmp != Plugin.Cfg.BobbingAmplitude.Value) Plugin.Cfg.BobbingAmplitude.Value = bAmp;

                // Hover Bobbing Speed
                GUILayout.Label($"Hover Bobbing Speed: {Plugin.Cfg.BobbingSpeed.Value:F1}");
                float bSpeed = GUILayout.HorizontalSlider(Plugin.Cfg.BobbingSpeed.Value, 0.0f, 20.0f);
                if (bSpeed != Plugin.Cfg.BobbingSpeed.Value) Plugin.Cfg.BobbingSpeed.Value = bSpeed;

                // Height Offset
                GUILayout.Label($"Ground Height Offset: {Plugin.Cfg.PetHeightOffset.Value:F2} units");
                float hOffset = GUILayout.HorizontalSlider(Plugin.Cfg.PetHeightOffset.Value, -10.0f, 10.0f);
                if (hOffset != Plugin.Cfg.PetHeightOffset.Value) Plugin.Cfg.PetHeightOffset.Value = hOffset;

                // Max Ground Distance
                GUILayout.Label($"Max Ground Distance: {Plugin.Cfg.MaxGroundDistance.Value:F1} units");
                float maxGDist = GUILayout.HorizontalSlider(Plugin.Cfg.MaxGroundDistance.Value, 0.1f, 30.0f);
                if (maxGDist != Plugin.Cfg.MaxGroundDistance.Value) Plugin.Cfg.MaxGroundDistance.Value = maxGDist;

                // Squish Smoothing Speed
                GUILayout.Label($"Squish Smoothing Speed: {Plugin.Cfg.SquishSmoothing.Value:F1}");
                float sSmoothing = GUILayout.HorizontalSlider(Plugin.Cfg.SquishSmoothing.Value, 0.5f, 30.0f);
                if (sSmoothing != Plugin.Cfg.SquishSmoothing.Value) Plugin.Cfg.SquishSmoothing.Value = sSmoothing;
            }
            else if (_selectedTab == 3)
            {
                // Tab 3: Audio Settings & Audio Packs Select
                GUILayout.Label("Pet Audio Settings", "BoldLabel");
                GUILayout.Space(5f);

                // Volume
                GUILayout.Label($"Audio Volume: {Plugin.Cfg.AudioVolume.Value * 100f:F0}%");
                float aVolume = GUILayout.HorizontalSlider(Plugin.Cfg.AudioVolume.Value, 0.0f, 5.0f);
                if (aVolume != Plugin.Cfg.AudioVolume.Value) Plugin.Cfg.AudioVolume.Value = aVolume;

                // Min Interval
                GUILayout.Label($"Min Sound Interval: {Plugin.Cfg.AudioMinInterval.Value:F1} seconds");
                float aMin = GUILayout.HorizontalSlider(Plugin.Cfg.AudioMinInterval.Value, 1.0f, 120.0f);
                if (aMin != Plugin.Cfg.AudioMinInterval.Value) Plugin.Cfg.AudioMinInterval.Value = aMin;

                // Max Interval
                GUILayout.Label($"Max Sound Interval: {Plugin.Cfg.AudioMaxInterval.Value:F1} seconds");
                float aMax = GUILayout.HorizontalSlider(Plugin.Cfg.AudioMaxInterval.Value, 1.0f, 120.0f);
                if (aMax != Plugin.Cfg.AudioMaxInterval.Value) Plugin.Cfg.AudioMaxInterval.Value = aMax;

                GUILayout.Space(5f);

                // List of audio packs
                string audioDir = Plugin.AudioPacksDirectory;
                GUILayout.Label("Audio Packs Folder: " + audioDir, "MiniLabel");
                
                var packs = new System.Collections.Generic.List<string>();
                packs.Add("None");
                if (Directory.Exists(audioDir))
                {
                    foreach (var dir in Directory.GetDirectories(audioDir))
                    {
                        packs.Add(Path.GetFileName(dir));
                    }
                }

                GUILayout.Label("Select Audio Pack:", "BoldLabel");
                _audioScrollPos = GUILayout.BeginScrollView(_audioScrollPos, GUILayout.Height(90f));
                foreach (string pack in packs)
                {
                    bool isSelected = Plugin.Cfg.SelectedAudioPack.Value == pack;
                    if (GUILayout.Toggle(isSelected, " " + pack, "Button"))
                    {
                        if (!isSelected)
                        {
                            Plugin.Cfg.SelectedAudioPack.Value = pack;
                            Plugin.Instance.Config.Save();
                            PetController.Instance?.LoadAudioPack();
                        }
                    }
                }
                GUILayout.EndScrollView();

                GUILayout.Space(5f);
                if (GUILayout.Button("📁 Import Audio Pack ZIP"))
                {
                    StartBrowseAudioZipThread();
                }

                if (!string.IsNullOrEmpty(_importMessage))
                {
                    GUI.color = _importSuccess ? Color.green : Color.red;
                    GUILayout.Label(_importMessage, "MiniLabel");
                    GUI.color = Color.white;
                }
            }
        }

        // Spawns STA Thread for browsing a PNG file
        private void StartBrowsePngThread()
        {
            Thread t = new Thread(() =>
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Title = "Select Custom Pet Sprite (PNG)";
                    ofd.Filter = "PNG Sprite (*.png)|*.png";
                    ofd.Multiselect = false;

                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                            string srcPath = ofd.FileName;
                            string destName = Path.GetFileName(srcPath);
                            string destPath = Path.Combine(Plugin.PetsDirectory, destName);

                            Directory.CreateDirectory(Plugin.PetsDirectory);
                            File.Copy(srcPath, destPath, true);

                            lock (_lock)
                            {
                                _pendingPngToLoad = destPath;
                                _asyncImportSuccess = true;
                                _asyncImportMessage = "Selected file: " + destName;
                            }
                        }
                        catch (System.Exception ex)
                        {
                            lock (_lock)
                            {
                                _asyncImportSuccess = false;
                                _asyncImportMessage = "Import error: " + ex.Message;
                            }
                        }
                    }
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        // Spawns STA Thread for browsing and extracting an Audio ZIP pack
        private void StartBrowseAudioZipThread()
        {
            Thread t = new Thread(() =>
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Title = "Select Audio Pack (ZIP)";
                    ofd.Filter = "Audio Pack Archive (*.zip)|*.zip";
                    ofd.Multiselect = false;

                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                            string zipPath = ofd.FileName;
                            string packName = Path.GetFileNameWithoutExtension(zipPath);
                            string destPackDir = Path.Combine(Plugin.AudioPacksDirectory, packName);

                            Directory.CreateDirectory(Plugin.AudioPacksDirectory);
                            if (Directory.Exists(destPackDir))
                            {
                                Directory.Delete(destPackDir, true);
                            }
                            Directory.CreateDirectory(destPackDir);

                            // Extract the zip archive
                            ZipFile.ExtractToDirectory(zipPath, destPackDir);

                            lock (_lock)
                            {
                                _pendingAudioPackToLoad = packName;
                                _asyncImportSuccess = true;
                                _asyncImportMessage = "Successfully imported ZIP to pack: " + packName;
                            }
                        }
                        catch (System.Exception ex)
                        {
                            lock (_lock)
                            {
                                _asyncImportSuccess = false;
                                _asyncImportMessage = "ZIP extraction error: " + ex.Message;
                            }
                        }
                    }
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }
}
