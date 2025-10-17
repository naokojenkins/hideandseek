using System;
using System.IO;
using System.Numerics;
using Raylib_cs;

namespace HideAndSeek.Core.RaylibThreeD
{
    /// <summary>
    /// Loads and caches 3D models for agents once per app lifetime.
    /// Ensures proper disposal on shutdown.
    /// </summary>
    public static class AgentModelCache
    {
        private static readonly object _lock = new object();
        private static bool _initialized;
        private static bool _failed;

        public static Model? SeekerModel;
        public static ModelAnimation[]? SeekerAnimations;
        public static int SeekerIdleAnimIndex = 0;  // default to first
        public static int SeekerWalkAnimIndex = 1;  // default to second if exists
        public static int SeekerAlertAnimIndex = 2; // default to third if exists (e.g., Jump)

        // Hider model and animations (Fox.gltf)
        public static Model? HiderModel;
        public static ModelAnimation[]? HiderAnimations;
        public static int HiderIdleAnimIndex = 0;
        public static int HiderWalkAnimIndex = 1;
        public static int HiderAlertAnimIndex = 2;

        // Tunables for orientation/scale compensation
        // Seeker: Character.gltf (faces +Z), align with +X via -90° base yaw; gaze +90° so face=gaze=movement
        public static readonly float SeekerBaseYawOffsetDeg = -90f;
        public static readonly Vector3 SeekerScale = new Vector3(0.4f, 0.4f, 0.4f);
        // Vertical offset scaled with model size so feet rest on the ground
        public static float SeekerYOffset = 0.0f;
        // Define "face": we consider the model's forward axis to be the face direction.
        // Adjust gaze yaw offset so the gaze originates from the face side instead of the model's flank.
        public static readonly float SeekerGazeYawOffsetDeg = 90f;
        // Approximate eye height above agent origin and small forward offset for gaze origin
        public static readonly float SeekerEyeHeight = 0.6f;
        public static readonly float SeekerGazeForwardOffset = 0.05f;

        // Hider: Fox.gltf (typical glTF +Z forward). Use same offsets as seeker to match movement and gaze.
        public static readonly float HiderBaseYawOffsetDeg = 180f;
        public static readonly Vector3 HiderScale = new Vector3(2.0f, 2.0f, 2.0f);
        public static float HiderYOffset = 0.0f;
        public static readonly float HiderGazeYawOffsetDeg = 180f;
        public static readonly float HiderEyeHeight = 0.5f;
        public static readonly float HiderGazeForwardOffset = 0.05f;

        public static bool IsReady => _initialized && !_failed && SeekerModel != null;

        /// <summary>
        /// Initialize and load assets once. Safe to call multiple times.
        /// </summary>
        public static void Init()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                try
                {
                    // Assets are copied to output into GLB/Character/glTF/...
                    string baseDir = AppContext.BaseDirectory;
                    string modelPath = Path.Combine(baseDir, "GLB", "Character", "glTF", "Character.gltf");

                    // Probe several likely locations if not found in output dir
                    if (!File.Exists(modelPath))
                    {
                        // 1) When running from Sim/bin/Debug/net9.0 → go up to project dir and check relative GLB
                        var alt1 = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "GLB", "Character", "glTF", "Character.gltf"));
                        if (File.Exists(alt1)) modelPath = alt1;
                    }
                    if (!File.Exists(modelPath))
                    {
                        // 2) Go to repo root then into HideAndSeek.Core/GLB/Character/glTF
                        var alt2 = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "HideAndSeek.Core", "GLB", "Character", "glTF", "Character.gltf"));
                        if (File.Exists(alt2)) modelPath = alt2;
                    }
                    if (!File.Exists(modelPath))
                    {
                        // 3) Directly under repo root GLB/Character/glTF (if assets are moved there)
                        var alt3 = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "GLB", "Character", "glTF", "Character.gltf"));
                        if (File.Exists(alt3)) modelPath = alt3;
                    }

                    if (File.Exists(modelPath))
                    {
                        SeekerModel = Raylib.LoadModel(modelPath);

                        // Try to load animations (unsafe interop)
                        try
                        {
                            unsafe
                            {
                                var bytes = System.Text.Encoding.UTF8.GetBytes(modelPath);
                                sbyte* pathPtr = stackalloc sbyte[bytes.Length + 1];
                                for (int i = 0; i < bytes.Length; i++) pathPtr[i] = (sbyte)bytes[i];
                                pathPtr[bytes.Length] = 0;

                                int count = 0;
                                var animPtr = Raylib.LoadModelAnimations(pathPtr, &count);
                                if (animPtr != null && count > 0)
                                {
                                    var arr = new ModelAnimation[count];
                                    for (int i = 0; i < count; i++) arr[i] = animPtr[i];
                                    SeekerAnimations = arr;
                                    // Prefer explicit Idle clip if present by common ordering: many assets place Idle at index 3 when 0 is Wave.
                                    // If fewer than 4 animations, keep default 0.
                                    SeekerIdleAnimIndex = count > 3 ? 3 : 0;
                                    SeekerWalkAnimIndex = count > 1 ? 1 : 0;
                                    // Prefer a Jump/Alert clip if available: heuristically pick index 2 if exists; otherwise use the last available as alert.
                                    SeekerAlertAnimIndex = count > 2 ? 2 : Math.Max(0, count - 1);
                                }
                            }
                        }
                        catch { /* animations optional */ }

                        // Adjust Y offset to match new scale (1/3 of previous ~0.1)
                        SeekerYOffset = 0.033f;
                    }
                    else
                    {
                        _failed = true;
                        System.Console.WriteLine($"[WARN] Seeker model not found at: {modelPath}. Falling back to primitive.");
                    }

                    // Load Hider (Toy Mouse .glb)
                    try
                    {
                        string baseDir2 = AppContext.BaseDirectory;
                        string mousePath = Path.Combine(baseDir2, "GLB", "Toy Mouse.glb");
                        if (!File.Exists(mousePath))
                        {
                            var alt1f = Path.GetFullPath(Path.Combine(baseDir2, "..", "..", "GLB", "Toy Mouse.glb"));
                            if (File.Exists(alt1f)) mousePath = alt1f;
                        }
                        if (!File.Exists(mousePath))
                        {
                            var alt2f = Path.GetFullPath(Path.Combine(baseDir2, "..", "..", "..", "..", "HideAndSeek.Core", "GLB", "Toy Mouse.glb"));
                            if (File.Exists(alt2f)) mousePath = alt2f;
                        }
                        if (!File.Exists(mousePath))
                        {
                            var alt3f = Path.GetFullPath(Path.Combine(baseDir2, "..", "..", "..", "..", "GLB", "Toy Mouse.glb"));
                            if (File.Exists(alt3f)) mousePath = alt3f;
                        }

                        if (File.Exists(mousePath))
                        {
                            HiderModel = Raylib.LoadModel(mousePath);
                            try
                            {
                                unsafe
                                {
                                    var bytesF = System.Text.Encoding.UTF8.GetBytes(mousePath);
                                    sbyte* pathPtrF = stackalloc sbyte[bytesF.Length + 1];
                                    for (int i = 0; i < bytesF.Length; i++) pathPtrF[i] = (sbyte)bytesF[i];
                                    pathPtrF[bytesF.Length] = 0;
                                    int countF = 0;
                                    var animPtrF = Raylib.LoadModelAnimations(pathPtrF, &countF);
                                    if (animPtrF != null && countF > 0)
                                    {
                                        var arrF = new ModelAnimation[countF];
                                        for (int i = 0; i < countF; i++) arrF[i] = animPtrF[i];
                                        HiderAnimations = arrF;
                                        // Heuristics similar to seeker
                                        HiderIdleAnimIndex = countF > 3 ? 3 : 0;
                                        HiderWalkAnimIndex = countF > 1 ? 1 : 0;
                                        HiderAlertAnimIndex = countF > 2 ? 2 : Math.Max(0, countF - 1);
                                    }
                                }
                            }
                            catch { /* animations optional for hider */ }
                            HiderYOffset = 0.165f;
                        }
                        else
                        {
                            System.Console.WriteLine($"[WARN] Hider model not found at: {mousePath}. Falling back to primitive.");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"[WARN] Failed to load hider model: {ex.Message}. Falling back to primitive.");
                    }
                }
                catch (Exception ex)
                {
                    _failed = true;
                    System.Console.WriteLine($"[WARN] Failed to load seeker model: {ex.Message}. Falling back to primitive.");
                }
                finally
                {
                    _initialized = true;
                }
            }
        }

        public static void Dispose()
        {
            lock (_lock)
            {
                if (SeekerModel != null)
                {
                    try { Raylib.UnloadModel(SeekerModel.Value); } catch { /* ignore */ }
                    SeekerModel = null;
                }
                if (HiderModel != null)
                {
                    try { Raylib.UnloadModel(HiderModel.Value); } catch { /* ignore */ }
                    HiderModel = null;
                }
                // Note: raylib requires unloading animations individually; wrapper handles GC usually
                _initialized = false;
                _failed = false;
            }
        }
    }
}
