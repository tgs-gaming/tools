using UnityEditor;
using UnityEngine;

namespace com.tgs.template.Editor
{
    public class PackageAWelcomeWindow : EditorWindow
    {
        private TGSTemplateService _service;
        private string _message;

        [MenuItem("TGS/Tools/Template/Welcome")]
        public static void Open()
        {
            GetWindow<PackageAWelcomeWindow>("TGS Template Package");
        }

        private void OnEnable()
        {
            _service = new TGSTemplateService();
            _message = _service.GetWelcomeMessage();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("TGS Template Package", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_message, MessageType.Info);

            if (GUILayout.Button("Log Message"))
            {
                Debug.Log(_message);
            }
        }
    }
}
