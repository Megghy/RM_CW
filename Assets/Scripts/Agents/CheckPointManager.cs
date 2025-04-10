using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CheckpointManager : MonoBehaviour
{
    // ����ģʽ������ Agent ���� (��ѡ������)
    public static CheckpointManager Instance { get; private set; }

    private List<Transform> checkpoints = new(); // �洢���м��� Transform
    private Dictionary<RacerAgent, int> agentNextCheckpointIndex = new(); // �洢ÿ�� Agent ��һ��Ҫ���ʵļ�������

    // ���ű�ʵ��������ʱ���� Awake
    void Awake()
    {
        // ���õ���
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); // �������ʵ�������ٴ��ظ�ʵ��
        }
        else
        {
            Instance = this;
            // DontDestroyOnLoad(gameObject); // �����Ҫ�ڳ����л�ʱ������ȡ��ע�ʹ���
        }

        FindAndSortCheckpoints();
    }

    // ���Ҳ����򳡾��е����м���
    private void FindAndSortCheckpoints()
    {
        // �������д��� "Checkpoint" Tag �� GameObjects
        GameObject[] checkpointObjects = GameObject.FindGameObjectsWithTag("Checkpoint");

        if (checkpointObjects.Length == 0)
        {
            Debug.LogError("No GameObjects found with the tag 'Checkpoint'. Checkpoint system will not work.", this);
            return;
        }

        // ת���� Transform �б�
        List<Transform> unsortedCheckpoints = new List<Transform>();
        foreach (GameObject go in checkpointObjects)
        {
            unsortedCheckpoints.Add(go.transform);
        }

        // �������е��������� (���� "CheckPoint_0", "CheckPoint_1", ...)
        try
        {
            // ʹ�� Linq �� OrderBy �Լ����������
            // ��ȡ���������һ���»��ߺ�����ֲ��ֽ��бȽ�
            checkpoints = unsortedCheckpoints.OrderBy(cp =>
            {
                string name = cp.gameObject.name;
                int lastUnderscore = name.LastIndexOf('_');
                if (lastUnderscore != -1 && int.TryParse(name.Substring(lastUnderscore + 1), out int index))
                {
                    return index;
                }
                // ������Ƹ�ʽ����ȷ����һ��Ĭ�ϵĴ�ֵ�����������ں���򱨴�
                Debug.LogWarning($"Checkpoint '{name}' does not follow the 'CheckPoint_index' naming convention. Sorting might be incorrect.", cp.gameObject);
                return int.MaxValue;
            }).ToList();

            Debug.Log($"Found and sorted {checkpoints.Count} checkpoints.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error sorting checkpoints: {ex.Message}. Using unsorted list.", this);
            checkpoints = unsortedCheckpoints; // ����ʧ����ʹ��δ�����б�
        }

        // ��ѡ: ���ü������ײ�壬ֻ�������������� (�������ͬʱ�� Collider �� Trigger)
        // foreach (Transform cp in checkpoints) {
        //     Collider col = cp.GetComponent<Collider>();
        //     if (col != null && !col.isTrigger) {
        //         // �����Ҫ������ײ��ʹ��������ڣ�������Ҫ���� Collider ���
        //         // Debug.LogWarning($"Checkpoint '{cp.name}' has a non-trigger Collider. Consider making it a trigger or adding a separate trigger Collider.", cp.gameObject);
        //     }
        // }
    }

    // Agent ע��ʱ���� (������ RacerAgent �� Initialize �� OnEnable �е���)
    public void RegisterAgent(RacerAgent agent)
    {
        if (!agentNextCheckpointIndex.ContainsKey(agent))
        {
            agentNextCheckpointIndex.Add(agent, 0); // ��ʼĿ���ǵ�һ������
            Debug.Log($"{agent.name} registered. Initial target: {GetAgentTargetCheckpoint(agent)?.name ?? "None"}");
        }
    }

    // Agent �Ƴ�ʱ���� (������ RacerAgent �� OnDisable �е���)
    public void UnregisterAgent(RacerAgent agent)
    {
        if (agentNextCheckpointIndex.ContainsKey(agent))
        {
            agentNextCheckpointIndex.Remove(agent);
            Debug.Log($"{agent.name} unregistered.");
        }
    }

    // Agent ͨ��һ������ʱ���� (�� Agent �� OnTriggerEnter ����)
    // ����ֵ: bool - �Ƿ�����ȷ�ļ���; bool - �Ƿ������һȦ
    public (bool correctCheckpoint, bool lapCompleted) AgentPassedCheckpoint(RacerAgent agent, Transform checkpointPassed)
    {
        if (!agentNextCheckpointIndex.ContainsKey(agent) || checkpoints.Count == 0)
        {
            Debug.LogWarning($"{agent.name} passed a checkpoint but is not registered or no checkpoints exist.");
            return (false, false); // Agent δע���û�м���
        }
        int expectedIndex = agentNextCheckpointIndex[agent];
        Transform expectedCheckpoint = checkpoints[expectedIndex];

        if (checkpointPassed == expectedCheckpoint)
        {
            // ͨ������ȷ�ļ���
            int nextIndex = (expectedIndex + 1) % checkpoints.Count;
            agentNextCheckpointIndex[agent] = nextIndex; // ����Ŀ�굽��һ��

            bool lapCompleted = (nextIndex == 0); // �����һ���ǵ�0����˵�������һȦ
            //Debug.Log($"{agent.name} passed correct checkpoint: {checkpointPassed.name}. Next: {checkpoints[nextIndex].name}. Lap Completed: {lapCompleted}");
            return (true, lapCompleted);
        }
        else
        {
            // ͨ���˴���ļ���
            //Debug.Log($"{agent.name} passed incorrect checkpoint: {checkpointPassed.name}. Expected: {expectedCheckpoint.name}");
            return (false, false);
        }
    }

    // ��ȡָ�� Agent ��ǰ��Ŀ����� Transform
    public Transform GetAgentTargetCheckpoint(RacerAgent agent)
    {
        if (agentNextCheckpointIndex.ContainsKey(agent) && checkpoints.Count > 0)
        {
            int targetIndex = agentNextCheckpointIndex[agent];
            return checkpoints[targetIndex];
        }
        return null; // ��� Agent δע���û�м���
    }

    // ��ȡָ�� Agent ��ǰ��Ŀ���������
    public int GetAgentTargetCheckpointIndex(RacerAgent agent)
    {
        if (agentNextCheckpointIndex.ContainsKey(agent))
        {
            return agentNextCheckpointIndex[agent];
        }
        return -1; // Agent δע��
    }


    // �� Agent �Ļغ�����ʱ���� (�� Agent �� OnEpisodeBegin ����)
    public void ResetAgent(RacerAgent agent)
    {
        if (agentNextCheckpointIndex.ContainsKey(agent))
        {
            agentNextCheckpointIndex[agent] = 0; // ����Ŀ��Ϊ��һ������
            Debug.Log($"{agent.name} reset. Target checkpoint index: 0");
        }
        else
        {
            // ��� Agent ������ʱ��ûע�ᣬ��ע��һ��
            RegisterAgent(agent);
        }
    }

#if UNITY_EDITOR // ֻ�ڱ༭���л��� Gizmos
    // (��ѡ) �� Scene ��ͼ�л��Ƽ���֮������ߺͱ�ţ��������
    void OnDrawGizmos()
    {
        if (checkpoints == null || checkpoints.Count < 2) return;

        // ���� Gizmo ��ɫ
        Gizmos.color = Color.yellow;

        // ���Ƽ���֮�������
        for (int i = 0; i < checkpoints.Count; i++)
        {
            Transform current = checkpoints[i];
            Transform next = checkpoints[(i + 1) % checkpoints.Count]; // ��ȡ��һ�����γɱջ�

            if (current != null && next != null)
            {
                Gizmos.DrawLine(current.position, next.position);
            }
        }

        // ���Ƽ�����
        GUIStyle style = new();
        style.normal.textColor = Color.black;
        style.alignment = TextAnchor.MiddleCenter;
        style.fontSize = 14;

        for (int i = 0; i < checkpoints.Count; i++)
        {
            // �ڼ����Ϸ���ʾ���
            UnityEditor.Handles.Label(checkpoints[i].position + Vector3.up * 1.5f, i.ToString(), style);
        }
    }
#endif
}