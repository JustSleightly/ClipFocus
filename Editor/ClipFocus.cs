#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace JustSleightly.ClipFocus
{
    /// <summary>
    /// Automatically focuses the Animation Window on the selected AnimatorState's clip
    /// or BlendTree child clip while maintaining the last selected GameObject as the preview target.
    /// </summary>
    [InitializeOnLoad]
    public class ClipFocus : EditorWindow
    {
        // Store the last selected GameObject to use as animation preview target
        static GameObject _lastGameObject;
        // Track pending clip restoration
        static AnimationClip _pendingClip;

        // Static constructor - registers the selection change callback when Unity loads
        static ClipFocus()
        {
            Selection.selectionChanged += UpdateAnimation;
            EditorApplication.update += ProcessPendingClip;
            Debug.Log("[ClipFocus] Initialized and registered selection callback");
        }

        // Main callback when selection changes in the Unity Editor
        static void UpdateAnimation()
        {
            object selection = EditorUtility.InstanceIDToObject(Selection.activeInstanceID);
            if (selection == null) return;
            
            // Process different selection types
            GameObjectSelected(selection as GameObject);
            StateSelected(selection as AnimatorState);
            ClipSelected(selection as AnimationClip);
        }

        // Track GameObject selections for animation preview context
        static void GameObjectSelected(GameObject selection)
        {
            if (!selection) return;
            _lastGameObject = selection;
            Debug.Log($"[ClipFocus] GameObject selected: {selection.name}");
        }

        // Handle AnimatorState selections - focuses Animation Window on the state's clip
        static void StateSelected(AnimatorState selection)
        {
            if (!selection) return;
            if (!HasOpenInstances<AnimationWindow>()) return;
            
            // Extract the AnimationClip from the state's motion
            AnimationClip clip = selection.motion as AnimationClip;
            if (!clip) return;

            Debug.Log($"[ClipFocus] AnimatorState selected: {selection.name} -> Clip: {clip.name}");

            // Set the preview GameObject and focus the Animation Window
            Selection.activeGameObject = _lastGameObject;
            AnimationWindow window = GetWindow<AnimationWindow>();
            window.animationClip = clip;
            Selection.SetActiveObjectWithContext(selection, window);
        }

        // Handle AnimationClip selections from BlendTrees - maintains recordable state
        static void ClipSelected(AnimationClip selection)
        {
            if (!selection) return;
            if (!HasOpenInstances<AnimationWindow>()) return;
            if (!_lastGameObject) return;

            Debug.Log($"[ClipFocus] AnimationClip selected: {selection.name}");
            
            // Store clip for immediate processing on next update
            _pendingClip = selection;
        }

        // Process pending clip restoration on editor update (faster than delayCall)
        static void ProcessPendingClip()
        {
            if (!_pendingClip) return;
            
            AnimationClip clipToProcess = _pendingClip;
            _pendingClip = null;
            
            if (!clipToProcess || !_lastGameObject) return;
            
            Debug.Log($"[ClipFocus] Restoring preview context for: {clipToProcess.name}");
            
            // Set GameObject context and maintain clip focus
            Selection.activeGameObject = _lastGameObject;
            AnimationWindow window = GetWindow<AnimationWindow>();
            window.animationClip = clipToProcess;
            Selection.activeObject = clipToProcess;
        }
    }
}
#endif