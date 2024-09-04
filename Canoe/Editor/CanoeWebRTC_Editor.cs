#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FishNet.Transporting.CanoeWebRTC.Editing
{
    [CustomEditor(typeof(CanoeWebRTC))]
    public class CanoeWebRTC_Editor : Editor
    {
        public override void OnInspectorGUI()
        {
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.wordWrap = true;
            labelStyle.alignment = TextAnchor.MiddleCenter;  // Center the text


            EditorGUILayout.LabelField(
                "\n//////  DISCLAIMER  //////\nEnsure to independently verify the filtering of local connections and the Relay Only functionality\n", labelStyle);

            DrawDefaultInspector();
        }
    }
}
#endif