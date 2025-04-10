using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CheckpointManager : MonoBehaviour
{
    // 单例模式，方便 Agent 访问 (可选但常用)
    public static CheckpointManager Instance { get; private set; }

    private List<Transform> checkpoints = new(); // 存储所有检查点 Transform
    private Dictionary<RacerAgent, int> agentNextCheckpointIndex = new(); // 存储每个 Agent 下一个要访问的检查点索引

    // 当脚本实例被加载时调用 Awake
    void Awake()
    {
        // 设置单例
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); // 如果已有实例，销毁此重复实例
        }
        else
        {
            Instance = this;
            // DontDestroyOnLoad(gameObject); // 如果需要在场景切换时保留，取消注释此行
        }

        FindAndSortCheckpoints();
    }

    // 查找并排序场景中的所有检查点
    private void FindAndSortCheckpoints()
    {
        // 查找所有带有 "Checkpoint" Tag 的 GameObjects
        GameObject[] checkpointObjects = GameObject.FindGameObjectsWithTag("Checkpoint");

        if (checkpointObjects.Length == 0)
        {
            Debug.LogError("No GameObjects found with the tag 'Checkpoint'. Checkpoint system will not work.", this);
            return;
        }

        // 转换成 Transform 列表
        List<Transform> unsortedCheckpoints = new List<Transform>();
        foreach (GameObject go in checkpointObjects)
        {
            unsortedCheckpoints.Add(go.transform);
        }

        // 按名称中的索引排序 (例如 "CheckPoint_0", "CheckPoint_1", ...)
        try
        {
            // 使用 Linq 的 OrderBy 对检查点进行排序
            // 提取名称中最后一个下划线后的数字部分进行比较
            checkpoints = unsortedCheckpoints.OrderBy(cp =>
            {
                string name = cp.gameObject.name;
                int lastUnderscore = name.LastIndexOf('_');
                if (lastUnderscore != -1 && int.TryParse(name.Substring(lastUnderscore + 1), out int index))
                {
                    return index;
                }
                // 如果名称格式不正确，给一个默认的大值，让它们排在后面或报错
                Debug.LogWarning($"Checkpoint '{name}' does not follow the 'CheckPoint_index' naming convention. Sorting might be incorrect.", cp.gameObject);
                return int.MaxValue;
            }).ToList();

            Debug.Log($"Found and sorted {checkpoints.Count} checkpoints.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error sorting checkpoints: {ex.Message}. Using unsorted list.", this);
            checkpoints = unsortedCheckpoints; // 排序失败则使用未排序列表
        }

        // 可选: 禁用检查点的碰撞体，只保留触发器功能 (如果它们同时有 Collider 和 Trigger)
        // foreach (Transform cp in checkpoints) {
        //     Collider col = cp.GetComponent<Collider>();
        //     if (col != null && !col.isTrigger) {
        //         // 如果需要物理碰撞体和触发器都在，可能需要两个 Collider 组件
        //         // Debug.LogWarning($"Checkpoint '{cp.name}' has a non-trigger Collider. Consider making it a trigger or adding a separate trigger Collider.", cp.gameObject);
        //     }
        // }
    }

    // Agent 注册时调用 (例如在 RacerAgent 的 Initialize 或 OnEnable 中调用)
    public void RegisterAgent(RacerAgent agent)
    {
        if (!agentNextCheckpointIndex.ContainsKey(agent))
        {
            agentNextCheckpointIndex.Add(agent, 0); // 初始目标是第一个检查点
            Debug.Log($"{agent.name} registered. Initial target: {GetAgentTargetCheckpoint(agent)?.name ?? "None"}");
        }
    }

    // Agent 移除时调用 (例如在 RacerAgent 的 OnDisable 中调用)
    public void UnregisterAgent(RacerAgent agent)
    {
        if (agentNextCheckpointIndex.ContainsKey(agent))
        {
            agentNextCheckpointIndex.Remove(agent);
            Debug.Log($"{agent.name} unregistered.");
        }
    }

    // Agent 通过一个检查点时调用 (由 Agent 的 OnTriggerEnter 调用)
    // 返回值: bool - 是否是正确的检查点; bool - 是否完成了一圈
    public (bool correctCheckpoint, bool lapCompleted) AgentPassedCheckpoint(RacerAgent agent, Transform checkpointPassed)
    {
        if (!agentNextCheckpointIndex.ContainsKey(agent) || checkpoints.Count == 0)
        {
            Debug.LogWarning($"{agent.name} passed a checkpoint but is not registered or no checkpoints exist.");
            return (false, false); // Agent 未注册或没有检查点
        }
        int expectedIndex = agentNextCheckpointIndex[agent];
        Transform expectedCheckpoint = checkpoints[expectedIndex];

        if (checkpointPassed == expectedCheckpoint)
        {
            // 通过了正确的检查点
            int nextIndex = (expectedIndex + 1) % checkpoints.Count;
            agentNextCheckpointIndex[agent] = nextIndex; // 更新目标到下一个

            bool lapCompleted = (nextIndex == 0); // 如果下一个是第0个，说明完成了一圈
            //Debug.Log($"{agent.name} passed correct checkpoint: {checkpointPassed.name}. Next: {checkpoints[nextIndex].name}. Lap Completed: {lapCompleted}");
            return (true, lapCompleted);
        }
        else
        {
            // 通过了错误的检查点
            //Debug.Log($"{agent.name} passed incorrect checkpoint: {checkpointPassed.name}. Expected: {expectedCheckpoint.name}");
            return (false, false);
        }
    }

    // 获取指定 Agent 当前的目标检查点 Transform
    public Transform GetAgentTargetCheckpoint(RacerAgent agent)
    {
        if (agentNextCheckpointIndex.ContainsKey(agent) && checkpoints.Count > 0)
        {
            int targetIndex = agentNextCheckpointIndex[agent];
            return checkpoints[targetIndex];
        }
        return null; // 如果 Agent 未注册或没有检查点
    }

    // 获取指定 Agent 当前的目标检查点索引
    public int GetAgentTargetCheckpointIndex(RacerAgent agent)
    {
        if (agentNextCheckpointIndex.ContainsKey(agent))
        {
            return agentNextCheckpointIndex[agent];
        }
        return -1; // Agent 未注册
    }


    // 当 Agent 的回合重置时调用 (由 Agent 的 OnEpisodeBegin 调用)
    public void ResetAgent(RacerAgent agent)
    {
        if (agentNextCheckpointIndex.ContainsKey(agent))
        {
            agentNextCheckpointIndex[agent] = 0; // 重置目标为第一个检查点
            Debug.Log($"{agent.name} reset. Target checkpoint index: 0");
        }
        else
        {
            // 如果 Agent 在重置时还没注册，就注册一下
            RegisterAgent(agent);
        }
    }

#if UNITY_EDITOR // 只在编辑器中绘制 Gizmos
    // (可选) 在 Scene 视图中绘制检查点之间的连线和编号，方便调试
    void OnDrawGizmos()
    {
        if (checkpoints == null || checkpoints.Count < 2) return;

        // 设置 Gizmo 颜色
        Gizmos.color = Color.yellow;

        // 绘制检查点之间的连线
        for (int i = 0; i < checkpoints.Count; i++)
        {
            Transform current = checkpoints[i];
            Transform next = checkpoints[(i + 1) % checkpoints.Count]; // 获取下一个，形成闭环

            if (current != null && next != null)
            {
                Gizmos.DrawLine(current.position, next.position);
            }
        }

        // 绘制检查点编号
        GUIStyle style = new();
        style.normal.textColor = Color.black;
        style.alignment = TextAnchor.MiddleCenter;
        style.fontSize = 14;

        for (int i = 0; i < checkpoints.Count; i++)
        {
            // 在检查点上方显示编号
            UnityEditor.Handles.Label(checkpoints[i].position + Vector3.up * 1.5f, i.ToString(), style);
        }
    }
#endif
}