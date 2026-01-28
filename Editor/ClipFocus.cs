#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace JustSleightly.ClipFocus
{
    [InitializeOnLoad]
    public class ClipFocus : EditorWindow
    {
        static GameObject _lastGameObject;

        static ClipFocus()
        {
            Selection.selectionChanged += UpdateAnimation;
        }

        static void UpdateAnimation()
        {
            object selection = EditorUtility.InstanceIDToObject(Selection.activeInstanceID);
            if (selection == null) return;
            GameObjectSelected(selection as GameObject);
            StateSelected(selection as AnimatorState);
        }

        static void GameObjectSelected(GameObject selection)
        {
            if (!selection) return;
            _lastGameObject = selection;
        }

        static void StateSelected(AnimatorState selection)
        {
            if (!selection) return;
            if (!HasOpenInstances<AnimationWindow>()) return;
            AnimationClip clip = selection.motion as AnimationClip;
            if (!clip) return;

            Selection.activeGameObject = _lastGameObject;
            AnimationWindow window = GetWindow<AnimationWindow>();
            window.animationClip = clip;
            Selection.SetActiveObjectWithContext(selection, window);
        }
    }
}
#endif