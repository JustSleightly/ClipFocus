#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Reflection;

namespace JustSleightly.ClipFocus
{
    /// <summary>
    /// Automatically focuses the Animation Window on the selected AnimatorState's clip
    /// or BlendTree child clip while maintaining the last selected GameObject as the preview target.
    /// Respects the Animation Window's lock state.
    /// </summary>
    [InitializeOnLoad]
    public class ClipFocus : EditorWindow
    {
        // ═══════════════════════════════════════════════════════════════════
        // STATIC FIELDS
        // ═══════════════════════════════════════════════════════════════════

        // Store the last selected GameObject to use as animation preview target
        // This allows the Animation Window to preview clips on the correct object
        static GameObject _lastGameObject;

        // Track pending clip restoration - needed because Unity's selection system
        // requires a frame delay when switching from clip selection back to GameObject context
        static AnimationClip _pendingClip;

        // ═══════════════════════════════════════════════════════════════════
        // REFLECTION CACHE (for lock state detection)
        // ═══════════════════════════════════════════════════════════════════

        // Cached reflection info to avoid repeated lookups (performance optimization)
        static FieldInfo _lockTrackerField;
        static MemberInfo _isLockedMember;
        static bool _isLockedIsProperty; // true = property, false = field
        static bool _reflectionInitialized;
        static bool _reflectionSuccessful;

        // ═══════════════════════════════════════════════════════════════════
        // INITIALIZATION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Static constructor - automatically called when Unity loads due to [InitializeOnLoad]
        /// Registers callbacks for selection changes and editor updates
        /// </summary>
        static ClipFocus()
        {
            Selection.selectionChanged += UpdateAnimation;
            EditorApplication.update += ProcessPendingClip;
            InitializeReflection();
            Debug.Log("[ClipFocus] Initialized and registered selection callback");
        }

        /// <summary>
        /// Initialize reflection for accessing AnimationWindow's internal lock state
        /// Uses two-step reflection: m_LockTracker field -> isLocked (property or field)
        /// </summary>
        static void InitializeReflection()
        {
            if (_reflectionInitialized) return;
            _reflectionInitialized = true;

            // Step 1: Get m_LockTracker field from AnimationWindow (private field)
            _lockTrackerField = typeof(AnimationWindow).GetField(
                "m_LockTracker",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            if (_lockTrackerField == null)
            {
                Debug.LogWarning("[ClipFocus] Could not find m_LockTracker field on AnimationWindow");
                return;
            }

            System.Type lockTrackerType = _lockTrackerField.FieldType;
            Debug.Log($"[ClipFocus] Found m_LockTracker field, type: {lockTrackerType.FullName}");

            // Step 2: Try to find isLocked - could be property or field, public or non-public
            // Try as public property first
            _isLockedMember = lockTrackerType.GetProperty(
                "isLocked",
                BindingFlags.Public | BindingFlags.Instance
            );

            if (_isLockedMember != null)
            {
                _isLockedIsProperty = true;
                _reflectionSuccessful = true;
                Debug.Log("[ClipFocus] Found isLocked as public property");
                return;
            }

            // Try as non-public property
            _isLockedMember = lockTrackerType.GetProperty(
                "isLocked",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            if (_isLockedMember != null)
            {
                _isLockedIsProperty = true;
                _reflectionSuccessful = true;
                Debug.Log("[ClipFocus] Found isLocked as non-public property");
                return;
            }

            // Try as public field
            _isLockedMember = lockTrackerType.GetField(
                "isLocked",
                BindingFlags.Public | BindingFlags.Instance
            );

            if (_isLockedMember != null)
            {
                _isLockedIsProperty = false;
                _reflectionSuccessful = true;
                Debug.Log("[ClipFocus] Found isLocked as public field");
                return;
            }

            // Try as non-public field (m_IsLocked is the internal backing field)
            _isLockedMember = lockTrackerType.GetField(
                "m_IsLocked",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            if (_isLockedMember != null)
            {
                _isLockedIsProperty = false;
                _reflectionSuccessful = true;
                Debug.Log("[ClipFocus] Found m_IsLocked as non-public field");
                return;
            }

            // Last resort: dump all members for debugging
            Debug.LogWarning("[ClipFocus] Could not find isLocked/m_IsLocked. Dumping all members:");
            foreach (MemberInfo member in lockTrackerType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Debug.Log($"  - {member.MemberType}: {member.Name}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // ANIMATION WINDOW UTILITIES
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get the first open AnimationWindow instance without creating or focusing one
        /// Uses Resources.FindObjectsOfTypeAll to avoid side effects from GetWindow
        /// </summary>
        static AnimationWindow GetAnimationWindowSafe()
        {
            AnimationWindow[] windows = Resources.FindObjectsOfTypeAll<AnimationWindow>();
            return windows.Length > 0 ? windows[0] : null;
        }

        // ═══════════════════════════════════════════════════════════════════
        // LOCK STATE DETECTION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Check if the Animation Window is currently locked via reflection
        /// Returns false if reflection failed (fail-safe: assume unlocked)
        /// </summary>
        static bool IsAnimationWindowLocked(AnimationWindow window)
        {
            // Fail-safe: if reflection isn't available, assume not locked
            if (!_reflectionSuccessful || window == null)
                return false;

            // Step 1: Get the m_LockTracker object from the window instance
            object lockTracker = _lockTrackerField.GetValue(window);
            if (lockTracker == null) return false;

            // Step 2: Read the lock state (property or field)
            bool isLocked;
            if (_isLockedIsProperty)
            {
                isLocked = (bool)((PropertyInfo)_isLockedMember).GetValue(lockTracker);
            }
            else
            {
                isLocked = (bool)((FieldInfo)_isLockedMember).GetValue(lockTracker);
            }

            return isLocked;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SELECTION ROUTING
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Main callback when selection changes in the Unity Editor
        /// Routes to appropriate handler based on the type of object selected
        /// </summary>
        static void UpdateAnimation()
        {
            object selection = EditorUtility.InstanceIDToObject(Selection.activeInstanceID);
            if (selection == null) return;

            // Route to type-specific handlers using pattern matching via 'as' operator
            // Only one of these will execute (the one matching the selection type)
            GameObjectSelected(selection as GameObject);
            StateSelected(selection as AnimatorState);
            ClipSelected(selection as AnimationClip);
        }

        // ═══════════════════════════════════════════════════════════════════
        // SELECTION HANDLERS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Track GameObject selections for animation preview context
        /// Stores reference so Animation Window can preview clips on this object
        /// </summary>
        static void GameObjectSelected(GameObject selection)
        {
            if (!selection) return;

            _lastGameObject = selection;
            Debug.Log($"[ClipFocus] GameObject selected: {selection.name}");
        }

        /// <summary>
        /// Handle AnimatorState selections from Animator Controller window
        /// Focuses Animation Window on the state's motion clip while maintaining preview context
        /// </summary>
        static void StateSelected(AnimatorState selection)
        {
            if (!selection) return;
            if (!HasOpenInstances<AnimationWindow>()) return;

            // Extract the AnimationClip from the state's motion
            // Note: motion could also be a BlendTree, which won't cast to AnimationClip
            AnimationClip clip = selection.motion as AnimationClip;
            if (!clip) return;

            // Check lock state BEFORE making any selection changes
            // Use safe getter to avoid side effects
            AnimationWindow window = GetAnimationWindowSafe();
            if (window == null) return;

            if (IsAnimationWindowLocked(window))
            {
                Debug.Log("[ClipFocus] Animation Window is locked - skipping state focus");
                return;
            }

            Debug.Log($"[ClipFocus] AnimatorState selected: {selection.name} -> Clip: {clip.name}");

            // Now safe to modify selection - lock check passed
            Selection.activeGameObject = _lastGameObject;

            // Use GetWindow to ensure proper focus behavior
            window = GetWindow<AnimationWindow>();
            window.animationClip = clip;

            Selection.SetActiveObjectWithContext(selection, window);
        }

        /// <summary>
        /// Handle AnimationClip selections from BlendTrees
        /// Queues the clip for processing on next update to maintain recordable state
        /// CRITICAL: Do NOT add any window calls here - it breaks the timing-sensitive selection flow
        /// </summary>
        static void ClipSelected(AnimationClip selection)
        {
            if (!selection) return;
            if (!HasOpenInstances<AnimationWindow>()) return;
            if (!_lastGameObject) return;

            Debug.Log($"[ClipFocus] AnimationClip selected: {selection.name}");

            // Queue clip for next frame - direct processing here would conflict with
            // Unity's internal selection handling and break the preview context
            // Lock state will be checked in ProcessPendingClip to preserve timing
            _pendingClip = selection;
        }

        // ═══════════════════════════════════════════════════════════════════
        // DEFERRED PROCESSING
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Process pending clip restoration on editor update
        /// Using EditorApplication.update is faster than EditorApplication.delayCall
        /// CRITICAL: Lock check must happen BEFORE any selection changes
        /// </summary>
        static void ProcessPendingClip()
        {
            if (!_pendingClip) return;

            // Capture and clear pending clip immediately to prevent re-processing
            AnimationClip clipToProcess = _pendingClip;
            _pendingClip = null;

            // Validate references are still valid
            if (!clipToProcess || !_lastGameObject) return;

            // Check lock state BEFORE making any selection changes
            // Use safe getter to avoid side effects
            AnimationWindow window = GetAnimationWindowSafe();
            if (window == null) return;

            if (IsAnimationWindowLocked(window))
            {
                Debug.Log("[ClipFocus] Animation Window is locked - skipping clip restore");
                return;
            }

            Debug.Log($"[ClipFocus] Restoring preview context for: {clipToProcess.name}");

            // CRITICAL: Maintain exact original order for timing-sensitive recordable state
            // 1. Set GameObject context first
            Selection.activeGameObject = _lastGameObject;

            // 2. Get window via GetWindow to ensure proper focus/state
            window = GetWindow<AnimationWindow>();

            // 3. Set clip on window
            window.animationClip = clipToProcess;

            // 4. Set clip as active selection for inspector
            Selection.activeObject = clipToProcess;
        }
    }
}
#endif