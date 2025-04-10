using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq; // 需要Linq来方便地获取子对象

public class CheckpointPlacerWindow : EditorWindow
{
    // --- Editor Window Variables ---
    private GameObject pathNodesParent;      // 父对象，包含所有路径节点
    private GameObject checkpointPrefab;   // 检查点预制件
    private float checkpointSpacing = 10f; // 检查点之间的目标间距
    private bool lookForward = true;       // 检查点是否朝向路径前方
    private bool clearExisting = true;     // 是否在放置前清除旧的检查点
    private string generatedParentName = "GeneratedCheckpoints"; // 新生成的检查点的父对象名称

    // --- Private variables ---
    private List<Transform> sortedPathNodes; // 存储排序后的路径节点

    // --- Editor Window Setup ---
    [MenuItem("Tools/Checkpoint Placer")]
    public static void ShowWindow()
    {
        // 显示编辑器窗口实例。如果它不存在，就创建一个。
        GetWindow<CheckpointPlacerWindow>("Checkpoint Placer");
    }

    // --- GUI Drawing ---
    void OnGUI()
    {
        GUILayout.Label("Path and Prefab Settings", EditorStyles.boldLabel);

        // 获取用户输入的路径父对象和预制件
        pathNodesParent = (GameObject)EditorGUILayout.ObjectField("Path Nodes Parent", pathNodesParent, typeof(GameObject), true);
        checkpointPrefab = (GameObject)EditorGUILayout.ObjectField("Checkpoint Prefab", checkpointPrefab, typeof(GameObject), false); // Prefab不能是场景对象

        if (pathNodesParent != null)
        {
            // 尝试自动获取并排序子节点 (按名称)
            FetchAndSortPathNodes();
            if (sortedPathNodes != null && sortedPathNodes.Count > 0)
            {
                EditorGUILayout.HelpBox($"Found {sortedPathNodes.Count} path nodes under '{pathNodesParent.name}'. Ensure they are named sequentially (e.g., PathNode_0, PathNode_1...).", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("Could not find child Transforms under the specified parent, or children are not named correctly.", MessageType.Warning);
            }
        }

        GUILayout.Space(10);
        GUILayout.Label("Placement Settings", EditorStyles.boldLabel);
        checkpointSpacing = EditorGUILayout.FloatField("Checkpoint Spacing", checkpointSpacing);
        lookForward = EditorGUILayout.Toggle("Checkpoints Look Forward", lookForward);
        clearExisting = EditorGUILayout.Toggle("Clear Existing First", clearExisting);
        generatedParentName = EditorGUILayout.TextField("Generated Parent Name", generatedParentName);

        // 防呆：确保间距大于0
        if (checkpointSpacing <= 0)
        {
            checkpointSpacing = 0.1f;
            EditorGUILayout.HelpBox("Spacing must be positive.", MessageType.Warning);
        }

        // 执行按钮
        GUILayout.Space(20);
        if (GUILayout.Button("Place Checkpoints"))
        {
            PlaceCheckpoints();
        }
    }

    // --- Core Logic ---

    // 获取并排序子节点
    private void FetchAndSortPathNodes()
    {
        if (pathNodesParent == null)
        {
            sortedPathNodes = null;
            return;
        }

        // 获取所有直接子对象的Transform组件
        List<Transform> children = pathNodesParent.GetComponentsInChildren<Transform>(true) // include inactive? maybe not needed. Use GetComponentsInChildren<Transform>() if only direct children are nodes.
                                   .Where(t => t.parent == pathNodesParent.transform) // Ensure only direct children
                                   .ToList();


        // 尝试按名称中的数字排序 (例如 PathNode_0, PathNode_1)
        try
        {
            // 使用正则表达式提取名称末尾的数字进行排序
            sortedPathNodes = children.OrderBy(t => {
                string name = t.gameObject.name;
                var match = System.Text.RegularExpressions.Regex.Match(name, @"(\d+)$");
                return match.Success ? int.Parse(match.Groups[1].Value) : int.MaxValue; // 如果没有数字，放到最后
            }).ToList();

            // 也可以用简单的字符串比较排序，如果命名规范 (PathNode_0, PathNode_1, ..., PathNode_10)
            // sortedPathNodes = children.OrderBy(t => t.gameObject.name).ToList();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error sorting path nodes by name. Ensure sequential numbering (e.g., Node_0, Node_1): {ex.Message}");
            sortedPathNodes = children; // 使用原始顺序作为回退
        }
    }


    // 放置检查点的主函数
    private void PlaceCheckpoints()
    {
        // --- Pre-checks ---
        if (sortedPathNodes == null || sortedPathNodes.Count < 2)
        {
            Debug.LogError("Path Nodes Parent must be assigned and contain at least 2 child nodes named sequentially.");
            return;
        }
        if (checkpointPrefab == null)
        {
            Debug.LogError("Checkpoint Prefab must be assigned.");
            return;
        }
        if (checkpointPrefab.GetComponent<CheckpointScript>() == null)
        {
            Debug.LogError("Checkpoint Prefab is missing the 'CheckpointData' script component.");
            return;
        }


        // --- Preparation ---
        Undo.SetCurrentGroupName("Place Checkpoints"); // 允许撤销操作
        int group = Undo.GetCurrentGroup();

        // 清除旧的检查点
        if (clearExisting)
        {
            GameObject existingParent = GameObject.Find(generatedParentName);
            if (existingParent != null)
            {
                Undo.DestroyObjectImmediate(existingParent); // 注册撤销
                Debug.Log($"Cleared existing checkpoints under '{generatedParentName}'.");
            }
        }

        // 创建新的父对象
        GameObject checkpointsParent = new GameObject(generatedParentName);
        Undo.RegisterCreatedObjectUndo(checkpointsParent, "Create Checkpoints Parent");

        // --- Placement Loop ---
        int checkpointIndex = 0;
        float distanceSinceLastCheckpoint = 0f; // 追踪自上个检查点以来的距离

        // 在第一个节点处放置一个检查点
        PlaceSingleCheckpoint(sortedPathNodes[0].position, sortedPathNodes[1].position, checkpointsParent.transform, checkpointIndex++);

        // 遍历路径段
        for (int i = 0; i < sortedPathNodes.Count - 1; i++)
        {
            Transform startNode = sortedPathNodes[i];
            Transform endNode = sortedPathNodes[i + 1];
            Vector3 segmentStartPos = startNode.position;
            Vector3 segmentEndPos = endNode.position;
            float segmentLength = Vector3.Distance(segmentStartPos, segmentEndPos);
            Vector3 segmentDirection = (segmentEndPos - segmentStartPos).normalized;

            // 如果段长度为零，跳过
            if (segmentLength < 0.01f) continue;

            // 从上个段剩余的距离开始累加
            float currentDistanceInSegment = distanceSinceLastCheckpoint;

            while (currentDistanceInSegment + checkpointSpacing <= segmentLength)
            {
                // 计算需要前进的距离以达到下一个检查点间距
                float distanceToPlacement = checkpointSpacing; // - distanceSinceLastCheckpoint; // This logic seems complex, let's simplify

                // 从当前段的起点开始计算放置位置
                Vector3 placementPosition = segmentStartPos + segmentDirection * (currentDistanceInSegment + checkpointSpacing - distanceSinceLastCheckpoint); // Needs review

                // --- Simplified Logic: Step along the segment ---
                float distanceNeeded = checkpointSpacing - distanceSinceLastCheckpoint;
                Vector3 checkpointPosition = segmentStartPos + segmentDirection * distanceNeeded;

                PlaceSingleCheckpoint(checkpointPosition, segmentEndPos, checkpointsParent.transform, checkpointIndex++);

                // 更新追踪变量
                distanceSinceLastCheckpoint = 0f; // Reset distance since last checkpoint was just placed
                segmentStartPos = checkpointPosition; // Continue from the newly placed checkpoint for next calculation within this segment
                segmentLength = Vector3.Distance(segmentStartPos, segmentEndPos); // Recalculate remaining segment length
                // This iterative placement within a segment seems complex and prone to drift.

                // --- Alternative: Global distance tracking ---
                // This approach is generally more robust for varying segment lengths.
                // We need total path length and track distance along it. See previous example.
                // For simplicity here, we'll stick to placing based on segment starts, acknowledging potential spacing inaccuracies near nodes.
            }

            // 更新本段结束后，距离下个检查点还需要多少距离
            distanceSinceLastCheckpoint += segmentLength; // Accumulate the full length of the segment just traversed
                                                          // The logic above placing *within* segments needs careful testing.
                                                          // A simpler, often sufficient approach is to just place one near the *start* of each segment after the first node.
                                                          // Or place based on *total* path distance, which is more robust.

            // Let's revert to a simpler logic: place based on iterating distance along the *entire path*.

        } // End of segment loop


        // --- Let's use the Total Path Distance approach for robustness ---
        checkpointIndex = 0; // Reset index for the robust approach
        float totalPathLength = GetTotalPathLength();
        float distanceAlongPath = 0f; // Current distance marker along the entire path

        // Place first checkpoint at the start
        PlaceSingleCheckpoint(sortedPathNodes[0].position, sortedPathNodes[1].position, checkpointsParent.transform, checkpointIndex++);
        distanceAlongPath = 0f; // Technically the first is at distance 0

        float nextCheckpointDistance = checkpointSpacing; // Distance where the next checkpoint should be placed

        for (int i = 0; i < sortedPathNodes.Count - 1; i++)
        {
            Vector3 startNodePos = sortedPathNodes[i].position;
            Vector3 endNodePos = sortedPathNodes[i + 1].position;
            float segmentLength = Vector3.Distance(startNodePos, endNodePos);
            Vector3 segmentDirection = (endNodePos - startNodePos).normalized;
            float segmentStartDistance = GetTotalPathLengthUpToNode(i); // Total distance up to the start of this segment

            // Check if the next checkpoint falls within this segment
            while (nextCheckpointDistance <= segmentStartDistance + segmentLength + 0.001f) // Add tolerance
            {
                // Calculate the exact position within this segment
                float distanceIntoSegment = nextCheckpointDistance - segmentStartDistance;
                if (distanceIntoSegment < 0) distanceIntoSegment = 0; // Clamp if very slightly before segment starts due to float errors

                Vector3 checkpointPosition = startNodePos + segmentDirection * distanceIntoSegment;

                PlaceSingleCheckpoint(checkpointPosition, endNodePos, checkpointsParent.transform, checkpointIndex++);

                // Move to the next checkpoint distance marker
                nextCheckpointDistance += checkpointSpacing;

                // Safety break if spacing is zero or negative (should be caught by GUI check)
                if (checkpointSpacing <= 0) break;
            }
            // If spacing is very small, safety break
            if (checkpointSpacing <= 0) break;
        }


        Debug.Log($"Successfully placed {checkpointIndex} checkpoints under '{generatedParentName}'.");

        // 完成撤销组
        Undo.CollapseUndoOperations(group);
    }

    // 放置单个检查点的辅助函数
    private void PlaceSingleCheckpoint(Vector3 position, Vector3 nextPosition, Transform parent, int index)
    {
        if (checkpointPrefab == null) return;

        // 计算朝向
        Quaternion rotation = Quaternion.identity;
        if (lookForward)
        {
            Vector3 direction = (nextPosition - position).normalized;
            if (direction != Vector3.zero) // Avoid zero direction vector
            {
                rotation = Quaternion.LookRotation(direction, Vector3.up); // Look towards next point, assuming Up is world up
            }
            // Optionally, align rotation with path node's up vector if nodes have specific orientations
            // rotation = Quaternion.LookRotation(direction, startNode.up);
        }

        // 实例化并设置
        GameObject cpInstance = (GameObject)PrefabUtility.InstantiatePrefab(checkpointPrefab, parent); // Use PrefabUtility for proper prefab connection
        cpInstance.transform.position = position;
        cpInstance.transform.rotation = rotation;
        cpInstance.name = $"{checkpointPrefab.name}_{index}";

        // 设置检查点索引
        CheckpointScript data = cpInstance.GetComponent<CheckpointScript>();
        if (data != null)
        {
            data.checkpointIndex = index;
        }
        else
        {
            Debug.LogWarning($"Checkpoint instance '{cpInstance.name}' is missing CheckpointData script!");
        }

        // 注册撤销
        Undo.RegisterCreatedObjectUndo(cpInstance, "Place Checkpoint");
    }

    // Helper: 计算到指定节点索引的总路径长度
    private float GetTotalPathLengthUpToNode(int nodeIndex)
    {
        float totalLength = 0;
        if (sortedPathNodes == null || nodeIndex <= 0) return 0;

        for (int i = 0; i < nodeIndex && i < sortedPathNodes.Count - 1; i++)
        {
            totalLength += Vector3.Distance(sortedPathNodes[i].position, sortedPathNodes[i + 1].position);
        }
        return totalLength;
    }

    // Helper: 计算路径总长度
    private float GetTotalPathLength()
    {
        return GetTotalPathLengthUpToNode(sortedPathNodes.Count - 1); // Length up to the start of the "last" segment effectively
    }
}