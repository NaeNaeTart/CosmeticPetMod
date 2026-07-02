using BepInEx.Configuration;
using UnityEngine;

namespace CosmeticPetMod
{
    public class ModConfig
    {
        public ConfigEntry<bool> ModEnabled { get; }
        public ConfigEntry<bool> SimpleMode { get; }
        public ConfigEntry<KeyCode> TogglePetKey { get; }
        public ConfigEntry<KeyCode> ToggleMenuKey { get; }
        public ConfigEntry<string> SelectedPetImage { get; }
        public ConfigEntry<float> PetScale { get; }
        public ConfigEntry<float> PetFollowSpeed { get; }
        public ConfigEntry<float> FollowDistance { get; }
        public ConfigEntry<float> WiggleSpeed { get; }
        public ConfigEntry<float> WiggleIntensity { get; }
        public ConfigEntry<float> PetHeightOffset { get; }
        public ConfigEntry<float> MaxGroundDistance { get; }
        public ConfigEntry<float> BobbingAmplitude { get; }
        public ConfigEntry<float> BobbingSpeed { get; }
        public ConfigEntry<float> SquishSmoothing { get; }
        public ConfigEntry<string> SelectedAudioPack { get; }
        public ConfigEntry<float> AudioMinInterval { get; }
        public ConfigEntry<float> AudioMaxInterval { get; }
        public ConfigEntry<float> AudioVolume { get; }

        public ModConfig(ConfigFile config)
        {
            ModEnabled = config.Bind("General", "ModEnabled", true, "Enable or disable the cosmetic pet mod.");
            SimpleMode = config.Bind("General", "SimpleMode", false, "If enabled, the pet ignores all geometry/collision checks and hovers/wiggles directly next to the player.");
            TogglePetKey = config.Bind("General", "TogglePetKey", KeyCode.P, "The keybind to toggle the pet visibility on or off.");
            ToggleMenuKey = config.Bind("General", "ToggleMenuKey", KeyCode.O, "The keybind to open the cosmetic pet settings menu.");
            SelectedPetImage = config.Bind("General", "SelectedPetImage", "default_slime.png", "The filename of the currently selected pet image from the Pets folder.");
            
            PetScale = config.Bind("Appearance", "PetScale", 1.0f, "The scale multiplier of the pet sprite.");
            PetFollowSpeed = config.Bind("Movement", "PetFollowSpeed", 3.0f, "How fast the pet moves to catch up with the player.");
            FollowDistance = config.Bind("Movement", "FollowDistance", 1.2f, "The distance the pet stays behind the player.");
            
            WiggleSpeed = config.Bind("Animation", "WiggleSpeed", 8.0f, "How fast the pet wiggles and squishes when walking.");
            WiggleIntensity = config.Bind("Animation", "WiggleIntensity", 15.0f, "The maximum tilt angle in degrees during the walking wiggle.");
            BobbingAmplitude = config.Bind("Animation", "BobbingAmplitude", 0.15f, "The vertical bobbing height amplitude of the pet when hovering/airborne.");
            BobbingSpeed = config.Bind("Animation", "BobbingSpeed", 3.5f, "The speed of the vertical bobbing animation when hovering/airborne.");
            
            PetHeightOffset = config.Bind("Physics", "PetHeightOffset", 0.4f, "The height offset of the pet above the ground.");
            MaxGroundDistance = config.Bind("Physics", "MaxGroundDistance", 8.0f, "The maximum vertical distance below the player that the pet will try to stick to the ground. If the ground is further than this, the pet will hover smoothly below the player.");
            SquishSmoothing = config.Bind("Physics", "SquishSmoothing", 6.0f, "How smoothly the ceiling squeezing factor interpolates when moving under different heights.");

            SelectedAudioPack = config.Bind("Audio", "SelectedAudioPack", "default_slime", "The folder name of the currently selected pet audio-pack from the AudioPacks folder.");
            AudioMinInterval = config.Bind("Audio", "AudioMinInterval", 15.0f, "Minimum duration in seconds between random pet audio/voice triggers.");
            AudioMaxInterval = config.Bind("Audio", "AudioMaxInterval", 45.0f, "Maximum duration in seconds between random pet audio/voice triggers.");
            AudioVolume = config.Bind("Audio", "AudioVolume", 0.8f, "Volume level of the pet's audio triggers.");
        }
    }
}
