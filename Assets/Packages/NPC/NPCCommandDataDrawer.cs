#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(NPCCommandData))]
public class NPCCommandDataDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var typeProp = property.FindPropertyRelative("Type");
        var posProp = property.FindPropertyRelative("TargetPosition");
        var tfProp = property.FindPropertyRelative("TargetTransform");
        var speedProp = property.FindPropertyRelative("MoveSpeed");
        var durProp = property.FindPropertyRelative("Duration");
        var animProp = property.FindPropertyRelative("AnimationTrigger");

        var type = (NPCCommandType)typeProp.enumValueIndex;

        float y = position.y;
        EditorGUI.PropertyField(Row(ref y, position), typeProp);

        switch (type)
        {
            case NPCCommandType.MoveToPosition:
                EditorGUI.PropertyField(Row(ref y, position), posProp);
                EditorGUI.PropertyField(Row(ref y, position), speedProp);
                break;
            case NPCCommandType.MoveToTarget:
                EditorGUI.PropertyField(Row(ref y, position), tfProp);
                EditorGUI.PropertyField(Row(ref y, position), speedProp);
                break;
            case NPCCommandType.LookAtTarget:
                EditorGUI.PropertyField(Row(ref y, position), tfProp);
                break;
            case NPCCommandType.Wait:
                EditorGUI.PropertyField(Row(ref y, position), durProp);
                break;
            case NPCCommandType.PlayAnimation:
                EditorGUI.PropertyField(Row(ref y, position), animProp);
                break;
            case NPCCommandType.Stop:
                // ничего лишнего
                break;
        }

        EditorGUI.EndProperty();
    }

    private Rect Row(ref float y, Rect parent)
    {
        float h = EditorGUIUtility.singleLineHeight;
        var r = new Rect(parent.x, y, parent.width, h);
        y += h + EditorGUIUtility.standardVerticalSpacing;
        return r;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var type = (NPCCommandType)property.FindPropertyRelative("Type").enumValueIndex;
        float lineH = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        switch (type)
        {
            case NPCCommandType.MoveToPosition:
            case NPCCommandType.MoveToTarget:
                return lineH * 3;
            case NPCCommandType.LookAtTarget:
            case NPCCommandType.Wait:
            case NPCCommandType.PlayAnimation:
                return lineH * 2;
            case NPCCommandType.Stop:
                return lineH;
        }
        return lineH;
    }
}
#endif