using UnityEngine;
using UnityEditor;
using System.Linq; // 用于检查 Tag 是否存在
using System.Collections.Generic; // 用于 List
using UnityEditor.SceneManagement; // 用于 MarkSceneDirty

public class ModifyObjectsByNameWindow : EditorWindow
{
    // --- Search Criteria ---
    private string targetGameObjectName = ""; // 要查找的 GameObject 名称
    private bool includeInactive = true;    // 是否包含非激活的 GameObject
    private bool exactMatch = true;         // 是否需要精确名称匹配

    // --- Actions ---
    private bool modifyTag = true;         // 是否修改 Tag
    private string tagName = "Untagged";      // 要应用的 Tag 名称
    private bool ensureTagExists = true;    // 如果 Tag 不存在，是否尝试创建它

    private bool modifyLayer = true;        // 是否修改 Layer
    private int targetLayerIndex = 0;       // 要应用的 Layer (索引)

    // 添加菜单项到 Unity 编辑器顶部菜单 "Tools" 下
    [MenuItem("Tools/Mass Modify Objects by Name (Tag/Layer)")]
    public static void ShowWindow()
    {
        // 显示现有窗口实例，或者如果没有，则创建一个新的。
        GetWindow<ModifyObjectsByNameWindow>("Modify by Name");
    }

    // 绘制窗口内容
    void OnGUI()
    {
        GUILayout.Label("Mass Modify GameObjects by Name", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("查找指定名称的 GameObject，并选择性地修改它们的 Tag 和/或 Layer。", MessageType.Info);

        // --- Search Settings ---
        EditorGUILayout.Space();
        GUILayout.Label("Search Criteria", EditorStyles.boldLabel);
        targetGameObjectName = EditorGUILayout.TextField("GameObject Name", targetGameObjectName);
        includeInactive = EditorGUILayout.Toggle("Include Inactive Objects", includeInactive);
        exactMatch = EditorGUILayout.Toggle(new GUIContent("Exact Name Match", "勾选则名称需完全匹配；不勾选则包含即可 (忽略大小写)。"), exactMatch);

        // --- Actions Settings ---
        EditorGUILayout.Space();
        GUILayout.Label("Actions to Perform", EditorStyles.boldLabel);

        // Tag Modification Section
        modifyTag = EditorGUILayout.BeginToggleGroup("Modify Tag", modifyTag);
        EditorGUI.indentLevel++;
        tagName = EditorGUILayout.TextField("Tag To Apply", tagName);
        ensureTagExists = EditorGUILayout.Toggle("Ensure Tag Exists", ensureTagExists);
        EditorGUI.indentLevel--;
        EditorGUILayout.EndToggleGroup();

        EditorGUILayout.Space(5); // Add a small vertical space

        // Layer Modification Section
        modifyLayer = EditorGUILayout.BeginToggleGroup("Modify Layer", modifyLayer);
        EditorGUI.indentLevel++;
        // LayerField shows layer names but returns the index
        targetLayerIndex = EditorGUILayout.LayerField("Layer To Apply", targetLayerIndex);
        EditorGUI.indentLevel--;
        EditorGUILayout.EndToggleGroup();


        EditorGUILayout.Space();

        // --- Execution Button ---
        if (GUILayout.Button("Apply Changes to Matching Objects"))
        {
            ApplyChanges();
        }
    }

    // 执行修改操作
    void ApplyChanges()
    {
        if (string.IsNullOrWhiteSpace(targetGameObjectName))
        {
            EditorUtility.DisplayDialog("Error", "GameObject Name 不能为空。", "OK");
            return;
        }

        if (!modifyTag && !modifyLayer)
        {
            EditorUtility.DisplayDialog("Info", "没有选择任何操作（既不修改 Tag 也不修改 Layer）。", "OK");
            return;
        }

        // --- 验证 Tag (如果需要修改 Tag) ---
        if (modifyTag)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                EditorUtility.DisplayDialog("Error", "Tag Name 不能为空，因为已勾选 'Modify Tag'。", "OK");
                return;
            }

            bool tagExists = UnityEditorInternal.InternalEditorUtility.tags.Contains(tagName);
            if (!tagExists)
            {
                if (ensureTagExists)
                {
                    if (EditorUtility.DisplayDialog("Create Tag?", $"Tag '{tagName}' 不存在。是否要创建它？\n(注意：这会修改项目设置)", "Yes", "No"))
                    {
                        AddTag(tagName);
                        // 重新检查 Tag 是否成功创建
                        tagExists = UnityEditorInternal.InternalEditorUtility.tags.Contains(tagName);
                        if (!tagExists)
                        {
                            EditorUtility.DisplayDialog("Error", $"无法创建 Tag '{tagName}'。请手动添加。", "OK");
                            return;
                        }
                        Debug.Log($"Tag '{tagName}' 已创建。");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Cancelled", $"操作已取消。Tag '{tagName}' 不存在。", "OK");
                        return;
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", $"Tag '{tagName}' 不存在。请手动添加或勾选 'Ensure Tag Exists'。", "OK");
                    return;
                }
            }
        }

        // --- 验证 Layer (如果需要修改 Layer) ---
        // EditorGUILayout.LayerField 已经保证了选择的是有效 Layer index
        if (modifyLayer)
        {
            // 可以添加一个检查，例如不允许设置为 Ignore Raycast (索引 2)，如果需要的话
            // if(targetLayerIndex == 2) { ... }
        }


        // --- 查找场景中的 GameObject ---
        GameObject[] allGameObjects = UnityEngine.Object.FindObjectsOfType<GameObject>(includeInactive);
        List<GameObject> matchingObjects = new List<GameObject>();

        foreach (GameObject go in allGameObjects)
        {
            if (!includeInactive && !go.activeInHierarchy) continue;

            bool nameMatches;
            if (exactMatch)
            {
                nameMatches = go.name.Equals(targetGameObjectName, System.StringComparison.Ordinal);
            }
            else
            {
                nameMatches = go.name.IndexOf(targetGameObjectName, System.StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (nameMatches)
            {
                matchingObjects.Add(go);
            }
        }

        if (matchingObjects.Count == 0)
        {
            EditorUtility.DisplayDialog("Info", $"在当前场景中没有找到名称" + (exactMatch ? "完全匹配" : "包含") + $" '{targetGameObjectName}' 的 GameObject" + (includeInactive ? " (包括非激活)" : "") + "。", "OK");
            return;
        }

        // --- 收集需要修改的对象 (避免不必要的 Undo 记录) ---
        List<GameObject> objectsToModify = new List<GameObject>();
        foreach (GameObject go in matchingObjects)
        {
            bool needsChange = false;
            if (modifyTag && go.tag != tagName)
            {
                needsChange = true;
            }
            if (modifyLayer && go.layer != targetLayerIndex)
            {
                needsChange = true;
            }

            if (needsChange)
            {
                objectsToModify.Add(go);
            }
        }

        if (objectsToModify.Count == 0)
        {
            string message = $"找到 {matchingObjects.Count} 个匹配的 GameObject，但它们";
            if (modifyTag && modifyLayer) message += $"已经拥有 Tag '{tagName}' 和 Layer '{LayerMask.LayerToName(targetLayerIndex)}'。";
            else if (modifyTag) message += $"已经拥有 Tag '{tagName}'。";
            else if (modifyLayer) message += $"已经拥有 Layer '{LayerMask.LayerToName(targetLayerIndex)}'。";
            message += "\n无需修改。";
            EditorUtility.DisplayDialog("Info", message, "OK");
            return;
        }

        // --- 应用修改 ---
        int taggedCount = 0;
        int layeredCount = 0;

        // **重要**: 在修改之前记录 Undo 操作
        Undo.RecordObjects(objectsToModify.ToArray(), $"Mass Modify Tag/Layer by Name '{targetGameObjectName}'");

        // 遍历并应用修改
        foreach (GameObject go in objectsToModify) // 只遍历确定需要修改的对象
        {
            bool tagChanged = false;
            bool layerChanged = false;

            // 修改 Tag (如果需要且不同)
            if (modifyTag && go.tag != tagName)
            {
                try
                {
                    go.tag = tagName;
                    tagChanged = true;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"无法为 GameObject '{go.name}' 设置 Tag '{tagName}'。错误: {ex.Message}", go);
                }
            }

            // 修改 Layer (如果需要且不同)
            if (modifyLayer && go.layer != targetLayerIndex)
            {
                go.layer = targetLayerIndex;
                layerChanged = true;
            }

            // 计数并标记场景脏
            if (tagChanged) taggedCount++;
            if (layerChanged) layeredCount++;
            if (tagChanged || layerChanged)
            {
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
        }

        // --- 显示结果 ---
        string successMessage = "操作完成。\n";
        if (taggedCount > 0) successMessage += $"成功将 Tag '{tagName}' 应用于 {taggedCount} 个 GameObject。\n";
        if (layeredCount > 0) successMessage += $"成功将 Layer '{LayerMask.LayerToName(targetLayerIndex)}' 应用于 {layeredCount} 个 GameObject。\n";
        if (taggedCount == 0 && layeredCount == 0)
        {
            // 理论上不应该进入这里，因为 objectsToModify.Count > 0
            successMessage = "没有对象被实际修改（可能已是目标状态或在修改过程中出错）。";
            Debug.LogWarning(successMessage);
        }
        else
        {
            Debug.Log(successMessage.Trim());
            EditorUtility.DisplayDialog("Success", successMessage.Trim(), "OK");
        }
    }

    // 辅助函数：在 Project Settings 中添加 Tag
    private static void AddTag(string tagName)
    {
        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/TagManager.asset"));
        if (tagManager == null) { Debug.LogError("无法加载 TagManager.asset!"); return; }
        SerializedProperty tagsProp = tagManager.FindProperty("tags");
        if (tagsProp == null || !tagsProp.isArray) { Debug.LogError("在 TagManager.asset 中找不到 'tags' 属性。"); return; }

        bool found = false;
        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue.Equals(tagName)) { found = true; break; }
        }

        if (!found)
        {
            int newIndex = tagsProp.arraySize;
            tagsProp.InsertArrayElementAtIndex(newIndex);
            SerializedProperty newTagProp = tagsProp.GetArrayElementAtIndex(newIndex);
            newTagProp.stringValue = tagName;
            tagManager.ApplyModifiedProperties();
            tagManager.UpdateIfRequiredOrScript();
            Debug.Log($"已尝试将 Tag '{tagName}' 添加到项目设置。");
        }
        else
        {
            Debug.LogWarning($"Tag '{tagName}' 已经存在于项目设置中。");
        }
    }
}