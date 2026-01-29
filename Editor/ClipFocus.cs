#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Reflection;
using System.Collections.Generic;

namespace JustSleightly.ClipFocus
{
    [InitializeOnLoad]
    public class ClipFocus : EditorWindow
    {
        #region Fields

        const string PrefDebugLogs = "ClipFocus_DebugLogs";
        const string MenuDebugLogs = "JustSleightly/ClipFocus/Enable Debug Logs";

        static GameObject _lastGameObject;      // Cached GameObject for animation preview context
        static AnimationClip _pendingClip;      // Queued clip for deferred BlendTree processing
        static AnimationClip _lastProcessedClip; // Track last processed clip for focus restore
        static AnimationWindow _lastProcessedWindow; // Track which window we processed the clip on
        static bool _lastProcessedWindowWasLocked; // Track previous lock state to detect unlock events
        static bool _debugLogsEnabled;          // User-togglable debug output
        static bool _isProcessing;              // Re-entry guard to prevent self-triggered selection handling
        static EditorWindow _lastFocusedWindow; // Track focus changes

        // Reflection cache for accessing AnimationWindow's internal lock state
        static FieldInfo _lockTrackerField;
        static MemberInfo _isLockedMember;
        static bool _isLockedIsProperty;
        static bool _reflectionInitialized;
        static bool _reflectionSuccessful;

        // Reflection for AnimationWindow's OnSelectionChange method
        static MethodInfo _onSelectionChangeMethod;
        static bool _onSelectionChangeFound;

        #endregion

        #region Menu Items

        [MenuItem(MenuDebugLogs, false)]
        private static void ToggleDebugLogs()
        {
            _debugLogsEnabled = !_debugLogsEnabled;
            EditorPrefs.SetBool(PrefDebugLogs, _debugLogsEnabled);
            Menu.SetChecked(MenuDebugLogs, _debugLogsEnabled);
            Debug.Log($"<color=yellow>[ClipFocus]</color> Debug logs {(_debugLogsEnabled ? "enabled" : "disabled")}");
        }

        [MenuItem(MenuDebugLogs, true)]
        private static bool ToggleDebugLogsValidate()
        {
            Menu.SetChecked(MenuDebugLogs, _debugLogsEnabled);
            return true;
        }

        [MenuItem("JustSleightly/Documentation/ClipFocus", false, 5000)]
        private static void Documentation()
        {
            Debug.Log("<color=yellow>[ClipFocus]</color> Opening Documentation for ClipFocus");
            Application.OpenURL("https://github.com/JustSleightly/ClipFocus");
        }

        #endregion

        #region Initialization

        static ClipFocus()
        {
            _debugLogsEnabled = EditorPrefs.GetBool(PrefDebugLogs, false);
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.update += OnEditorUpdate;
            InitializeLockReflection();
            InitializeOnSelectionChangeReflection();
            LogDebug("Initialized");
        }

        /// <summary>
        /// Sets up reflection to access AnimationWindow's internal m_LockTracker.isLocked
        /// Required because Unity doesn't expose the lock state publicly in this version
        /// </summary>
        static void InitializeLockReflection()
        {
            if (_reflectionInitialized) return;
            _reflectionInitialized = true;

            _lockTrackerField = typeof(AnimationWindow).GetField(
                "m_LockTracker",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            if (_lockTrackerField == null)
            {
                Debug.LogWarning("<color=yellow>[ClipFocus]</color> Could not find m_LockTracker field - lock detection disabled");
                return;
            }

            System.Type trackerType = _lockTrackerField.FieldType;

            _isLockedMember = trackerType.GetProperty("isLocked", BindingFlags.Public | BindingFlags.Instance)
                ?? trackerType.GetProperty("isLocked", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? (MemberInfo)trackerType.GetField("isLocked", BindingFlags.Public | BindingFlags.Instance)
                ?? trackerType.GetField("m_IsLocked", BindingFlags.NonPublic | BindingFlags.Instance);

            if (_isLockedMember != null)
            {
                _isLockedIsProperty = _isLockedMember is PropertyInfo;
                _reflectionSuccessful = true;
                LogDebug($"Lock reflection initialized ({_isLockedMember.MemberType}: {_isLockedMember.Name})");
            }
            else
            {
                Debug.LogWarning("<color=yellow>[ClipFocus]</color> Could not find isLocked member - lock detection disabled");
            }
        }

        /// <summary>
        /// Sets up reflection to access AnimationWindow's OnSelectionChange method
        /// Used to force the window to update its internal GameObject reference
        /// </summary>
        static void InitializeOnSelectionChangeReflection()
        {
            _onSelectionChangeMethod = typeof(AnimationWindow).GetMethod(
                "OnSelectionChange", 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );

            if (_onSelectionChangeMethod != null)
            {
                _onSelectionChangeFound = true;
                LogDebug("Found OnSelectionChange method");
            }
            else
            {
                Debug.LogWarning("<color=yellow>[ClipFocus]</color> Could not find OnSelectionChange method - BlendTree clip context restore disabled");
            }
        }

        #endregion

        #region Update Loop

        /// <summary>
        /// Combined editor update loop for processing pending clips, monitoring focus, and monitoring lock state
        /// </summary>
        static void OnEditorUpdate()
        {
            ProcessPendingClip();
            MonitorWindowFocus();
            MonitorLockStateChanges();
        }

        #endregion

        #region Utilities

        static void LogDebug(string message)
        {
            if (_debugLogsEnabled)
                Debug.Log($"<color=yellow>[ClipFocus]</color> {message}");
        }

        static AnimationWindow[] GetAllAnimationWindows()
        {
            return Resources.FindObjectsOfTypeAll<AnimationWindow>();
        }

        static List<AnimationWindow> GetUnlockedAnimationWindows()
        {
            List<AnimationWindow> unlocked = new List<AnimationWindow>();
            foreach (AnimationWindow window in GetAllAnimationWindows())
            {
                if (!IsWindowLocked(window))
                    unlocked.Add(window);
            }
            return unlocked;
        }

        /// <summary>
        /// Gets the first unlocked Animation window, or null if all are locked
        /// </summary>
        static AnimationWindow GetFirstUnlockedAnimationWindow()
        {
            foreach (AnimationWindow window in GetAllAnimationWindows())
            {
                if (!IsWindowLocked(window))
                    return window;
            }
            return null;
        }

        static bool IsWindowLocked(AnimationWindow window)
        {
            if (!_reflectionSuccessful || window == null)
                return false;

            object lockTracker = _lockTrackerField.GetValue(window);
            if (lockTracker == null) return false;

            if (_isLockedIsProperty)
                return (bool)((PropertyInfo)_isLockedMember).GetValue(lockTracker);
            else
                return (bool)((FieldInfo)_isLockedMember).GetValue(lockTracker);
        }

        /// <summary>
        /// Checks if the currently focused window is an Animator-related window
        /// Used to determine if clip selection came from Animator (BlendTree) vs Project window
        /// </summary>
        static bool IsAnimatorWindowFocused()
        {
            EditorWindow focused = focusedWindow;
            if (focused == null) return false;

            string typeName = focused.GetType().Name;
            string fullTypeName = focused.GetType().FullName;

            LogDebug($"Focused window type: {fullTypeName ?? typeName}");

            // Check for Animator window type (UnityEditor.Graphs.AnimatorControllerTool)
            return typeName.Contains("Animator") || (fullTypeName != null && fullTypeName.Contains("Animator"));
        }

        /// <summary>
        /// Forces the Animation window to update its internal GameObject reference
        /// by calling OnSelectionChange via reflection
        /// </summary>
        static void ForceAnimationWindowStateUpdate(AnimationWindow window)
        {
            if (!_onSelectionChangeFound || window == null) return;

            try
            {
                LogDebug("Calling OnSelectionChange on Animation window");
                _onSelectionChangeMethod.Invoke(window, null);
            }
            catch (System.Exception e)
            {
                LogDebug($"Error forcing state update: {e.Message}");
            }
        }

        /// <summary>
        /// Refreshes the Animation window's GameObject context and clip
        /// Used for focus changes and lock state changes during "fragile" state
        /// </summary>
        static void RefreshWindowContext(AnimationWindow window)
        {
            if (window == null || _lastProcessedClip == null || _lastGameObject == null) return;
            
            LogDebug("Refreshing window context");
            
            _isProcessing = true;
            try
            {
                // Temporarily make GameObject the active selection
                Selection.activeGameObject = _lastGameObject;
                
                // Call OnSelectionChange so window registers the GameObject
                ForceAnimationWindowStateUpdate(window);
                
                // Re-assign our clip to the window (GameObject context is now locked in)
                LogDebug($"Re-assigning clip: {_lastProcessedClip.name}");
                window.animationClip = _lastProcessedClip;
                
                // Restore clip selection with context
                Selection.SetActiveObjectWithContext(_lastProcessedClip, _lastGameObject);
            }
            finally
            {
                _isProcessing = false;
            }
        }

        #endregion

        #region Monitoring

        /// <summary>
        /// Monitors window focus changes and refreshes Animation window state when it regains focus
        /// Only applies to the specific window where we processed the BlendTree clip
        /// Respects locked state - won't refresh locked windows
        /// </summary>
        static void MonitorWindowFocus()
        {
            EditorWindow currentFocused = focusedWindow;
            
            // Only act on focus changes
            if (currentFocused == _lastFocusedWindow) return;
            
            _lastFocusedWindow = currentFocused;

            // Check if THIS is the specific Animation window we processed AND we have a clip to restore
            // Combined type check and cast to avoid warning
            if (currentFocused is AnimationWindow animWindow && 
                currentFocused == _lastProcessedWindow && 
                _lastProcessedClip != null && 
                _lastGameObject != null)
            {
                // Respect locked state - don't refresh if window is now locked
                if (IsWindowLocked(animWindow))
                {
                    LogDebug("Animation window is locked - skipping refresh");
                    return;
                }
                
                // Only restore if selection is still the clip (user hasn't changed it)
                if (Selection.activeObject == _lastProcessedClip)
                {
                    LogDebug("Animation window focused - refreshing context");
                    RefreshWindowContext(animWindow);
                }
                else
                {
                    // User changed selection - clear our tracking
                    LogDebug("User changed selection - clearing BlendTree clip tracking");
                    _lastProcessedClip = null;
                    _lastProcessedWindow = null;
                }
            }
        }

        /// <summary>
        /// Monitors lock state changes on the tracked window
        /// Refreshes context immediately when window is unlocked during "fragile" state
        /// </summary>
        static void MonitorLockStateChanges()
        {
            // Only monitor if we're tracking a window with a processed clip
            if (_lastProcessedWindow == null || _lastProcessedClip == null || _lastGameObject == null)
            {
                _lastProcessedWindowWasLocked = false;
                return;
            }

            bool isCurrentlyLocked = IsWindowLocked(_lastProcessedWindow);
            
            // Detect unlock event: was locked, now unlocked
            if (_lastProcessedWindowWasLocked && !isCurrentlyLocked)
            {
                LogDebug("Detected window unlock - refreshing context to maintain recordable state");
                
                // Only refresh if selection is still our clip
                if (Selection.activeObject == _lastProcessedClip)
                {
                    RefreshWindowContext(_lastProcessedWindow);
                }
            }
            
            // Update tracked lock state
            _lastProcessedWindowWasLocked = isCurrentlyLocked;
        }

        #endregion

        #region Selection Handling

        static void OnSelectionChanged()
        {
            // Skip if we're currently processing to avoid re-entry from our own selection changes
            if (_isProcessing) return;

            object selection = EditorUtility.InstanceIDToObject(Selection.activeInstanceID);
            if (selection == null) return;

            if (selection is GameObject go)
                HandleGameObjectSelected(go);
            else if (selection is AnimatorState state)
                HandleStateSelected(state);
            else if (selection is AnimationClip clip)
                HandleClipSelected(clip);
        }

        /// <summary>
        /// Caches selected GameObjects to use as animation preview target
        /// </summary>
        static void HandleGameObjectSelected(GameObject selection)
        {
            _lastGameObject = selection;
            LogDebug($"GameObject cached: {selection.name}");
            
            // Clear BlendTree clip tracking when user selects GameObject
            // This exits the "fragile" state and stops continuous re-assignment
            _lastProcessedClip = null;
            _lastProcessedWindow = null;
            _lastProcessedWindowWasLocked = false;
        }

        /// <summary>
        /// Handles AnimatorState selection from Animator window
        /// Immediately applies clip to all unlocked Animation windows
        /// </summary>
        static void HandleStateSelected(AnimatorState selection)
        {
            if (!HasOpenInstances<AnimationWindow>()) return;

            AnimationClip clip = selection.motion as AnimationClip;
            if (!clip) return;

            List<AnimationWindow> unlockedWindows = GetUnlockedAnimationWindows();
            if (unlockedWindows.Count == 0)
            {
                LogDebug("All windows locked - skipping");
                return;
            }

            LogDebug($"State: {selection.name} -> {clip.name} ({unlockedWindows.Count} window(s))");

            _isProcessing = true;
            try
            {
                Selection.activeGameObject = _lastGameObject;

                foreach (AnimationWindow window in unlockedWindows)
                    window.animationClip = clip;

                Selection.SetActiveObjectWithContext(selection, unlockedWindows[0]);
            }
            finally
            {
                _isProcessing = false;
            }
        }

        /// <summary>
        /// Handles AnimationClip selection from BlendTree (Animator window only)
        /// Queues for deferred processing to achieve recordable state
        /// Ignores clips selected from Project window
        /// </summary>
        static void HandleClipSelected(AnimationClip selection)
        {
            if (!HasOpenInstances<AnimationWindow>()) return;
            if (!_lastGameObject) return;

            // Only process clips selected from Animator window (BlendTree children)
            // Ignore clips selected from Project window
            if (!IsAnimatorWindowFocused())
            {
                LogDebug($"Clip selected outside Animator window - ignoring: {selection.name}");
                return;
            }

            LogDebug($"Clip queued: {selection.name}");
            _pendingClip = selection;
        }

        #endregion

        #region Deferred Processing

        /// <summary>
        /// Processes queued BlendTree clips on EditorApplication.update
        /// This timing is critical - faster methods fail to achieve recordable state
        /// Only applies to one window since Focus() is required and breaks multi-window recording
        /// </summary>
        static void ProcessPendingClip()
        {
            if (!_pendingClip) return;

            AnimationClip clipToProcess = _pendingClip;
            _pendingClip = null;

            if (!clipToProcess || !_lastGameObject) return;

            // Get first unlocked window only - Focus() is required for recordable state
            // but breaks multi-window recording, so we only apply to one window
            AnimationWindow window = GetFirstUnlockedAnimationWindow();
            if (window == null)
            {
                LogDebug("All windows locked - skipping");
                return;
            }

            LogDebug($"Processing: {clipToProcess.name}");

            _isProcessing = true;
            try
            {
                // Step 1: Establish GameObject context for animator reference
                Selection.activeGameObject = _lastGameObject;

                // Step 2: Focus window to trigger Unity's internal state update
                window.Focus();
                
                // Step 3: Force Animation window to register GameObject BEFORE setting clip
                ForceAnimationWindowStateUpdate(window);
                
                // Step 4: NOW set the clip - window already knows about GameObject
                window.animationClip = clipToProcess;

                // Step 5: Set clip as selection while preserving GameObject context
                Selection.SetActiveObjectWithContext(clipToProcess, _lastGameObject);
                
                // Step 6: Cache this clip and window for focus/lock monitoring
                _lastProcessedClip = clipToProcess;
                _lastProcessedWindow = window;
                _lastProcessedWindowWasLocked = IsWindowLocked(window);
            }
            finally
            {
                _isProcessing = false;
            }
        }

        #endregion
    }
}
#endif