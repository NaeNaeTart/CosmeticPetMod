# Cosmetic Pet Mod for Casualties Unknown

Welcome to the **Cosmetic Pet Mod**, a premium companion experience for *Casualties Unknown* with smooth physics-based following, advanced multiplayer synchronization, and full audio support!

---

## 🚀 Key Features & Mechanics

### 1. 🦘 Physics-Based Follow Behavior
- **Horizontal Lag**: Trails behind you at a configurable follow distance with smooth exponential interpolation — no rigid snapping.
- **Auto-Flipping**: Detects your facing direction (or movement delta) with a 150ms hysteresis lock to prevent rapid flutter, always looking the correct way.
- **Teleport Protection**: Two-tier guard — soft reset above 8 units, hard snap above 18 units — stops the pet from warping across the screen during fast movement.

### 2. 🌍 Auto-Leveling Ground System
- **Multi-Raycast Sampling**: Three rays span the pet's left, center, and right edges to bridge 1-tile gaps and step smoothly over terrain.
- **One-Way Platform Filtering**: Ignores pass-through platforms unless the player is standing on or very close to them.
- **Spring-Damper Suspension**: `Mathf.SmoothDamp` ground interpolation with a small threshold gate (`0.35u`) to suppress flat-ground micro-jitter while still gliding over stairs.
- **Item Ignore**: The pet sees through all dropped/physical world items — no snapping onto boxes or bags.

### 3. 🪟 Smart Ceiling Squishing
- **Lookahead Pre-Pass**: Ceiling scanning starts *before* the pet enters an overhang, triggered from a stable origin point above the player.
- **Accurate Clearance**: Measured from the pet's actual bottom edge, not the player origin.
- **Asymmetric Interpolation**: Compresses 2.5× faster than it expands for a satisfying jelly squish feel.
- **Minimum Squish Cap**: Pet can compress down to `0.15×` its height to squeeze through very tight gaps.
- **Ceiling-Aware Wall Rays**: Horizontal wall collision checks are clamped below detected ceilings so the pet can slide sideways without corner-snagging.

### 4. 💃 Walking Wiggle & Squash-and-Stretch
- **Tilt Wiggle**: `Mathf.Sin`-driven left/right rotation when walking.
- **Squash & Stretch**: Vertical compression and horizontal expansion synchronized to stride rhythm.

### 5. 🛸 Simple Mode (Noclip)
Toggle **Simple Mode** to bypass all collision checks entirely. The pet smoothly hovers and bobs alongside you, gliding freely through any geometry. Walk wiggles remain active. Useful if you experience physics issues in unusual map geometry.

### 6. 🔊 Audio Pack Support (Up to 500%)
Drop WAV, OGG, or MP3 files into an audio pack folder and the pet will randomly play them on a configurable interval:
- **500% Volume Cap**: Volume goes up to `5.0×` using `PlayOneShot` to bypass Unity's standard `1.0` clamp.
- **Configurable Intervals**: Set minimum and maximum seconds between sounds independently.

### 7. 🛡️ 100% Invincibility
A pure cosmetic companion — no colliders, no health, completely immune to game physics, enemies, and player collision.

---

## 🌐 Multiplayer Synchronization

An industry-grade P2P and Steam Lobby sync architecture so everyone in your session sees (and hears!) your pet perfectly.

### 👥 Personalized Follow Dynamics
All movement parameters (`Follow Speed`, `Follow Distance`, `Wiggle Speed`, etc.) are client-authoritative and broadcast via Steam lobby member metadata. Each player's pet moves with their chosen settings, independently per-player.

### 🖼️ Peer-to-Peer Custom PNG Transfer
When you load a custom local skin that other players don't have, the mod automatically transfers it:
1. Remote clients detect a new SHA1 hash via lobby metadata.
2. They issue a reliable P2P request on Channel `1337`.
3. You send the raw image bytes directly.
4. They cache it by hash in `Pets/Cache/` for instant loading next session.

### 🎵 Multiplayer Audio Sync
When your pet plays a sound, a tiny 6-byte P2P packet (type `2`, unreliable delivery) is broadcast to all lobby members. Each receiver plays the clip on their local copy of your pet's `AudioSource`, positioned in 3D space at the pet's location. If a player doesn't have your audio pack installed, they simply hear nothing — no crash, no error.

### 🤫 Anti-Jitter Motion Filtering
- Position deadzone `0.02u/frame` to suppress floating-point noise when standing still.
- Facing-direction flip locked behind `0.05u` movement delta and 150ms sustain timer.
- Low-pass velocity smoothing so walk animations start and stop organically.

---

## 🛠️ Settings & Customization

The mod integrates into **CU_ModSettings** (native game settings menu) when installed, and always provides a standalone **ImGui window**.

| Key | Action |
|-----|--------|
| `O` | Toggle the in-game settings window |
| `P` | Toggle pet visibility |

### Available Settings

| Setting | Description |
|---------|-------------|
| Enable Cosmetic Pet | Toggle the pet on/off |
| Simple Mode | Bypass all collision/geometry checks (noclip) |
| Pet Scale | Visual size multiplier |
| Follow Speed | How fast the pet catches up to you |
| Follow Distance | Ideal horizontal gap behind you |
| Wiggle Speed | Walking wiggle animation frequency |
| Wiggle Intensity | Max rotation angle while walking |
| Hover Bobbing Amplitude | Vertical bob range while airborne |
| Hover Bobbing Speed | Vertical bob frequency while airborne |
| Ground Height Offset | How high above the ground the pet rests |
| Max Ground Snapping Distance | Max depth below you the pet tracks ground |
| Squish Smoothing Speed | How fast ceiling squish transitions |
| Pet Audio Pack | Which audio pack folder to load sounds from |
| Audio Volume | Playback volume (0–500%) |
| Min Audio Interval | Minimum seconds between random sounds |
| Max Audio Interval | Maximum seconds between random sounds |
| Toggle Key | Keybind for hiding/showing pet |
| Toggle Menu Key | Keybind for opening the settings window |

### 📥 Importing Custom Sprites
**Method A — Drop files:**  
Place `.png` files into `BepInEx/plugins/CosmeticPetMod/Pets/` and they appear in the settings list immediately.

**Method B — In-game importer:**  
Paste a full file path or direct image URL into the settings window and click **Import Image File / URL**. The mod downloads, caches, and applies it mid-session.

### 🎵 Installing Audio Packs
Create a folder inside `BepInEx/plugins/CosmeticPetMod/AudioPacks/<pack_name>/` and place `.wav`, `.ogg`, or `.mp3` files inside. Select the pack in settings — the pet will play sounds randomly on its configured interval.

> **Note**: The mod no longer auto-creates these folders or generates placeholder files on startup. Create them manually when you want to add content.

---

## 📦 Dependencies

| Dependency | Required? | Purpose |
|------------|-----------|---------|
| [ScavLib](https://github.com/kanisuko/ScavLib) | **Required** | ImGui menu registration & Mod Registry |
| [CU_ModSettings](https://github.com/danimineiro/CU_ModSettings) | Optional (soft) | Native in-game settings page integration |

---

## 🗂️ Project File Structure

- **[PluginInfo.cs](PluginInfo.cs)**: GUID, name, and version constants.
- **[Plugin.cs](Plugin.cs)**: BepInEx entry point — registers with ScavLib, applies Harmony patches, sets up settings integrations.
- **[ModConfig.cs](ModConfig.cs)**: All BepInEx persistent config entries.
- **[PetController.cs](PetController.cs)**: Local pet physics, raycasting, collision, animation, audio playback, simple mode.
- **[MpPetManager.cs](MpPetManager.cs)**: Steam P2P skin transfer, lobby metadata sync, remote pet rendering, audio sync.
- **[CosmeticPetWindow.cs](CosmeticPetWindow.cs)**: Standalone ImGui settings window with sliders, dropdowns, and image importer.

---

## 📋 Changelog

### v1.1.0
- **Simple Mode**: Bypass all collision checks with smooth noclip hover follow
- **Improved Ground Snapping**: Threshold-gated rate limiter eliminates flat-ground stickiness; two-tier teleport protection prevents wild snapping
- **Overhauled Ceiling Squishing**: Early lookahead pre-pass, accurate bottom-edge clearance, asymmetric compression (2.5× faster), minimum cap `0.15×`, ceiling-aware wall rays
- **Item Ignore**: Pet no longer snaps to or collides with physical dropped items
- **500% Audio Volume**: `PlayOneShot` overload bypasses Unity's `1.0` clamp; sliders updated to `5.0` (500%)
- **Multiplayer Audio Sync**: Pet sounds broadcast via 6-byte P2P unreliable packet (type 2); remote pets play sounds positioned in 3D space
- **Removed ScavSetLib Dependency**: No longer requires `ScavSetLib.dll`; native settings fully handled by `CU_ModSettings`
- **No Auto-Created Folders**: `Pets/` and `AudioPacks/` directories are no longer created automatically on startup

### v1.0.0
- Initial release: physics follow, multi-raycast ground leveling, ceiling squish, walking wiggle, multiplayer skin sync via P2P, CU_ModSettings and ScavLib integration
