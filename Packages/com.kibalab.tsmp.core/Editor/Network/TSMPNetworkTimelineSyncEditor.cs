using K13A.TSMP.Udon;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace K13A.TSMP.Editor
{
    [CustomEditor(typeof(TSMPNetworkTimelineSync))]
    [CanEditMultipleObjects]
    public sealed class TSMPNetworkTimelineSyncEditor : TSMPNetworkBehaviourEditor
    {
        public override void OnInspectorGUI()
        {
            if (DrawUdonSharpHeader())
                return;

            serializedObject.Update();

            DrawTSMPNetworkSection(false);
            DrawProperty("director");

            TSMPNetworkTimelineSync sync = (TSMPNetworkTimelineSync)target;
            PlayableDirector director = sync.director != null ? sync.director : sync.GetComponent<PlayableDirector>();
            if (director == null)
            {
                EditorGUILayout.HelpBox("Assign a PlayableDirector or add one to this GameObject.", MessageType.Info);
            }
            else if (director.playableAsset == null)
            {
                EditorGUILayout.HelpBox("PlayableDirector has no timeline asset.", MessageType.Info);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Correction", EditorStyles.boldLabel);
            DrawProperty("timeApplyThreshold");

            using (new EditorGUI.DisabledScope(true))
                DrawProperty("encodedTimelineBytes");

            serializedObject.ApplyModifiedProperties();
        }

    }
}
