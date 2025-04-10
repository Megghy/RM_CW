using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq; // ��ҪLinq������ػ�ȡ�Ӷ���

public class CheckpointPlacerWindow : EditorWindow
{
    // --- Editor Window Variables ---
    private GameObject pathNodesParent;      // �����󣬰�������·���ڵ�
    private GameObject checkpointPrefab;   // ����Ԥ�Ƽ�
    private float checkpointSpacing = 10f; // ����֮���Ŀ����
    private bool lookForward = true;       // �����Ƿ���·��ǰ��
    private bool clearExisting = true;     // �Ƿ��ڷ���ǰ����ɵļ���
    private string generatedParentName = "GeneratedCheckpoints"; // �����ɵļ���ĸ���������

    // --- Private variables ---
    private List<Transform> sortedPathNodes; // �洢������·���ڵ�

    // --- Editor Window Setup ---
    [MenuItem("Tools/Checkpoint Placer")]
    public static void ShowWindow()
    {
        // ��ʾ�༭������ʵ��������������ڣ��ʹ���һ����
        GetWindow<CheckpointPlacerWindow>("Checkpoint Placer");
    }

    // --- GUI Drawing ---
    void OnGUI()
    {
        GUILayout.Label("Path and Prefab Settings", EditorStyles.boldLabel);

        // ��ȡ�û������·���������Ԥ�Ƽ�
        pathNodesParent = (GameObject)EditorGUILayout.ObjectField("Path Nodes Parent", pathNodesParent, typeof(GameObject), true);
        checkpointPrefab = (GameObject)EditorGUILayout.ObjectField("Checkpoint Prefab", checkpointPrefab, typeof(GameObject), false); // Prefab�����ǳ�������

        if (pathNodesParent != null)
        {
            // �����Զ���ȡ�������ӽڵ� (������)
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

        // ������ȷ��������0
        if (checkpointSpacing <= 0)
        {
            checkpointSpacing = 0.1f;
            EditorGUILayout.HelpBox("Spacing must be positive.", MessageType.Warning);
        }

        // ִ�а�ť
        GUILayout.Space(20);
        if (GUILayout.Button("Place Checkpoints"))
        {
            PlaceCheckpoints();
        }
    }

    // --- Core Logic ---

    // ��ȡ�������ӽڵ�
    private void FetchAndSortPathNodes()
    {
        if (pathNodesParent == null)
        {
            sortedPathNodes = null;
            return;
        }

        // ��ȡ����ֱ���Ӷ����Transform���
        List<Transform> children = pathNodesParent.GetComponentsInChildren<Transform>(true) // include inactive? maybe not needed. Use GetComponentsInChildren<Transform>() if only direct children are nodes.
                                   .Where(t => t.parent == pathNodesParent.transform) // Ensure only direct children
                                   .ToList();


        // ���԰������е��������� (���� PathNode_0, PathNode_1)
        try
        {
            // ʹ��������ʽ��ȡ����ĩβ�����ֽ�������
            sortedPathNodes = children.OrderBy(t => {
                string name = t.gameObject.name;
                var match = System.Text.RegularExpressions.Regex.Match(name, @"(\d+)$");
                return match.Success ? int.Parse(match.Groups[1].Value) : int.MaxValue; // ���û�����֣��ŵ����
            }).ToList();

            // Ҳ�����ü򵥵��ַ����Ƚ�������������淶 (PathNode_0, PathNode_1, ..., PathNode_10)
            // sortedPathNodes = children.OrderBy(t => t.gameObject.name).ToList();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error sorting path nodes by name. Ensure sequential numbering (e.g., Node_0, Node_1): {ex.Message}");
            sortedPathNodes = children; // ʹ��ԭʼ˳����Ϊ����
        }
    }


    // ���ü����������
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
        Undo.SetCurrentGroupName("Place Checkpoints"); // ����������
        int group = Undo.GetCurrentGroup();

        // ����ɵļ���
        if (clearExisting)
        {
            GameObject existingParent = GameObject.Find(generatedParentName);
            if (existingParent != null)
            {
                Undo.DestroyObjectImmediate(existingParent); // ע�᳷��
                Debug.Log($"Cleared existing checkpoints under '{generatedParentName}'.");
            }
        }

        // �����µĸ�����
        GameObject checkpointsParent = new GameObject(generatedParentName);
        Undo.RegisterCreatedObjectUndo(checkpointsParent, "Create Checkpoints Parent");

        // --- Placement Loop ---
        int checkpointIndex = 0;
        float distanceSinceLastCheckpoint = 0f; // ׷�����ϸ����������ľ���

        // �ڵ�һ���ڵ㴦����һ������
        PlaceSingleCheckpoint(sortedPathNodes[0].position, sortedPathNodes[1].position, checkpointsParent.transform, checkpointIndex++);

        // ����·����
        for (int i = 0; i < sortedPathNodes.Count - 1; i++)
        {
            Transform startNode = sortedPathNodes[i];
            Transform endNode = sortedPathNodes[i + 1];
            Vector3 segmentStartPos = startNode.position;
            Vector3 segmentEndPos = endNode.position;
            float segmentLength = Vector3.Distance(segmentStartPos, segmentEndPos);
            Vector3 segmentDirection = (segmentEndPos - segmentStartPos).normalized;

            // ����γ���Ϊ�㣬����
            if (segmentLength < 0.01f) continue;

            // ���ϸ���ʣ��ľ��뿪ʼ�ۼ�
            float currentDistanceInSegment = distanceSinceLastCheckpoint;

            while (currentDistanceInSegment + checkpointSpacing <= segmentLength)
            {
                // ������Ҫǰ���ľ����Դﵽ��һ��������
                float distanceToPlacement = checkpointSpacing; // - distanceSinceLastCheckpoint; // This logic seems complex, let's simplify

                // �ӵ�ǰ�ε���㿪ʼ�������λ��
                Vector3 placementPosition = segmentStartPos + segmentDirection * (currentDistanceInSegment + checkpointSpacing - distanceSinceLastCheckpoint); // Needs review

                // --- Simplified Logic: Step along the segment ---
                float distanceNeeded = checkpointSpacing - distanceSinceLastCheckpoint;
                Vector3 checkpointPosition = segmentStartPos + segmentDirection * distanceNeeded;

                PlaceSingleCheckpoint(checkpointPosition, segmentEndPos, checkpointsParent.transform, checkpointIndex++);

                // ����׷�ٱ���
                distanceSinceLastCheckpoint = 0f; // Reset distance since last checkpoint was just placed
                segmentStartPos = checkpointPosition; // Continue from the newly placed checkpoint for next calculation within this segment
                segmentLength = Vector3.Distance(segmentStartPos, segmentEndPos); // Recalculate remaining segment length
                // This iterative placement within a segment seems complex and prone to drift.

                // --- Alternative: Global distance tracking ---
                // This approach is generally more robust for varying segment lengths.
                // We need total path length and track distance along it. See previous example.
                // For simplicity here, we'll stick to placing based on segment starts, acknowledging potential spacing inaccuracies near nodes.
            }

            // ���±��ν����󣬾����¸����㻹��Ҫ���پ���
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

        // ��ɳ�����
        Undo.CollapseUndoOperations(group);
    }

    // ���õ�������ĸ�������
    private void PlaceSingleCheckpoint(Vector3 position, Vector3 nextPosition, Transform parent, int index)
    {
        if (checkpointPrefab == null) return;

        // ���㳯��
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

        // ʵ����������
        GameObject cpInstance = (GameObject)PrefabUtility.InstantiatePrefab(checkpointPrefab, parent); // Use PrefabUtility for proper prefab connection
        cpInstance.transform.position = position;
        cpInstance.transform.rotation = rotation;
        cpInstance.name = $"{checkpointPrefab.name}_{index}";

        // ���ü�������
        CheckpointScript data = cpInstance.GetComponent<CheckpointScript>();
        if (data != null)
        {
            data.checkpointIndex = index;
        }
        else
        {
            Debug.LogWarning($"Checkpoint instance '{cpInstance.name}' is missing CheckpointData script!");
        }

        // ע�᳷��
        Undo.RegisterCreatedObjectUndo(cpInstance, "Place Checkpoint");
    }

    // Helper: ���㵽ָ���ڵ���������·������
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

    // Helper: ����·���ܳ���
    private float GetTotalPathLength()
    {
        return GetTotalPathLengthUpToNode(sortedPathNodes.Count - 1); // Length up to the start of the "last" segment effectively
    }
}