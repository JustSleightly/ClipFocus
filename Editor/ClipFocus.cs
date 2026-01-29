#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Reflection;
using System.Collections.Generic;

namespace JustSleightly.ClipFocus
{
    /// <summary>
    /// Automatically focuses the Animation Window on the selected AnimatorState's clip
    /// or BlendTree child clip while maintaining the last selected GameObject as the preview target.
    /// Respects each Animation Window's lock state and supports multiple Animation windows.
    /// </summary>
    [InitializeOnLoad]
    public class ClipFocus : EditorWindow
    {
        // ═══════════════════════════════════════════════════════════════════
        // CONSTANTS
        // ═══════════════════════════════════════════════════════════════════

        const string PREF_DEBUG_LOGS = "ClipFocus_DebugLogs";
        const string MENU_DEBUG_LOGS = "JustSleightly/ClipFocus/Enable Debug Logs";

        // ═══════════════════════════════════════════════════════════════════
        // STATIC FIELDS
        // ═══════════════════════════════════════════════════════════════════

        // Store the last selected GameObject to use as animation preview target
        static GameObject _lastGameObject;

        // Track pending clip restoration for BlendTree timing
        static AnimationClip _pendingClip;

        // Reflection cache for lock state detection
        static FieldInfo _lockTrackerField;
        static MemberInfo _isLockedMember;
        static bool _isLockedIsProperty;
        static bool _reflectionInitialized;
        static bool _reflectionSuccessful;

        // Debug logging toggle (persisted via EditorPrefs)
        static bool _debugLogsEnabled;

        // ═══════════════════════════════════════════════════════════════════
        // MENU ITEMS
        // ═══════════════════════════════════════════════════════════════════

        [MenuItem(MENU_DEBUG_LOGS, false)]
        private static void ToggleDebugLogs()
        {
            _debugLogsEnabled = !_debugLogsEnabled;
            EditorPrefs.SetBool(PREF_DEBUG_LOGS, _debugLogsEnabled);
            Menu.SetChecked(MENU_DEBUG_LOGS, _debugLogsEnabled);
            Debug.Log($"<color=yellow>[ClipFocus]</color> Debug logs {(_debugLogsEnabled ? "enabled" : "disabled")}");
        }

        [MenuItem(MENU_DEBUG_LOGS, true)]
        private static bool ToggleDebugLogsValidate()
        {
            Menu.SetChecked(MENU_DEBUG_LOGS, _debugLogsEnabled);
            return true;
        }

        [MenuItem("JustSleightly/Documentation/ClipFocus", false, 5000)]
        private static void Documentation()
        {
            Debug.Log("<color=yellow>[ClipFocus]</color> Opening Documentation for ClipFocus");
            Application.OpenURL("https://github.com/JustSleightly/ClipFocus");
        }

        // ═══════════════════════════════════════════════════════════════════
        // INITIALIZATION
        // ═══════════════════════════════════════════════════════════════════

        static ClipFocus()
        {
            // Load debug preference from EditorPrefs (persists across projects)
            _debugLogsEnabled = EditorPrefs.GetBool(PREF_DEBUG_LOGS, false);

            Selection.selectionChanged += UpdateAnimation;
            EditorApplication.update += ProcessPendingClip;
            InitializeReflection();
            LogDebug("Initialized and registered selection callback");
        }

        static void InitializeReflection()
        {
            if (_reflectionInitialized) return;
            _reflectionInitialized = true;

            // Step 1: Get m_LockTracker field from AnimationWindow
            _lockTrackerField = typeof(AnimationWindow).GetField(
                "m_LockTracker",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            if (_lockTrackerField == null)
            {
                Debug.LogWarning("<color=yellow>[ClipFocus]</color> Could not find m_LockTracker field");
                return;
            }

            System.Type lockTrackerType = _lockTrackerField.FieldType;
            LogDebug($"Found m_LockTracker field, type: {lockTrackerType.FullName}");

            // Step 2: Try to find isLocked - check multiple access patterns
            _isLockedMember = lockTrackerType.GetProperty("isLocked", BindingFlags.Public | BindingFlags.Instance);
            if (_isLockedMember != null)
            {
                _isLockedIsProperty = true;
                _reflectionSuccessful = true;
                LogDebug("Found isLocked as public property");
                return;
            }

            _isLockedMember = lockTrackerType.GetProperty("isLocked", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_isLockedMember != null)
            {
                _isLockedIsProperty = true;
                _reflectionSuccessful = true;
                LogDebug("Found isLocked as non-public property");
                return;
            }

            _isLockedMember = lockTrackerType.GetField("isLocked", BindingFlags.Public | BindingFlags.Instance);
            if (_isLockedMember != null)
            {
                _isLockedIsProperty = false;
                _reflectionSuccessful = true;
                LogDebug("Found isLocked as public field");
                return;
            }

            _isLockedMember = lockTrackerType.GetField("m_IsLocked", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_isLockedMember != null)
            {
                _isLockedIsProperty = false;
                _reflectionSuccessful = true;
                LogDebug("Found m_IsLocked as non-public field");
                return;
            }

            Debug.LogWarning("<color=yellow>[ClipFocus]</color> Could not find isLocked/m_IsLocked");
        }

        // ═══════════════════════════════════════════════════════════════════
        // UTILITIES
        // ═══════════════════════════════════════════════════════════════════

        static void LogDebug(string message)
        {
            if (_debugLogsEnabled)
                Debug.Log($"<color=yellow>[ClipFocus]</color> {message}");
        }

        /// <summary>
        /// Get all open Animation windows without creating or focusing any
        /// </summary>
        static AnimationWindow[] GetAllAnimationWindows()
        {
            return Resources.FindObjectsOfTypeAll<AnimationWindow>();
        }

        /// <summary>
        /// Get all unlocked Animation windows
        /// </summary>
        static List<AnimationWindow> GetUnlockedAnimationWindows()
        {
            List<AnimationWindow> unlocked = new List<AnimationWindow>();
            AnimationWindow[] allWindows = GetAllAnimationWindows();

            foreach (AnimationWindow window in allWindows)
            {
                if (!IsAnimationWindowLocked(window))
                    unlocked.Add(window);
            }

            return unlocked;
        }

        static bool IsAnimationWindowLocked(AnimationWindow window)
        {
            if (!_reflectionSuccessful || window == null)
                return false;

            object lockTracker = _lockTrackerField.GetValue(window);
            if (lockTracker == null) return false;

            bool isLocked;
            if (_isLockedIsProperty)
                isLocked = (bool)((PropertyInfo)_isLockedMember).GetValue(lockTracker);
            else
                isLocked = (bool)((FieldInfo)_isLockedMember).GetValue(lockTracker);

            return isLocked;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SELECTION ROUTING
        // ═══════════════════════════════════════════════════════════════════

        static void UpdateAnimation()
        {
            object selection = EditorUtility.InstanceIDToObject(Selection.activeInstanceID);
            if (selection == null) return;

            GameObjectSelected(selection as GameObject);
            StateSelected(selection as AnimatorState);
            ClipSelected(selection as AnimationClip);
        }

        // ═══════════════════════════════════════════════════════════════════
        // SELECTION HANDLERS
        // ═══════════════════════════════════════════════════════════════════

        static void GameObjectSelected(GameObject selection)
        {
            if (!selection) return;

            _lastGameObject = selection;
            LogDebug($"GameObject selected: {selection.name}");
        }

        static void StateSelected(AnimatorState selection)
        {
            if (!selection) return;
            if (!HasOpenInstances<AnimationWindow>()) return;

            AnimationClip clip = selection.motion as AnimationClip;
            if (!clip) return;

            // Get all unlocked windows
            List<AnimationWindow> unlockedWindows = GetUnlockedAnimationWindows();
            if (unlockedWindows.Count == 0)
            {
                LogDebug("All Animation Windows are locked - skipping state focus");
                return;
            }

            LogDebug($"AnimatorState selected: {selection.name} -> Clip: {clip.name} (applying to {unlockedWindows.Count} window(s))");

            // Set preview context
            Selection.activeGameObject = _lastGameObject;

            // Apply clip to ALL unlocked windows
            foreach (AnimationWindow window in unlockedWindows)
            {
                window.animationClip = clip;
            }

            // Use first unlocked window for selection context
            Selection.SetActiveObjectWithContext(selection, unlockedWindows[0]);
        }

        static void ClipSelected(AnimationClip selection)
        {
            if (!selection) return;
            if (!HasOpenInstances<AnimationWindow>()) return;
            if (!_lastGameObject) return;

            LogDebug($"AnimationClip selected: {selection.name}");

            // Queue clip for next frame to maintain recordable state timing
            _pendingClip = selection;
        }

        // ═══════════════════════════════════════════════════════════════════
        // DEFERRED PROCESSING
        // ═══════════════════════════════════════════════════════════════════

        static void ProcessPendingClip()
        {
            if (!_pendingClip) return;

            AnimationClip clipToProcess = _pendingClip;
            _pendingClip = null;

            if (!clipToProcess || !_lastGameObject) return;

            // Get all unlocked windows
            List<AnimationWindow> unlockedWindows = GetUnlockedAnimationWindows();
            if (unlockedWindows.Count == 0)
            {
                LogDebug("All Animation Windows are locked - skipping clip restore");
                return;
            }

            LogDebug($"Restoring preview context for: {clipToProcess.name} (applying to {unlockedWindows.Count} window(s))");

            // CRITICAL ORDER for recordable state:
            // 1. Set GameObject context first
            Selection.activeGameObject = _lastGameObject;

            // 2. Focus each unlocked window to trigger internal state update for recordable
            //    Then assign the clip to that window
            //    Note: Only the last-focused window will retain full context for locking
            //    This is a Unity limitation - single window use works perfectly
            foreach (AnimationWindow window in unlockedWindows)
            {
                window.Focus();
                window.animationClip = clipToProcess;
            }

            // 3. Set clip as active selection for inspector
            Selection.activeObject = clipToProcess;
        }
    }
}
#endif