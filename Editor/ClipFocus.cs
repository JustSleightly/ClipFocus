#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

namespace JustSleightly.ClipFocus
{
    [InitializeOnLoad]
    public class ClipFocus : EditorWindow
    {
        #region Fields

        const string PrefDebugLogs = "ClipFocus_DebugLogs";
        const string MenuDebugLogs = "JustSleightly/ClipFocus/Enable Debug Logs";

        static GameObject _lastGameObject;
        static AnimationClip _pendingClip;
        static AnimationClip _lastProcessedClip;
        static readonly HashSet<AnimationWindow> ProcessedWindows = new HashSet<AnimationWindow>();
        static readonly Dictionary<AnimationWindow, bool> AllWindowLockStates = new Dictionary<AnimationWindow, bool>();
        static bool _debugLogsEnabled;
        static bool _isProcessing;
        static EditorWindow _lastFocusedWindow;
        static bool _userWarningShown;

        // Reflection cache for AnimationWindow lock state
        static FieldInfo _lockTrackerField;
        static MemberInfo _isLockedMember;
        static bool _isLockedIsProperty;
        static bool _reflectionInitialized;
        static bool _reflectionSuccessful;

        // Reflection for AnimationWindow OnSelectionChange
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
            Log($"Debug logs {(_debugLogsEnabled ? "enabled" : "disabled")}");
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
            Log("Initialized");
        }

        static void InitializeLockReflection()
        {
            if (_reflectionInitialized) return;
            _reflectionInitialized = true;

            _lockTrackerField = typeof(AnimationWindow).GetField("m_LockTracker", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_lockTrackerField == null)
            {
                Log("Could not find m_LockTracker field - lock detection disabled", LogLevel.Warning);
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
                Log($"Lock reflection initialized ({_isLockedMember.MemberType}: {_isLockedMember.Name})");
            }
            else
            {
                Log("Could not find isLocked member - lock detection disabled", LogLevel.Warning);
            }
        }

        static void InitializeOnSelectionChangeReflection()
        {
            _onSelectionChangeMethod = typeof(AnimationWindow).GetMethod("OnSelectionChange", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (_onSelectionChangeMethod != null)
            {
                _onSelectionChangeFound = true;
                Log("Found OnSelectionChange method");
            }
            else
            {
                Log("Could not find OnSelectionChange method - BlendTree clip context restore disabled", LogLevel.Warning);
            }
        }

        #endregion

        #region Update Loop

        static void OnEditorUpdate()
        {
            ProcessPendingClip();
            MonitorWindowFocus();
            MonitorLockStateChanges();
        }

        #endregion

        #region Utilities

        enum LogLevel
        {
            Info,    // Standard log
            Warning  // Warning log
        }

        static void Log(string message, LogLevel level = LogLevel.Info)
        {
            // All logs only show when debug enabled
            if (!_debugLogsEnabled) return;

            string formattedMessage = $"<color=yellow>[ClipFocus]</color> {message}";

            switch (level)
            {
                case LogLevel.Warning:
                    Debug.LogWarning(formattedMessage);
                    break;
                default:
                    Debug.Log(formattedMessage);
                    break;
            }
        }

        static void ShowUserWarningOnce()
        {
            if (_userWarningShown) return;
            _userWarningShown = true;
            Debug.LogWarning("<color=yellow>[ClipFocus]</color> No valid Animator component found. Select a GameObject with an Animator component and assign a controller to use ClipFocus.");
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

        static bool IsAnimatorWindowFocused()
        {
            EditorWindow focused = focusedWindow;
            if (focused == null) return false;

            string typeName = focused.GetType().Name;
            string fullTypeName = focused.GetType().FullName;

            Log($"Focused window type: {fullTypeName ?? typeName}");
            return typeName.Contains("Animator") || (fullTypeName != null && fullTypeName.Contains("Animator"));
        }

        /// <summary>
        /// Validates that the GameObject has an Animator with a controller containing the specified clip
        /// </summary>
        static bool ValidateAnimatorContext(AnimationClip clip)
        {
            if (_lastGameObject == null)
            {
                Log("No GameObject cached - cannot apply clip");
                return false;
            }

            Animator animator = _lastGameObject.GetComponentInParent<Animator>();
            if (animator == null)
            {
                Log($"GameObject '{_lastGameObject.name}' has no Animator component - cannot apply clip '{clip.name}'");
                ShowUserWarningOnce();
                return false;
            }

            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (controller == null)
            {
                Log($"Animator on '{animator.gameObject.name}' has no RuntimeAnimatorController - cannot apply clip '{clip.name}'");
                ShowUserWarningOnce();
                return false;
            }

            AnimationClip[] controllerClips = controller.animationClips;
            if (controllerClips == null || controllerClips.Length == 0)
            {
                Log($"RuntimeAnimatorController on '{animator.gameObject.name}' contains no clips - cannot apply clip '{clip.name}'");
                return false;
            }

            if (!controllerClips.Contains(clip))
            {
                Log($"Clip '{clip.name}' does not exist in controller on '{animator.gameObject.name}'");
                return false;
            }

            Log($"Validation passed: Clip '{clip.name}' exists in controller on '{animator.gameObject.name}'");
            return true;
        }

        /// <summary>
        /// Calls OnSelectionChange on the Animation window via reflection to update its internal GameObject reference
        /// </summary>
        static void ForceAnimationWindowStateUpdate(AnimationWindow window)
        {
            if (!_onSelectionChangeFound || window == null) return;

            try
            {
                Log("Calling OnSelectionChange on Animation window");
                _onSelectionChangeMethod.Invoke(window, null);
            }
            catch (System.Exception e)
            {
                Log($"Error forcing state update: {e.Message}");
            }
        }

        /// <summary>
        /// Refreshes a window's GameObject context and clip assignment during "fragile" state
        /// </summary>
        static void RefreshWindowContext(AnimationWindow window)
        {
            if (window == null || _lastProcessedClip == null || _lastGameObject == null) return;
            
            Log("Refreshing window context");
            
            _isProcessing = true;
            try
            {
                // Temporarily select GameObject so window registers it as animation target
                Selection.activeGameObject = _lastGameObject;
                ForceAnimationWindowStateUpdate(window);
                
                // Re-assign clip now that window has GameObject context
                Log($"Re-assigning clip: {_lastProcessedClip.name}");
                window.animationClip = _lastProcessedClip;
                
                // Restore clip selection while keeping GameObject as context
                Selection.SetActiveObjectWithContext(_lastProcessedClip, _lastGameObject);
            }
            finally
            {
                _isProcessing = false;
            }
        }

        /// <summary>
        /// Processes a window that was locked during initial clip processing and has now been unlocked
        /// </summary>
        static void ProcessNewlyUnlockedWindow(AnimationWindow window)
        {
            if (window == null || _lastProcessedClip == null || _lastGameObject == null) return;

            Log($"Processing newly unlocked window with clip: {_lastProcessedClip.name}");

            _isProcessing = true;
            try
            {
                Selection.activeGameObject = _lastGameObject;
                ForceAnimationWindowStateUpdate(window);
                window.animationClip = _lastProcessedClip;

                // Add to tracking so future focus/lock changes are monitored
                ProcessedWindows.Add(window);

                Selection.SetActiveObjectWithContext(_lastProcessedClip, _lastGameObject);
            }
            finally
            {
                _isProcessing = false;
            }
        }

        static void ClearTracking()
        {
            _lastProcessedClip = null;
            ProcessedWindows.Clear();
            AllWindowLockStates.Clear();
        }

        #endregion

        #region Monitoring

        /// <summary>
        /// Monitors focus changes to refresh window context when processed windows regain focus
        /// </summary>
        static void MonitorWindowFocus()
        {
            EditorWindow currentFocused = focusedWindow;
            if (currentFocused == _lastFocusedWindow) return;
            
            _lastFocusedWindow = currentFocused;

            if (currentFocused is AnimationWindow animWindow && 
                ProcessedWindows.Contains(animWindow) && 
                _lastProcessedClip != null && 
                _lastGameObject != null)
            {
                if (IsWindowLocked(animWindow))
                {
                    Log("Animation window is locked - skipping refresh");
                    return;
                }
                
                if (Selection.activeObject == _lastProcessedClip)
                {
                    Log("Processed Animation window focused - refreshing context");
                    RefreshWindowContext(animWindow);
                }
                else
                {
                    // User selected something else - exit "fragile" state
                    Log("User changed selection - clearing BlendTree clip tracking");
                    ClearTracking();
                }
            }
        }

        /// <summary>
        /// Monitors ALL windows for unlock events to process newly unlocked windows with current clip
        /// </summary>
        static void MonitorLockStateChanges()
        {
            if (_lastProcessedClip == null || _lastGameObject == null)
            {
                AllWindowLockStates.Clear();
                return;
            }

            // Remove destroyed windows
            ProcessedWindows.RemoveWhere(w => w == null);

            AnimationWindow[] allWindows = GetAllAnimationWindows();

            foreach (AnimationWindow window in allWindows)
            {
                if (window == null) continue;

                bool isCurrentlyLocked = IsWindowLocked(window);
                bool wasLocked = AllWindowLockStates.ContainsKey(window) && AllWindowLockStates[window];
                
                // Detect unlock event (was locked → now unlocked)
                if (wasLocked && !isCurrentlyLocked && Selection.activeObject == _lastProcessedClip)
                {
                    if (ProcessedWindows.Contains(window))
                    {
                        // Already processed - just refresh to maintain recordable state
                        Log("Processed window unlocked - refreshing context");
                        RefreshWindowContext(window);
                    }
                    else
                    {
                        // New unlock - process it with current clip
                        Log("New window unlocked - processing with current clip");
                        ProcessNewlyUnlockedWindow(window);
                    }
                }
                
                // Update lock state tracking for next iteration
                AllWindowLockStates[window] = isCurrentlyLocked;
            }
        }

        #endregion

        #region Selection Handling

        static void OnSelectionChanged()
        {
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

        static void HandleGameObjectSelected(GameObject selection)
        {
            _lastGameObject = selection;
            Log($"GameObject cached: {selection.name}");
            ClearTracking();
        }

        /// <summary>
        /// Handles AnimatorState clicks - applies to all unlocked windows immediately (no deferred processing)
        /// </summary>
        static void HandleStateSelected(AnimatorState selection)
        {
            if (!HasOpenInstances<AnimationWindow>()) return;

            AnimationClip clip = selection.motion as AnimationClip;
            if (!clip || !ValidateAnimatorContext(clip)) return;

            List<AnimationWindow> unlockedWindows = GetUnlockedAnimationWindows();
            if (unlockedWindows.Count == 0)
            {
                Log("All windows locked - skipping");
                return;
            }

            Log($"State: {selection.name} -> {clip.name} ({unlockedWindows.Count} window(s))");

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
        /// Handles BlendTree clip clicks from Animator window - queues for deferred processing
        /// </summary>
        static void HandleClipSelected(AnimationClip selection)
        {
            if (!HasOpenInstances<AnimationWindow>() || !_lastGameObject) return;

            // Ignore clips selected from Project window
            if (!IsAnimatorWindowFocused())
            {
                Log($"Clip selected outside Animator window - ignoring: {selection.name}");
                return;
            }

            Log($"Clip queued: {selection.name}");
            _pendingClip = selection;
        }

        #endregion

        #region Deferred Processing

        /// <summary>
        /// Processes BlendTree clips on EditorApplication.update (timing critical for recordable state)
        /// Even if all windows are locked, caches clip for processing when windows unlock
        /// </summary>
        static void ProcessPendingClip()
        {
            if (!_pendingClip) return;

            AnimationClip clipToProcess = _pendingClip;
            _pendingClip = null;

            if (!clipToProcess || !_lastGameObject || !ValidateAnimatorContext(clipToProcess)) return;

            List<AnimationWindow> unlockedWindows = GetUnlockedAnimationWindows();
            
            ProcessedWindows.Clear();

            // Initialize lock tracking for ALL windows to detect future unlocks
            AllWindowLockStates.Clear();
            foreach (AnimationWindow window in GetAllAnimationWindows())
            {
                if (window != null)
                    AllWindowLockStates[window] = IsWindowLocked(window);
            }

            // Cache clip even if all windows locked (for unlock detection)
            _lastProcessedClip = clipToProcess;

            if (unlockedWindows.Count == 0)
            {
                Log("All windows locked - will process when unlocked");
                return;
            }

            Log($"Processing: {clipToProcess.name} across {unlockedWindows.Count} window(s)");

            _isProcessing = true;
            try
            {
                Selection.activeGameObject = _lastGameObject;

                // Process each unlocked window with GameObject context + clip
                foreach (AnimationWindow window in unlockedWindows)
                {
                    ForceAnimationWindowStateUpdate(window);
                    window.animationClip = clipToProcess;
                    ProcessedWindows.Add(window);
                }

                Selection.SetActiveObjectWithContext(clipToProcess, _lastGameObject);
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