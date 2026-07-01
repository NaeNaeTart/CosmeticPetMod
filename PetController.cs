using System;
using System.IO;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CosmeticPetMod
{
    public class PetController : MonoBehaviour
    {
        public static PetController Instance { get; private set; } = null!;

        private GameObject? _petVisuals;
        private SpriteRenderer? _spriteRenderer;
        
        private Vector3 _lastPlayerPos;
        private float _wiggleTimer;
        private bool _facingRight = true;
        private string _loadedImagePath = "";
        private float _yVelocity;
        private float _currentSquishY = 1.0f;
        private float _smoothMovementSpeed;
        private float _directionLockTimer;
        private bool _lastPendingFacingRight = true;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            // Create child GameObject for pet visuals so we don't pollute the plugin controller GameObject
            _petVisuals = new GameObject("CosmeticPet_Visuals");
            _petVisuals.transform.SetParent(transform);
            
            _spriteRenderer = _petVisuals.AddComponent<SpriteRenderer>();
            _spriteRenderer.sortingOrder = 30; // High sorting order to draw in front of background elements
            
            // Try loading the configured pet image on startup
            LoadConfiguredPet();

            // Find initial player position to prevent teleportation lags
            var player = FindPlayer();
            if (player != null)
            {
                _lastPlayerPos = player.transform.position;
                _petVisuals.transform.position = _lastPlayerPos;
            }
        }

        private void Update()
        {
            if (_petVisuals == null || _spriteRenderer == null) return;

            // Handle toggle visibility keybind
            if (Input.GetKeyDown(Plugin.Cfg.TogglePetKey.Value))
            {
                Plugin.Cfg.ModEnabled.Value = !Plugin.Cfg.ModEnabled.Value;
                Plugin.Logger.LogInfo($"Cosmetic Pet visibility toggled to: {Plugin.Cfg.ModEnabled.Value}");
            }

            // Sync visual active state with ModEnabled config
            if (_petVisuals.activeSelf != Plugin.Cfg.ModEnabled.Value)
            {
                _petVisuals.SetActive(Plugin.Cfg.ModEnabled.Value);
            }

            if (!Plugin.Cfg.ModEnabled.Value) return;

            var player = FindPlayer();
            if (player == null) return;

            Vector3 playerPos = player.transform.position;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 0. Prevent teleportation lags and mountain clip-throughs
            if (Vector3.Distance(playerPos, _petVisuals.transform.position) > 15.0f)
            {
                _petVisuals.transform.position = playerPos;
            }

            // 1. Determine player facing direction with hysteresis to prevent rapid mirroring flutter
            bool pendingFacingRight = _facingRight;
            float scaleX = player.transform.localScale.x;
            if (Mathf.Abs(scaleX) > 0.01f)
            {
                pendingFacingRight = scaleX > 0f;
            }
            else
            {
                float dx = playerPos.x - _lastPlayerPos.x;
                if (Mathf.Abs(dx) > 0.01f)
                {
                    pendingFacingRight = dx > 0f;
                }
            }

            if (pendingFacingRight != _facingRight)
            {
                if (pendingFacingRight != _lastPendingFacingRight)
                {
                    _lastPendingFacingRight = pendingFacingRight;
                    _directionLockTimer = 0f;
                }
                
                _directionLockTimer += dt;
                if (_directionLockTimer >= 0.15f) // Sustained facing change for 150ms
                {
                    _facingRight = pendingFacingRight;
                }
            }
            else
            {
                _directionLockTimer = 0f;
            }

            // 2. Target follow horizontal position
            float followDist = Plugin.Cfg.FollowDistance.Value;
            float targetX = playerPos.x + (_facingRight ? -followDist : followDist);
            
            // Smoothly move the horizontal position towards the target
            float followSpeed = Plugin.Cfg.PetFollowSpeed.Value;
            float currentX = _petVisuals.transform.position.x;
            float newX = Mathf.Lerp(currentX, targetX, dt * followSpeed);

            // 3. Scale-independent horizontal wall collision clamping (three-point check)
            float petScale = Plugin.Cfg.PetScale.Value;
            float halfWidth = 0.4f * petScale;
            float visualHeight = (_spriteRenderer != null && _spriteRenderer.sprite != null) ? 
                (_spriteRenderer.sprite.rect.height / _spriteRenderer.sprite.pixelsPerUnit) * petScale : 2.4f * petScale;

            float dist = Mathf.Abs(newX - currentX);
            if (dist > 0.001f)
            {
                float dirX = Mathf.Sign(newX - currentX);
                float rayDist = dist + halfWidth;

                // Check at bottom, center, and top heights of the sprite's current squished visual volume
                float[] checkHeights = new float[] { 
                    _petVisuals.transform.position.y - (visualHeight * _currentSquishY * 0.35f), 
                    _petVisuals.transform.position.y, 
                    _petVisuals.transform.position.y + (visualHeight * _currentSquishY * 0.35f) 
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

            // 4. Smart Ground detection using Multi-Raycast (Left, Center, Right) on the clamped horizontal coordinate
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
                RaycastHit2D[] ceilHits = Physics2D.RaycastAll(new Vector2(x, playerPos.y + 0.8f), Vector2.up, 5.0f);
                foreach (var hit in ceilHits)
                {
                    if (hit.collider != null && !hit.collider.isTrigger)
                    {
                        Body hitBody = hit.collider.GetComponentInParent<Body>();
                        if (hitBody == null)
                        {
                            float hitY = hit.point.y;
                            if (hitY >= playerPos.y + 1.0f)
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
                Vector2 rayOrigin = new Vector2(x, playerPos.y + 1.2f);
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
                                float verticalDiff = playerPos.y - hitY;
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

            // 4. Height clamping with Airborne Coyote Hover & Ceiling Squeezing
            float targetY;
            float heightOffset = Plugin.Cfg.PetHeightOffset.Value;
            float maxGroundDist = Plugin.Cfg.MaxGroundDistance.Value;

            bool isAirborne = !foundGround || (playerPos.y - highestGroundY > maxGroundDist);

            if (isAirborne)
            {
                // Smart Airborne Coyote Hover: float relative to player's feet/hips + gentle sine-wave bobbing
                float bobbingSpeed = Plugin.Cfg.BobbingSpeed.Value;
                float bobbingAmplitude = Plugin.Cfg.BobbingAmplitude.Value;
                float bobbingOffset = Mathf.Sin(Time.time * bobbingSpeed) * bobbingAmplitude;
                targetY = playerPos.y + (visualHeight * 0.5f) + heightOffset - 0.4f + bobbingOffset;
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
                float groundY = foundGround ? highestGroundY : (playerPos.y - 0.4f);
                float clearance = lowestCeilingY - groundY;
                if (clearance < visualHeight)
                {
                    // Calculate visual squeezing factor based on available space
                    targetSquishY = Mathf.Clamp(clearance / visualHeight, 0.4f, 1.0f);
                }
            }

            // Smooth Interpolation of the squish factor over time
            _currentSquishY = Mathf.Lerp(_currentSquishY, targetSquishY, dt * Plugin.Cfg.SquishSmoothing.Value);
            squishY = _currentSquishY;
            squishX = 1.0f / squishY; // Maintain volume

            // Adjust Y height center using the interpolated squish factor so the bottom continues resting on the ground
            if (!isAirborne)
            {
                targetY = highestGroundY + (visualHeight * squishY * 0.5f) + heightOffset;
            }
            else
            {
                float bobbingSpeed = Plugin.Cfg.BobbingSpeed.Value;
                float bobbingAmplitude = Plugin.Cfg.BobbingAmplitude.Value;
                float bobbingOffset = Mathf.Sin(Time.time * bobbingSpeed) * bobbingAmplitude;
                targetY = playerPos.y + (visualHeight * squishY * 0.5f) + heightOffset - 0.4f + bobbingOffset;
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
            float currentY = _petVisuals.transform.position.y;
            float newY = Mathf.SmoothDamp(currentY, targetY, ref _yVelocity, 0.12f, 50.0f, dt);

            // 4.5 Robust Post-Movement Collision Resolution
            Vector2 finalPos = new Vector2(newX, newY);
            Vector2 previousPos = new Vector2(currentX, currentY);
            float halfWidthResolved = halfWidth;
            float halfHeightResolved = visualHeight * squishY * 0.5f;

            // Run a few resolution iterations to handle multiple overlapping surfaces (e.g. corner junctions)
            for (int i = 0; i < 3; i++)
            {
                // Find any overlapping solid collider at finalPos
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
                            break; // Resolve one collider at a time
                        }
                    }
                }

                if (overlappingCol == null)
                {
                    break; // No overlapping colliders, we are safe!
                }

                // We are overlapping a solid block! Let's find the penetration point and normal.
                // Try raycasting from the previous safe position to finalPos
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
                            // Found the exact boundary entry point!
                            // Offset along the normal to keep the bounding box outside
                            Vector2 normal = hit.normal;
                            
                            if (Mathf.Abs(normal.x) > 0.1f)
                            {
                                finalPos.x = hit.point.x + normal.x * halfWidthResolved;
                            }
                            if (Mathf.Abs(normal.y) > 0.1f)
                            {
                                finalPos.y = hit.point.y + normal.y * halfHeightResolved;
                                _yVelocity = 0f; // Stop vertical velocity if we hit floor/ceiling to prevent jitter/sticking
                            }
                            resolved = true;
                            break;
                        }
                    }
                }

                if (!resolved)
                {
                    // Fallback 1: Raycast from playerPos (which is guaranteed to be in empty space) to finalPos
                    Vector2 playerPos2D = new Vector2(playerPos.x, playerPos.y);
                    Vector2 playerToFinal = finalPos - playerPos2D;
                    float playerDist = playerToFinal.magnitude;
                    if (playerDist > 0.001f)
                    {
                        RaycastHit2D[] hits = Physics2D.RaycastAll(playerPos2D, playerToFinal.normalized, playerDist + 1.0f);
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
                                    _yVelocity = 0f;
                                }
                                resolved = true;
                                break;
                            }
                        }
                    }
                }

                if (!resolved)
                {
                    // Fallback 2: Simple geometric push-out using the closest point on the collider
                    Vector2 closest = overlappingCol.ClosestPoint(finalPos);
                    Vector2 pushDir = finalPos - closest;
                    if (pushDir.magnitude > 0.001f)
                    {
                        pushDir.Normalize();
                        finalPos.x = closest.x + pushDir.x * halfWidthResolved;
                        finalPos.y = closest.y + pushDir.y * halfHeightResolved;
                        _yVelocity = 0f;
                    }
                    else
                    {
                        // Fallback 3: Extreme backup - push straight up out of the block
                        finalPos.y += 0.1f;
                        _yVelocity = 0f;
                    }
                }
            }

            // Assign the resolved, completely safe coordinates
            newX = finalPos.x;
            newY = finalPos.y;

            // Set final position
            _petVisuals.transform.position = new Vector3(newX, newY, _petVisuals.transform.position.z);

            // 5. Walking wiggle animation (with low-pass motion speed filtering and deadzone)
            float playerMovementSpeed = Vector3.Distance(playerPos, _lastPlayerPos) / dt;
            if (playerMovementSpeed < 0.1f) // Deadzone filter
            {
                playerMovementSpeed = 0f;
            }

            // Smooth the speed over time to prevent instant popping/twitching
            _smoothMovementSpeed = Mathf.Lerp(_smoothMovementSpeed, playerMovementSpeed, dt * 8.0f);
            bool isWalking = _smoothMovementSpeed > 0.4f;

            float finalScaleX = petScale * squishX;
            float finalScaleY = petScale * squishY;

            if (isWalking)
            {
                float wiggleSpeed = Plugin.Cfg.WiggleSpeed.Value;
                float wiggleIntensity = Plugin.Cfg.WiggleIntensity.Value;

                _wiggleTimer += dt * wiggleSpeed;
                
                // Tilt rotation wiggle
                float angle = Mathf.Sin(_wiggleTimer) * wiggleIntensity;
                _petVisuals.transform.rotation = Quaternion.Euler(0f, 0f, angle);

                // Squash and stretch vertical bounce wiggle combined with ceiling squeeze scale
                float squash = Mathf.Sin(_wiggleTimer * 2f) * 0.12f;
                _petVisuals.transform.localScale = new Vector3(
                    (1f + squash) * finalScaleX * (_facingRight ? 1f : -1f), 
                    (1f - squash) * finalScaleY, 
                    1f
                );
            }
            else
            {
                // Smoothly decay back to rest rotation and scale
                _petVisuals.transform.rotation = Quaternion.Lerp(_petVisuals.transform.rotation, Quaternion.identity, dt * 5.0f);
                _petVisuals.transform.localScale = Vector3.Lerp(
                    _petVisuals.transform.localScale, 
                    new Vector3(finalScaleX * (_facingRight ? 1f : -1f), finalScaleY, 1f), 
                    dt * 5.0f
                );
            }

            // Store current position for velocity checking next frame
            _lastPlayerPos = playerPos;

            // Update remote multiplayer pets
            if (IsMpInstalled())
            {
                try
                {
                    UpdateMultiplayerPetsSafely();
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"Multiplayer pet update error: {ex.Message}");
                }
            }
        }

        private static bool IsMpInstalled()
        {
            try
            {
                return Type.GetType("KrokoshaCasualtiesMP.KSteam, KrokoshaCasualtiesMP") != null;
            }
            catch
            {
                return false;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void UpdateMultiplayerPetsSafely()
        {
            MpPetManager.UpdateMultiplayerPets();
        }

        private Body? FindPlayer()
        {
            if (PlayerCamera.main != null && PlayerCamera.main.body != null)
            {
                return PlayerCamera.main.body;
            }
            return null;
        }

        public void LoadConfiguredPet()
        {
            string source = Plugin.Cfg.SelectedPetImage.Value;
            if (string.IsNullOrEmpty(source)) return;

            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                string cachePath = GetCacheFilePath(source);
                if (File.Exists(cachePath))
                {
                    LoadPetFromFile(cachePath);
                }
                else
                {
                    StartCoroutine(DownloadAndCacheTexture(source, cachePath, (success) =>
                    {
                        if (success && File.Exists(cachePath))
                        {
                            LoadPetFromFile(cachePath);
                        }
                    }));
                }
            }
            else
            {
                string filePath = Path.Combine(Plugin.PetsDirectory, source);
                LoadPetFromFile(filePath);
            }
        }

        public bool LoadPetFromFile(string filePath)
        {
            if (_spriteRenderer == null) return false;

            if (!File.Exists(filePath))
            {
                Plugin.Logger.LogWarning($"Pet image file not found at: {filePath}");
                return false;
            }

            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Point; // Crisp retro point-filtering
                
                if (tex.LoadImage(data))
                {
                    // Calculate pixels per unit dynamically to enforce a uniform physical world size (2.4 units),
                    // regardless of the imported image's resolution!
                    float baseSizeInUnits = 2.4f;
                    float maxDimension = Mathf.Max(tex.width, tex.height);
                    float ppu = maxDimension / baseSizeInUnits;

                    // Create sprite centered (pivot at 0.5, 0.5) with the calculated PPU
                    Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
                    _spriteRenderer.sprite = sprite;
                    _loadedImagePath = filePath;
                    Plugin.Logger.LogInfo($"Successfully loaded pet sprite from {filePath} ({tex.width}x{tex.height}) with calculated PPU: {ppu:F2}");

                    if (IsMpInstalled())
                    {
                        try
                        {
                            MpPetManager.ForcePublishLocalPetSettings();
                        }
                        catch (Exception ex)
                        {
                            Plugin.Logger.LogWarning($"Failed to force publish pet settings: {ex.Message}");
                        }
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to load pet image file: {ex.Message}");
            }

            return false;
        }

        public static string GetCacheFilePath(string url)
        {
            string cacheDir = Path.Combine(Plugin.PetsDirectory, "Cache");
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
                string hex = BitConverter.ToString(hash).Replace("-", "").ToLower();
                return Path.Combine(cacheDir, hex + ".png");
            }
        }

        public static IEnumerator DownloadAndCacheTexture(string url, string cachePath, Action<bool> callback)
        {
            using (var uwr = UnityWebRequestTexture.GetTexture(url))
            {
                yield return uwr.SendWebRequest();

                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    var handler = (DownloadHandlerTexture)uwr.downloadHandler;
                    Texture2D tex = handler.texture;
                    if (tex != null)
                    {
                        try
                        {
                            byte[] pngBytes = tex.EncodeToPNG();
                            File.WriteAllBytes(cachePath, pngBytes);
                            Plugin.Logger.LogInfo($"Successfully downloaded and cached pet image from URL: {url}");
                            callback?.Invoke(true);
                            yield break;
                        }
                        catch (Exception ex)
                        {
                            Plugin.Logger.LogError($"Failed to cache downloaded texture: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Plugin.Logger.LogError($"Failed to download pet from {url}: {uwr.error}");
                }
            }
            callback?.Invoke(false);
        }
    }
}
