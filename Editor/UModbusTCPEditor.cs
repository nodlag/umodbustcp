using UnityEditor;
using UnityEngine;
using System.Collections;

// Custom Editor using SerializedProperties.
[CustomEditor(typeof(UModbusTCP))]
public class UModbusTCPEditor : Editor {

    Color COLOR_CONNECTED = new Color(0.0f, 1.0f, 0.0f, 0.4f);
    Color COLOR_NOT_CONNECTED = new Color(1.0f, 0.0f, 0.0f, 0.4f);

    public override void OnInspectorGUI() {
        //Target
        UModbusTCP oUModbusTCP = (UModbusTCP)target;

        //Setup Styles
        GUIStyle oGUIStyleStatus = new GUIStyle();
        oGUIStyleStatus.fontStyle = FontStyle.Bold;
        oGUIStyleStatus.alignment = TextAnchor.MiddleCenter;

        GUIStyle oGUIStyleStatusResult = new GUIStyle();
        oGUIStyleStatusResult.alignment = TextAnchor.MiddleCenter;
        oGUIStyleStatusResult.fixedHeight = 40f;

        GUIStyle oGUIStyleStatusResultInfo = new GUIStyle();
        oGUIStyleStatusResultInfo.alignment = TextAnchor.MiddleCenter;

        //Editor
        GUILayout.Space(20);
        GUILayout.BeginHorizontal();
        GUILayout.Label("STATUS", oGUIStyleStatus);
        GUILayout.EndHorizontal();
        GUILayout.Space(10);
        if(oUModbusTCP.connected) {
            //Style
            GUIStyle oGUIStyleStatusText = new GUIStyle();
            oGUIStyleStatusText.normal.background = MakeTex(1000, 1, COLOR_CONNECTED);
            //Status text
            GUILayout.BeginHorizontal(oGUIStyleStatusText);
            GUILayout.Label("CONNECTED", oGUIStyleStatusResult);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("IP: {0}", oUModbusTCP.ip), oGUIStyleStatusResultInfo);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("PORT: {0}", oUModbusTCP.port), oGUIStyleStatusResultInfo);
            GUILayout.EndHorizontal();
        } else {
            //Style
            GUIStyle oGUIStyleStatusText = new GUIStyle();
            oGUIStyleStatusText.normal.background = MakeTex(1000, 1, COLOR_NOT_CONNECTED);
            //Status text
            GUILayout.BeginHorizontal(oGUIStyleStatusText);
            GUILayout.Label("NOT CONNECTED", oGUIStyleStatusResult);
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(40);

    }

    private Texture2D MakeTex(int _iWidth, int _iHeight, Color _oColor) {
        Color[] oPix = new Color[_iWidth * _iHeight];

        for(int i = 0; i < oPix.Length; i++) {
            oPix[i] = _oColor;
        }

        Texture2D oResult = new Texture2D(_iWidth, _iHeight);
        oResult.SetPixels(oPix);
        oResult.Apply();

        return oResult;
    }

}
