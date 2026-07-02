using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Steamworks;
using KrokoshaCasualtiesMP;

namespace CosmeticPetMod
{
    public static class MpPetManager
    {
        private class RemotePetInstance
        {
            public Body OwnerBody = null!;
            public ulong SteamId;
            public string PlayerName = "";
            public GameObject? PetVisuals;
            public SpriteRenderer? SpriteRenderer;

            public Vector3 LastOwnerPos;
            public float WiggleTimer;
            public bool FacingRight = true;
            public string LoadedPetSource = "";
            public string LoadedPetHash = "";

            // Custom companion settings synchronized from lobby member data
            public float PetScale = 1.0f;
            public float WiggleSpeed = 8.0f;
            public float WiggleIntensity = 15.0f;
            public float PetHeightOffset = 0.4f;

            // Personalized follow settings synced from lobby
            public float FollowDistance = 1.2f;
            public float FollowSpeed = 3.0f;
            public float MaxGroundDistance = 3.0f;

            // Advanced auto-leveling custom variables synced from lobby
            public float BobbingAmplitude = 0.15f;
            public float BobbingSpeed = 3.5f;
            public float SquishSmoothing = 6.0f;
            public float CurrentSquishY = 1.0f;

            // LOW-PASS filter state to smooth animation triggers
            public float SmoothMovementSpeed = 0f;
            public float YVelocity;
            public float DirectionLockTimer;
            public bool LastPendingFacingRight = true;

            // Audio settings synchronized from lobby member data
            public string AudioPackName = "None";
            public float AudioVolume = 1.0f;
            public AudioSource? AudioSource;
            public List<AudioClip> AudioClips = new List<AudioClip>();
            public string LoadedAudioPackName = "";
        }

        private static readonly Dictionary<Body, RemotePetInstance> _remotePets = new Dictionary<Body, RemotePetInstance>();
        private static readonly HashSet<string> _downloadingUrls = new HashSet<string>();
        private static float _lastPublishTime;
        private static Callback<P2PSessionRequest_t>? _sessionRequestCallback;

        public static void UpdateMultiplayerPets()
        {
            // 0. Poll and process custom pet skin transfer P2P messages
            ReceiveP2PPackets();

            // 1. Periodically publish local pet settings to the Steam lobby
            PublishLocalPetSettings();

            // 2. Clean up if not in a lobby
            if (!KSteam.IS_IN_LOBBY)
            {
                if (_remotePets.Count > 0)
                {
                    ClearRemotePets();
                }
                return;
            }

            // 3. If the player has disabled seeing others' pets, clear and return
            if (!Plugin.Cfg.ShowOthersPets.Value)
            {
                if (_remotePets.Count > 0) ClearRemotePets();
                return;
            }

            var allNetBodies = NetBody.all_instances;
            if (allNetBodies == null) return;

            var activeOwners = new HashSet<Body>();

            // 3. Track and update remote players' pet companions
            foreach (var netBody in allNetBodies)
            {
                if (netBody == null || netBody.body == null) continue;
                if (netBody.IsBodyLocal()) continue;

                Body owner = netBody.body;
                activeOwners.Add(owner);

                if (!_remotePets.TryGetValue(owner, out var petInstance))
                {
                    ulong steamId = 0;
                    string name = netBody.playername ?? "Player";

                    if (netBody.plr != null)
                    {
                        steamId = netBody.plr.steam_id;
                    }

                    petInstance = new RemotePetInstance
                    {
                        OwnerBody = owner,
                        SteamId = steamId,
                        PlayerName = name,
                        LastOwnerPos = owner.transform.position
                    };

                    _remotePets[owner] = petInstance;
                    Plugin.Logger.LogInfo($"Tracking remote companion for player: {name} (SteamID: {steamId})");
                }

                UpdateRemotePet(petInstance);
            }

            // 4. Clean up companions for disconnected players
            var toRemove = new List<Body>();
            foreach (var pair in _remotePets)
            {
                if (pair.Key == null || !activeOwners.Contains(pair.Key!))
                {
                    if (pair.Value.PetVisuals != null)
                    {
                        UnityEngine.Object.Destroy(pair.Value.PetVisuals);
                    }
                    toRemove.Add(pair.Key!);
                    Plugin.Logger.LogInfo($"Destroyed remote companion for disconnected player: {pair.Value.PlayerName}");
                }
            }
            foreach (var r in toRemove)
            {
                _remotePets.Remove(r);
            }
        }

        public static void PublishLocalPetSettings()
        {
            if (Time.time - _lastPublishTime < 5f) return;
            _lastPublishTime = Time.time;

            if (KSteam.IS_IN_LOBBY)
            {
                try
                {
                    CSteamID lobbyId = KSteam.lobbyId;
                    string petImage = Plugin.Cfg.SelectedPetImage.Value;
                    string petHash = "";

                    if (!string.IsNullOrEmpty(petImage) &&
                        !petImage.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !petImage.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        string localPath = Path.Combine(Plugin.PetsDirectory, petImage);
                        petHash = GetFileSha1(localPath);
                    }

                    // Publish settings as lobby member metadata
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_image", petImage);
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_hash", petHash);
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_scale", Plugin.Cfg.PetScale.Value.ToString(CultureInfo.InvariantCulture));
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_wiggle_speed", Plugin.Cfg.WiggleSpeed.Value.ToString(CultureInfo.InvariantCulture));
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_wiggle_intensity", Plugin.Cfg.WiggleIntensity.Value.ToString(CultureInfo.InvariantCulture));
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_height_offset", Plugin.Cfg.PetHeightOffset.Value.ToString(CultureInfo.InvariantCulture));
                    
                    // Publish personalized movement/follow settings
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_follow_speed", Plugin.Cfg.PetFollowSpeed.Value.ToString(CultureInfo.InvariantCulture));
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_follow_distance", Plugin.Cfg.FollowDistance.Value.ToString(CultureInfo.InvariantCulture));
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_max_ground_distance", Plugin.Cfg.MaxGroundDistance.Value.ToString(CultureInfo.InvariantCulture));

                    // Publish advanced auto-leveling settings
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_bobbing_amplitude", Plugin.Cfg.BobbingAmplitude.Value.ToString(CultureInfo.InvariantCulture));
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_bobbing_speed", Plugin.Cfg.BobbingSpeed.Value.ToString(CultureInfo.InvariantCulture));
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_squish_smoothing", Plugin.Cfg.SquishSmoothing.Value.ToString(CultureInfo.InvariantCulture));

                    // Publish audio settings
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_audiopack", Plugin.Cfg.SelectedAudioPack.Value);
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_audiovolume", Plugin.Cfg.AudioVolume.Value.ToString(CultureInfo.InvariantCulture));

                    // Publish own pet visibility so others know whether to render it
                    SteamMatchmaking.SetLobbyMemberData(lobbyId, "cosmetic_pet_visible",
                        Plugin.Cfg.ShowMyPet.Value ? "true" : "false");
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"Failed to publish local pet settings to Steam lobby: {ex.Message}");
                }
            }
        }

        public static void ForcePublishLocalPetSettings()
        {
            _lastPublishTime = 0f;
            PublishLocalPetSettings();
        }

        public static void ClearRemotePets()
        {
            foreach (var pet in _remotePets.Values)
            {
                if (pet.PetVisuals != null)
                {
                    UnityEngine.Object.Destroy(pet.PetVisuals);
                }
            }
            _remotePets.Clear();
            Plugin.Logger.LogInfo("Cleared all remote multiplayer pet companions.");
        }

        private static void UpdateRemotePet(RemotePetInstance pet)
        {
            if (pet.OwnerBody == null) return;

            // 1. Sync configurations from the lobby
            FetchLobbyPetData(pet);

            // 2. Initialize pet visuals if not present
            if (pet.PetVisuals == null)
            {
                pet.PetVisuals = new GameObject($"CosmeticPet_Mp_{pet.PlayerName}");
                pet.PetVisuals.transform.SetParent(PetController.Instance.transform);

                pet.SpriteRenderer = pet.PetVisuals.AddComponent<SpriteRenderer>();
                pet.SpriteRenderer.sortingOrder = 30;

                pet.PetVisuals.transform.position = pet.OwnerBody.transform.position;
                pet.LastOwnerPos = pet.OwnerBody.transform.position;

                LoadRemotePetSprite(pet);
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 ownerPos = pet.OwnerBody.transform.position;

            // 0. Prevent teleportation lags and mountain clip-throughs (dynamic based on synced follow distance)
            float followDist = pet.FollowDistance;
            float teleportThreshold = Mathf.Max(15.0f, followDist + 5.0f);
            if (Vector3.Distance(ownerPos, pet.PetVisuals.transform.position) > teleportThreshold)
            {
                pet.PetVisuals.transform.position = ownerPos;
            }

            // 3. Determine owner facing direction with hysteresis to prevent rapid mirroring flutter
            bool pendingFacingRight = pet.FacingRight;
            float scaleX = pet.OwnerBody.transform.localScale.x;
            if (Mathf.Abs(scaleX) > 0.01f)
            {
                pendingFacingRight = scaleX > 0f;
            }
            else
            {
                float dx = ownerPos.x - pet.LastOwnerPos.x;
                if (Mathf.Abs(dx) > 0.05f) // Increased deadzone to 0.05f to prevent rapid mirroring/flipping jitter in place
                {
                    pendingFacingRight = dx > 0f;
                }
            }

            if (pendingFacingRight != pet.FacingRight)
            {
                if (pendingFacingRight != pet.LastPendingFacingRight)
                {
                    pet.LastPendingFacingRight = pendingFacingRight;
                    pet.DirectionLockTimer = 0f;
                }

                pet.DirectionLockTimer += dt;
                if (pet.DirectionLockTimer >= 0.15f) // Sustained facing change for 150ms
                {
                    pet.FacingRight = pendingFacingRight;
                }
            }
            else
            {
                pet.DirectionLockTimer = 0f;
            }

            // 4. Smooth horizontal movement (personalized follow distance & follow speed!)
            float targetX = ownerPos.x + (pet.FacingRight ? -followDist : followDist);

            float followSpeed = pet.FollowSpeed;
            float currentX = pet.PetVisuals.transform.position.x;
            float newX = Mathf.Lerp(currentX, targetX, dt * followSpeed);

            // 5. Scale-independent horizontal wall collision clamping (three-point check)
            float petScale = pet.PetScale;
            float halfWidth = 0.4f * petScale;
            float visualHeight = (pet.SpriteRenderer != null && pet.SpriteRenderer.sprite != null) ? 
                (pet.SpriteRenderer.sprite.rect.height / pet.SpriteRenderer.sprite.pixelsPerUnit) * petScale : 2.4f * petScale;

            float dist = Mathf.Abs(newX - currentX);
            if (dist > 0.001f)
            {
                float dirX = Mathf.Sign(newX - currentX);
                float rayDist = dist + halfWidth;

                // Check at bottom, center, and top heights of the sprite's current squished visual volume
                float[] checkHeights = new float[] { 
                    pet.PetVisuals.transform.position.y - (visualHeight * pet.CurrentSquishY * 0.35f), 
                    pet.PetVisuals.transform.position.y, 
                    pet.PetVisuals.transform.position.y + (visualHeight * pet.CurrentSquishY * 0.35f) 
                };

                foreach (float y in checkHeights)
                {
                    RaycastHit2D hit = Physics2D.Raycast(new Vector2(currentX, y), new Vector2(dirX, 0f), rayDist);
                    if (hit.collider != null && !hit.collider.isTrigger)
                    {
                        Body hitBody = hit.collider.GetComponentInParent<Body>();
                        if (hitBody == null)
                        {
                            // Clamp newX to stop exactly at the block edge
                            newX = hit.point.x - dirX * halfWidth;
                            
                            // Recalculate remaining distance for subsequent height checks
                            dist = Mathf.Abs(newX - currentX);
                            rayDist = dist + halfWidth;
                        }
                    }
                }
            }

            // 6. Smart Ground & Ceiling detection using Multi-Raycast (Left, Center, Right) on the clamped horizontal coordinate
            float[] sampleX = new float[] { newX - halfWidth, newX, newX + halfWidth };
            float highestGroundY = float.MinValue;
            bool foundGround = false;

            // Track if we found a ceiling above any of our sample points to use for squishing
            float lowestCeilingY = float.MaxValue;
            bool foundAnyCeiling = false;

            foreach (float x in sampleX)
            {
                // First, find if there is a ceiling above this sample point
                float ceilingY = float.MaxValue;
                bool foundCeiling = false;
                RaycastHit2D[] ceilHits = Physics2D.RaycastAll(new Vector2(x, ownerPos.y + 0.8f), Vector2.up, 5.0f);
                foreach (var hit in ceilHits)
                {
                    if (hit.collider != null && !hit.collider.isTrigger)
                    {
                        Body hitBody = hit.collider.GetComponentInParent<Body>();
                        if (hitBody == null)
                        {
                            float hitY = hit.point.y;
                            if (hitY >= ownerPos.y + 1.0f)
                            {
                                ceilingY = hitY;
                                foundCeiling = true;
                                if (ceilingY < lowestCeilingY)
                                {
                                    lowestCeilingY = ceilingY;
                                    foundAnyCeiling = true;
                                }
                                break;
                            }
                        }
                    }
                }

                // Next, find the ground below this sample point
                Vector2 rayOrigin = new Vector2(x, ownerPos.y + 1.2f);
                RaycastHit2D[] hits = Physics2D.RaycastAll(rayOrigin, Vector2.down, 15.0f);
                foreach (var hit in hits)
                {
                    if (hit.collider != null && !hit.collider.isTrigger)
                    {
                        Body hitBody = hit.collider.GetComponentInParent<Body>();
                        if (hitBody == null)
                        {
                            float hitY = hit.point.y;

                            // If there is a ceiling above this sample point, ignore ground hits at or above it
                            if (foundCeiling && hitY >= ceilingY - 0.1f) continue;

                            // Dynamic Platform Matching for pass-through/one-way platforms
                            bool isPassThrough = hit.collider.GetComponent<PlatformEffector2D>() != null || 
                                                 hit.collider.name.IndexOf("platform", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 hit.collider.name.IndexOf("oneway", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 hit.collider.name.IndexOf("passthrough", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 hit.collider.name.IndexOf("jumpthrough", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 hit.collider.name.IndexOf("bridge", StringComparison.OrdinalIgnoreCase) >= 0;

                            if (isPassThrough)
                            {
                                float verticalDiff = ownerPos.y - hitY;
                                if (verticalDiff < -0.2f || verticalDiff > 0.5f)
                                {
                                    continue; // Ignore this platform as the player is not standing on or close to it
                                }
                            }

                            if (hitY > highestGroundY)
                            {
                                highestGroundY = hitY;
                                foundGround = true;
                            }
                        }
                    }
                }
            }

            // 6. Height clamping with Airborne Coyote Hover & Ceiling Squeezing
            float targetY;
            float heightOffset = pet.PetHeightOffset;
            float maxGroundDist = pet.MaxGroundDistance;

            bool isAirborne = !foundGround || (ownerPos.y - highestGroundY > maxGroundDist);

            if (isAirborne)
            {
                // Smart Airborne Coyote Hover: float relative to owner's feet/hips + gentle sine-wave bobbing
                float bobbingOffset = Mathf.Sin(Time.time * pet.BobbingSpeed) * pet.BobbingAmplitude;
                targetY = ownerPos.y + (visualHeight * 0.5f) + heightOffset - 0.4f + bobbingOffset;
            }
            else
            {
                // Ground tracking mode: scale-independent center positioning so bottom rests exactly on the ground!
                targetY = highestGroundY + (visualHeight * 0.5f) + heightOffset;
            }

            // Smart Ceiling Detection & Squeezing
            float squishX = 1.0f;
            float squishY = 1.0f;

            float targetSquishY = 1.0f;
            if (foundAnyCeiling)
            {
                float groundY = foundGround ? highestGroundY : (ownerPos.y - 0.4f);
                float clearance = lowestCeilingY - groundY;
                if (clearance < visualHeight)
                {
                    // Calculate visual squeezing factor based on available space
                    targetSquishY = Mathf.Clamp(clearance / visualHeight, 0.4f, 1.0f);
                }
            }

            // Smooth Interpolation of the squish factor over time for remote pet
            pet.CurrentSquishY = Mathf.Lerp(pet.CurrentSquishY, targetSquishY, dt * pet.SquishSmoothing);
            squishY = pet.CurrentSquishY;
            squishX = 1.0f / squishY; // Maintain volume

            // Adjust Y height center using the interpolated squish factor so the bottom continues resting on the ground
            if (!isAirborne)
            {
                targetY = highestGroundY + (visualHeight * squishY * 0.5f) + heightOffset;
            }
            else
            {
                float bobbingOffset = Mathf.Sin(Time.time * pet.BobbingSpeed) * pet.BobbingAmplitude;
                targetY = ownerPos.y + (visualHeight * squishY * 0.5f) + heightOffset - 0.4f + bobbingOffset;
            }

            // Clamping targetY to the physical clearance bounds to prevent SmoothDamp spring compression inside tiles
            if (foundAnyCeiling)
            {
                float maxAllowedY = lowestCeilingY - (visualHeight * squishY * 0.5f);
                if (targetY > maxAllowedY)
                {
                    targetY = maxAllowedY;
                }
            }
            if (foundGround)
            {
                float minAllowedY = highestGroundY + (visualHeight * squishY * 0.5f);
                if (targetY < minAllowedY)
                {
                    targetY = minAllowedY;
                }
            }

            // Smoothly interpolate vertical position using SmoothDamp (Spring-Damper suspension)
            float currentY = pet.PetVisuals.transform.position.y;
            float newY = Mathf.SmoothDamp(currentY, targetY, ref pet.YVelocity, 0.12f, 50.0f, dt);

            // 6.5 Robust Post-Movement Collision Resolution for remote pet
            Vector2 finalPos = new Vector2(newX, newY);
            Vector2 previousPos = new Vector2(currentX, currentY);
            float halfWidthResolved = halfWidth;
            float halfHeightResolved = visualHeight * squishY * 0.5f;

            for (int i = 0; i < 3; i++)
            {
                Collider2D? overlappingCol = null;
                Collider2D[] overlaps = Physics2D.OverlapBoxAll(finalPos, new Vector2(halfWidthResolved * 2f, halfHeightResolved * 2f), 0f);
                foreach (var col in overlaps)
                {
                    if (col != null && !col.isTrigger)
                    {
                        Body hitBody = col.GetComponentInParent<Body>();
                        if (hitBody == null)
                        {
                            overlappingCol = col;
                            break;
                        }
                    }
                }

                if (overlappingCol == null)
                {
                    break;
                }

                Vector2 rayDir = finalPos - previousPos;
                float rayLen = rayDir.magnitude;
                bool resolved = false;

                if (rayLen > 0.001f)
                {
                    RaycastHit2D[] hits = Physics2D.RaycastAll(previousPos, rayDir.normalized, rayLen + 1.0f);
                    foreach (var hit in hits)
                    {
                        if (hit.collider == overlappingCol)
                        {
                            Vector2 normal = hit.normal;
                            if (Mathf.Abs(normal.x) > 0.1f)
                            {
                                finalPos.x = hit.point.x + normal.x * halfWidthResolved;
                            }
                            if (Mathf.Abs(normal.y) > 0.1f)
                            {
                                finalPos.y = hit.point.y + normal.y * halfHeightResolved;
                                pet.YVelocity = 0f;
                            }
                            resolved = true;
                            break;
                        }
                    }
                }

                if (!resolved)
                {
                    Vector2 ownerPos2D = new Vector2(ownerPos.x, ownerPos.y);
                    Vector2 ownerToFinal = finalPos - ownerPos2D;
                    float ownerDist = ownerToFinal.magnitude;
                    if (ownerDist > 0.001f)
                    {
                        RaycastHit2D[] hits = Physics2D.RaycastAll(ownerPos2D, ownerToFinal.normalized, ownerDist + 1.0f);
                        foreach (var hit in hits)
                        {
                            if (hit.collider == overlappingCol)
                            {
                                Vector2 normal = hit.normal;
                                if (Mathf.Abs(normal.x) > 0.1f)
                                {
                                    finalPos.x = hit.point.x + normal.x * halfWidthResolved;
                                }
                                if (Mathf.Abs(normal.y) > 0.1f)
                                {
                                    finalPos.y = hit.point.y + normal.y * halfHeightResolved;
                                    pet.YVelocity = 0f;
                                }
                                resolved = true;
                                break;
                            }
                        }
                    }
                }

                if (!resolved)
                {
                    Vector2 closest = overlappingCol.ClosestPoint(finalPos);
                    Vector2 pushDir = finalPos - closest;
                    if (pushDir.magnitude > 0.001f)
                    {
                        pushDir.Normalize();
                        finalPos.x = closest.x + pushDir.x * halfWidthResolved;
                        finalPos.y = closest.y + pushDir.y * halfHeightResolved;
                        pet.YVelocity = 0f;
                    }
                    else
                    {
                        finalPos.y += 0.1f;
                        pet.YVelocity = 0f;
                    }
                }
            }

            newX = finalPos.x;
            newY = finalPos.y;

            // Set final position
            pet.PetVisuals.transform.position = new Vector3(newX, newY, pet.PetVisuals.transform.position.z);

            // 7. Walk wiggle and squish/stretch animation (with position deadzone, low-pass filtering, and ceiling squish)
            float distMoved = Vector3.Distance(ownerPos, pet.LastOwnerPos);
            if (distMoved < 0.02f) // Ignore tiny network updates to prevent standing-still jittering!
            {
                distMoved = 0f;
            }
            float movementSpeed = distMoved / dt;

            // Apply a low-pass filter to smooth out sudden changes and prevent instant animation popping
            pet.SmoothMovementSpeed = Mathf.Lerp(pet.SmoothMovementSpeed, movementSpeed, dt * 8.0f);
            bool isWalking = pet.SmoothMovementSpeed > 0.5f;
            float finalScaleX = petScale * squishX;
            float finalScaleY = petScale * squishY;

            if (isWalking)
            {
                pet.WiggleTimer += dt * pet.WiggleSpeed;
                float angle = Mathf.Sin(pet.WiggleTimer) * pet.WiggleIntensity;
                pet.PetVisuals.transform.rotation = Quaternion.Euler(0f, 0f, angle);

                float squash = Mathf.Sin(pet.WiggleTimer * 2f) * 0.12f;
                pet.PetVisuals.transform.localScale = new Vector3(
                    (1f + squash) * finalScaleX * (pet.FacingRight ? 1f : -1f),
                    (1f - squash) * finalScaleY,
                    1f
                );
            }
            else
            {
                pet.PetVisuals.transform.rotation = Quaternion.Lerp(pet.PetVisuals.transform.rotation, Quaternion.identity, dt * 5.0f);
                pet.PetVisuals.transform.localScale = Vector3.Lerp(
                    pet.PetVisuals.transform.localScale,
                    new Vector3(finalScaleX * (pet.FacingRight ? 1f : -1f), finalScaleY, 1f),
                    dt * 5.0f
                );
            }

            pet.LastOwnerPos = ownerPos;
        }

        private static void FetchLobbyPetData(RemotePetInstance pet)
        {
            if (KSteam.IS_IN_LOBBY && pet.SteamId != 0)
            {
                try
                {
                    CSteamID lobbyId = KSteam.lobbyId;
                    CSteamID memberId = new CSteamID(pet.SteamId);

                    // Check whether the remote player has their pet hidden
                    string visStr = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_visible");
                    if (visStr == "false")
                    {
                        // Destroy visuals if they exist and skip the rest of the update
                        if (pet.PetVisuals != null)
                        {
                            UnityEngine.Object.Destroy(pet.PetVisuals);
                            pet.PetVisuals = null;
                            pet.SpriteRenderer = null;
                            pet.AudioSource = null;
                        }
                        return;
                    }

                    string image = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_image");
                    string hash = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_hash");

                    if (!string.IsNullOrEmpty(image))
                    {
                        bool needsReload = false;
                        if (image.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            image.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            if (pet.LoadedPetSource != image)
                            {
                                pet.LoadedPetSource = image;
                                pet.LoadedPetHash = "";
                                needsReload = true;
                            }
                        }
                        else
                        {
                            if (pet.LoadedPetHash != hash || pet.LoadedPetSource != image)
                            {
                                pet.LoadedPetSource = image;
                                pet.LoadedPetHash = hash;
                                needsReload = true;
                            }
                        }

                        if (needsReload)
                        {
                            LoadRemotePetSprite(pet);
                        }
                    }

                    string scaleStr = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_scale");
                    if (float.TryParse(scaleStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
                    {
                        pet.PetScale = scale;
                    }

                    string wSpeedStr = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_wiggle_speed");
                    if (float.TryParse(wSpeedStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float wSpeed))
                    {
                        pet.WiggleSpeed = wSpeed;
                    }

                    string wIntStr = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_wiggle_intensity");
                    if (float.TryParse(wIntStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float wInt))
                    {
                        pet.WiggleIntensity = wInt;
                    }

                    string hOffStr = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_height_offset");
                    if (float.TryParse(hOffStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float hOff))
                    {
                        pet.PetHeightOffset = hOff;
                    }

                    // Sync personalized movement/follow settings
                    string fSpeedStr = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_follow_speed");
                    if (float.TryParse(fSpeedStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float fSpeed))
                    {
                        pet.FollowSpeed = fSpeed;
                    }

                    string fDistStr = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_follow_distance");
                    if (float.TryParse(fDistStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float fDist))
                    {
                        pet.FollowDistance = fDist;
                    }

                     string maxGDistStr = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_max_ground_distance");
                    if (float.TryParse(maxGDistStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float maxGDist))
                    {
                        pet.MaxGroundDistance = maxGDist;
                    }

                    // Sync advanced auto-leveling settings
                    string bAmpStr = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_bobbing_amplitude");
                    if (float.TryParse(bAmpStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float bAmp))
                    {
                        pet.BobbingAmplitude = bAmp;
                    }

                    string bSpeedStr = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_bobbing_speed");
                    if (float.TryParse(bSpeedStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float bSpeed))
                    {
                        pet.BobbingSpeed = bSpeed;
                    }

                    string sSmoothingStr = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_squish_smoothing");
                    if (float.TryParse(sSmoothingStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float sSmoothing))
                    {
                        pet.SquishSmoothing = sSmoothing;
                    }

                    // Sync audio settings
                    string audioPackStr = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_audiopack");
                    if (!string.IsNullOrEmpty(audioPackStr) && audioPackStr != pet.AudioPackName)
                    {
                        pet.AudioPackName = audioPackStr;
                        LoadRemoteAudioPack(pet);
                    }

                    string audioVolStr = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "cosmetic_pet_audiovolume");
                    if (float.TryParse(audioVolStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float audioVol))
                    {
                        pet.AudioVolume = audioVol;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"Error fetching Steam lobby member pet data: {ex.Message}");
                }
            }
        }

        private static void LoadRemoteAudioPack(RemotePetInstance pet)
        {
            if (pet.PetVisuals == null) return;

            // Ensure the remote pet GameObject has an AudioSource
            if (pet.AudioSource == null)
            {
                pet.AudioSource = pet.PetVisuals.AddComponent<AudioSource>();
                pet.AudioSource.spatialBlend = 1.0f; // 3D positional audio
                pet.AudioSource.minDistance = 2f;
                pet.AudioSource.maxDistance = 25f;
                pet.AudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            }

            pet.AudioClips.Clear();

            if (string.IsNullOrEmpty(pet.AudioPackName) || pet.AudioPackName == "None")
            {
                pet.LoadedAudioPackName = "None";
                return;
            }

            string packPath = Path.Combine(Plugin.AudioPacksDirectory, pet.AudioPackName);
            if (!Directory.Exists(packPath))
            {
                Plugin.Logger.LogInfo($"Remote pet audio pack not found locally, skipping: {packPath}");
                pet.LoadedAudioPackName = pet.AudioPackName; // mark as attempted
                return;
            }

            pet.LoadedAudioPackName = pet.AudioPackName;
            Plugin.Logger.LogInfo($"Loading remote audio pack '{pet.AudioPackName}' for player {pet.PlayerName}");

            string[] audioFiles = Directory.GetFiles(packPath, "*.*", SearchOption.AllDirectories);
            foreach (string file in audioFiles)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                AudioType type;
                if (ext == ".wav") type = AudioType.WAV;
                else if (ext == ".ogg") type = AudioType.OGGVORBIS;
                else if (ext == ".mp3") type = AudioType.MPEG;
                else continue;

                PetController.Instance.StartCoroutine(LoadRemoteAudioClip(file, type, pet));
            }
        }

        private static IEnumerator LoadRemoteAudioClip(string filePath, AudioType type, RemotePetInstance pet)
        {
            string url = "file://" + filePath.Replace("\\", "/");
            using (var uwr = UnityWebRequestMultimedia.GetAudioClip(url, type))
            {
                yield return uwr.SendWebRequest();
                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(uwr);
                    if (clip != null)
                    {
                        pet.AudioClips.Add(clip);
                        Plugin.Logger.LogInfo($"Loaded remote audio clip: {clip.name} for {pet.PlayerName}");
                    }
                }
                else
                {
                    Plugin.Logger.LogWarning($"Failed to load remote audio clip from {filePath}: {uwr.error}");
                }
            }
        }

        private static void LoadRemotePetSprite(RemotePetInstance pet)
        {
            if (string.IsNullOrEmpty(pet.LoadedPetSource))
            {
                string fallbackPath = Path.Combine(Plugin.PetsDirectory, "default_slime.png");
                if (File.Exists(fallbackPath))
                {
                    LoadLocalSprite(pet, fallbackPath);
                }
                return;
            }

            if (pet.LoadedPetSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                pet.LoadedPetSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                string cachePath = PetController.GetCacheFilePath(pet.LoadedPetSource);
                if (File.Exists(cachePath))
                {
                    LoadLocalSprite(pet, cachePath);
                }
                else
                {
                    if (!_downloadingUrls.Contains(pet.LoadedPetSource))
                    {
                        _downloadingUrls.Add(pet.LoadedPetSource);
                        PetController.Instance.StartCoroutine(PetController.DownloadAndCacheTexture(pet.LoadedPetSource, cachePath, (success) =>
                        {
                            _downloadingUrls.Remove(pet.LoadedPetSource);
                            if (success && File.Exists(cachePath))
                            {
                                LoadLocalSprite(pet, cachePath);
                            }
                        }));
                    }
                }
            }
            else
            {
                // Local custom companion skin
                if (!string.IsNullOrEmpty(pet.LoadedPetHash))
                {
                    string cachedTransferredPath = Path.Combine(Plugin.PetsDirectory, "Cache", pet.LoadedPetHash + ".png");
                    if (File.Exists(cachedTransferredPath))
                    {
                        LoadLocalSprite(pet, cachedTransferredPath);
                    }
                    else
                    {
                        // Check if we already have it locally under the same original name & exact same hash
                        string localPath = Path.Combine(Plugin.PetsDirectory, pet.LoadedPetSource);
                        if (File.Exists(localPath) && GetFileSha1(localPath) == pet.LoadedPetHash)
                        {
                            LoadLocalSprite(pet, localPath);
                        }
                        else
                        {
                            // We do not have this skin. Request it peer-to-peer!
                            RequestPetSpriteOverP2P(pet.SteamId, pet.LoadedPetHash);

                            // Load fallback default_slime temporarily until it finishes downloading
                            string fallbackPath = Path.Combine(Plugin.PetsDirectory, "default_slime.png");
                            if (File.Exists(fallbackPath))
                            {
                                LoadLocalSprite(pet, fallbackPath);
                            }
                        }
                    }
                }
                else
                {
                    // No hash provided, load from original local name (legacy / fallback)
                    string localPath = Path.Combine(Plugin.PetsDirectory, pet.LoadedPetSource);
                    if (File.Exists(localPath))
                    {
                        LoadLocalSprite(pet, localPath);
                    }
                    else
                    {
                        string fallbackPath = Path.Combine(Plugin.PetsDirectory, "default_slime.png");
                        if (File.Exists(fallbackPath))
                        {
                            LoadLocalSprite(pet, fallbackPath);
                        }
                    }
                }
            }
        }

        private static void LoadLocalSprite(RemotePetInstance pet, string filePath)
        {
            if (pet.SpriteRenderer == null) return;

            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Point;

                if (tex.LoadImage(data))
                {
                    float baseSizeInUnits = 2.4f;
                    float maxDimension = Mathf.Max(tex.width, tex.height);
                    float ppu = maxDimension / baseSizeInUnits;

                    Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
                    pet.SpriteRenderer.sprite = sprite;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to load remote player local pet sprite: {ex.Message}");
            }
        }

        // ─────────── Peer-to-Peer Custom Pet Skin Transfer ───────────

        public static string GetFileSha1(string filePath)
        {
            if (!File.Exists(filePath)) return "";
            try
            {
                using (var sha = SHA1.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = sha.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLower();
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error calculating file hash for {filePath}: {ex.Message}");
                return "";
            }
        }

        public static void InitializeP2P()
        {
            if (_sessionRequestCallback == null)
            {
                try
                {
                    _sessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
                    Plugin.Logger.LogInfo("Initialized Cosmetic Pet P2P session request callback.");
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError($"Failed to initialize P2P callback: {ex.Message}");
                }
            }
        }

        private static void OnP2PSessionRequest(P2PSessionRequest_t request)
        {
            try
            {
                // Accept P2P session from anyone in our current lobby
                if (KSteam.IS_IN_LOBBY)
                {
                    SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
                    Plugin.Logger.LogInfo($"Accepted P2P session request from {request.m_steamIDRemote}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error accepting P2P session: {ex.Message}");
            }
        }

        public static void ReceiveP2PPackets()
        {
            if (!KSteam.IS_IN_LOBBY) return;

            // Make sure callback is initialized
            InitializeP2P();

            uint msgSize;
            while (SteamNetworking.IsP2PPacketAvailable(out msgSize, 1337))
            {
                byte[] packet = new byte[msgSize];
                uint bytesRead;
                CSteamID remoteUser;

                if (SteamNetworking.ReadP2PPacket(packet, msgSize, out bytesRead, out remoteUser, 1337))
                {
                    if (bytesRead < 1) continue;

                    byte packetType = packet[0];
                    if (packetType == 0)
                    {
                        // Request: remote player wants our custom image
                        HandleP2PRequest(remoteUser, packet);
                    }
                    else if (packetType == 1)
                    {
                        // Response: remote player sent us their custom image
                        HandleP2PResponse(packet);
                    }
                    else if (packetType == 2)
                    {
                        // Audio event: remote player's pet played a sound
                        HandleRemoteAudioEvent(remoteUser, packet);
                    }
                }
            }
        }

        public static void BroadcastAudioEvent(int clipIndex, float volume)
        {
            if (!KSteam.IS_IN_LOBBY) return;
            if (clipIndex < 0 || clipIndex > 255) return;

            try
            {
                byte[] packet = new byte[6];
                packet[0] = 2; // Type 2: Audio event
                packet[1] = (byte)clipIndex;
                Buffer.BlockCopy(BitConverter.GetBytes(volume), 0, packet, 2, 4);

                foreach (ulong memberId in GetLobbyMemberSteamIds())
                {
                    SteamNetworking.SendP2PPacket(
                        new CSteamID(memberId), packet, (uint)packet.Length,
                        EP2PSend.k_EP2PSendUnreliable, 1337);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error broadcasting audio event: {ex.Message}");
            }
        }

        private static void HandleRemoteAudioEvent(CSteamID remoteUser, byte[] packet)
        {
            if (packet.Length < 6) return;

            try
            {
                int clipIndex = packet[1];
                float volume = BitConverter.ToSingle(packet, 2);

                // Find the remote pet owned by this Steam user
                foreach (var pet in _remotePets.Values)
                {
                    if (pet.SteamId != remoteUser.m_SteamID) continue;
                    if (pet.AudioSource == null || pet.AudioClips.Count == 0) return;

                    int idx = clipIndex % pet.AudioClips.Count;
                    AudioClip clip = pet.AudioClips[idx];
                    if (clip != null)
                    {
                        pet.AudioSource.PlayOneShot(clip, volume);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error handling remote audio event: {ex.Message}");
            }
        }

        private static IEnumerable<ulong> GetLobbyMemberSteamIds()
        {
            var ids = new List<ulong>();
            try
            {
                CSteamID lobbyId = KSteam.lobbyId;
                int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
                ulong localId = SteamUser.GetSteamID().m_SteamID;
                for (int i = 0; i < memberCount; i++)
                {
                    ulong memberId = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i).m_SteamID;
                    if (memberId != localId)
                        ids.Add(memberId);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error enumerating lobby members: {ex.Message}");
            }
            return ids;
        }

        private static void RequestPetSpriteOverP2P(ulong ownerSteamId, string hash)
        {
            if (ownerSteamId == 0 || string.IsNullOrEmpty(hash) || hash.Length != 40) return;

            Plugin.Logger.LogInfo($"Requesting custom pet skin bytes for hash {hash} from SteamID {ownerSteamId}...");

            try
            {
                byte[] hashBytes = Encoding.UTF8.GetBytes(hash);
                byte[] packet = new byte[1 + hashBytes.Length];
                packet[0] = 0; // Type 0: Request
                Buffer.BlockCopy(hashBytes, 0, packet, 1, hashBytes.Length);

                CSteamID remoteUser = new CSteamID(ownerSteamId);
                SteamNetworking.SendP2PPacket(remoteUser, packet, (uint)packet.Length, EP2PSend.k_EP2PSendReliable, 1337);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error sending P2P request: {ex.Message}");
            }
        }

        private static void HandleP2PRequest(CSteamID remoteUser, byte[] packet)
        {
            try
            {
                if (packet.Length < 41) return;
                string hash = Encoding.UTF8.GetString(packet, 1, 40);
                Plugin.Logger.LogInfo($"Received request for pet hash {hash} from {remoteUser}");

                string targetFilePath = "";
                string activeSource = Plugin.Cfg.SelectedPetImage.Value;

                // Check if the requested hash matches our currently active local pet
                if (!string.IsNullOrEmpty(activeSource) &&
                    !activeSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !activeSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    string activePath = Path.Combine(Plugin.PetsDirectory, activeSource);
                    if (File.Exists(activePath) && GetFileSha1(activePath) == hash)
                    {
                        targetFilePath = activePath;
                    }
                }

                // If not, search the authorized Pets/ directory (ensuring file sandboxing)
                if (string.IsNullOrEmpty(targetFilePath))
                {
                    foreach (var file in Directory.GetFiles(Plugin.PetsDirectory, "*.png"))
                    {
                        if (GetFileSha1(file) == hash)
                        {
                            targetFilePath = file;
                            break;
                        }
                    }
                }

                // Or inside Cache directory
                if (string.IsNullOrEmpty(targetFilePath))
                {
                    string cacheDir = Path.Combine(Plugin.PetsDirectory, "Cache");
                    if (Directory.Exists(cacheDir))
                    {
                        foreach (var file in Directory.GetFiles(cacheDir, "*.png"))
                        {
                            if (GetFileSha1(file) == hash)
                            {
                                targetFilePath = file;
                                break;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(targetFilePath) && File.Exists(targetFilePath))
                {
                    byte[] fileBytes = File.ReadAllBytes(targetFilePath);
                    byte[] response = new byte[1 + 40 + fileBytes.Length];
                    response[0] = 1; // Type 1: Response

                    byte[] hashBytes = Encoding.UTF8.GetBytes(hash);
                    Buffer.BlockCopy(hashBytes, 0, response, 1, 40);
                    Buffer.BlockCopy(fileBytes, 0, response, 41, fileBytes.Length);

                    Plugin.Logger.LogInfo($"Sending pet skin bytes ({fileBytes.Length} bytes) for hash {hash} to {remoteUser}...");
                    SteamNetworking.SendP2PPacket(remoteUser, response, (uint)response.Length, EP2PSend.k_EP2PSendReliable, 1337);
                }
                else
                {
                    Plugin.Logger.LogWarning($"Could not find file matching requested hash: {hash}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error handling P2P request: {ex.Message}");
            }
        }

        private static void HandleP2PResponse(byte[] packet)
        {
            try
            {
                if (packet.Length < 41) return;
                string hash = Encoding.UTF8.GetString(packet, 1, 40);
                int fileLength = packet.Length - 41;
                if (fileLength <= 0) return;

                Plugin.Logger.LogInfo($"Received pet skin bytes ({fileLength} bytes) for hash {hash}");

                string cacheDir = Path.Combine(Plugin.PetsDirectory, "Cache");
                if (!Directory.Exists(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }

                string cachedPath = Path.Combine(cacheDir, hash + ".png");
                byte[] fileBytes = new byte[fileLength];
                Buffer.BlockCopy(packet, 41, fileBytes, 0, fileLength);

                File.WriteAllBytes(cachedPath, fileBytes);
                Plugin.Logger.LogInfo($"Saved received pet skin to cache: {cachedPath}");

                // Force reload of remote pet instances using this newly-transferred skin
                foreach (var pet in _remotePets.Values)
                {
                    if (pet.LoadedPetHash == hash)
                    {
                        Plugin.Logger.LogInfo($"Reloading companion sprite for {pet.PlayerName} using newly synchronized skin.");
                        LoadRemotePetSprite(pet);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error handling P2P response: {ex.Message}");
            }
        }
    }
}
