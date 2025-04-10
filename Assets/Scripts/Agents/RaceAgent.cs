using System.Collections.Generic;
using System.Linq;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class RacerAgent : Agent
{
    [Header("Debug & Manual Control")]
    [Tooltip("勾选此项以启用键盘手动控制，用于测试车辆移动。禁用 ML-Agents 控制。")]
    public bool enableManualControl = false; // 新增：手动控制开关

    [Header("Sensors")]
    public Transform sensorOrigin; // 传感器射线起点，通常位于车辆前部
    public float maxRayDistance = 50f; // 射线最大探测距离
    public LayerMask rayLayerMask = 1; // 射线碰撞层设置（默认为Default层，建议在Inspector中设置为障碍物层）
    public int rayDirections = 5; // 射线方向数量：1=仅前方，3=前/左/右，5=前/左前/右前/左/右
    public float rayAngleSpread = 90f; // 射线探测扇形角度范围（例如90度表示左右各45度）

    [Header("Wheel Control")]
    public WheelCollider frontLeftWheel;
    public WheelCollider frontRightWheel;
    public WheelCollider rearLeftWheel;
    public WheelCollider rearRightWheel;
    public float maxSteerAngle = 30f;    // 最大转向角度
    public float motorForce = 1500f;   // 电机驱动力
    public float brakeForce = 3000f;   // 刹车力

    private HashSet<int> triggeredCheckpoints = new HashSet<int>();

    [Header("References & Setup")]
    public CheckpointManager checkpointManager; // 检查点管理器引用
    public Transform startPosition; // 车辆每轮训练的起始位置和朝向
    public string[] wallCollisionName = new string[] {
        "Guard", "Guards"
    };
    [Tooltip("当 MaxStep > 0 时使用的每步时间惩罚的基础值 (除以 MaxStep)")]
    public float baseTimePenaltyFactor = -0.001f; // 基础时间惩罚因子
    [Tooltip("当 MaxStep = 0 (无限) 时使用的固定每步时间惩罚值")]
    public float fixedTimePenalty = -0.0005f; // 无限步数时的固定时间惩罚

    [Header("Rewards")]
    [Tooltip("给予速度奖励所需的最小速度 (m/s)")]
    public float minSpeedForReward = 0.1f;
    [Tooltip("在原地停止多久时触发重新开始 (s)")]
    public float timeToRestart = 3f;

    // 内部状态
    private Rigidbody rb;
    private float cumulativeReward = 0f; // 当前回合累计奖励值，用于调试
    private float currentSteerAction = 0f;
    private float currentThrottleBrakeAction = 0f;

    private const float maxSpeed = 20f;

    private int stoppedTime = 0;


    // 物理相关逻辑更新，用于持续性奖励/惩罚 (此部分未修改)
    void FixedUpdate()
    {
        // 如果启用了手动控制，则覆盖 ML-Agents 的行为
        if (enableManualControl)
        {
            HandleManualInput();
        }
        else // 否则执行 ML-Agents 的常规奖励逻辑
        {
            // 1. 时间惩罚
            float timePenalty;
            if (MaxStep > 0) // 如果设置了有限的最大步数
            {
                // 使用原始的按比例缩放的惩罚
                timePenalty = baseTimePenaltyFactor / MaxStep;
            }
            else // 如果 MaxStep = 0 (无限步数)
            {
                // 使用固定的、小的负奖励作为时间惩罚
                timePenalty = fixedTimePenalty;
                // 注意：确保 fixedTimePenalty 是一个负值
                if (fixedTimePenalty > 0)
                {
                    Debug.LogWarning("fixedTimePenalty 应为负值以作为惩罚", this);
                    fixedTimePenalty = -Mathf.Abs(fixedTimePenalty); // 强制为负
                }
            }
            AddReward(timePenalty);

            // 2. 速度奖励
            float speed = rb.linearVelocity.magnitude;
            if (speed >= minSpeedForReward) // 只有在超过最小速度时才给予奖励
            {
                float speedReward = (speed / maxSpeed) * 0.005f;
                AddReward(speedReward);
                cumulativeReward += speedReward;

                stoppedTime = 0;
            }
            else if(stoppedTime / Time.fixedDeltaTime > timeToRestart) // 原地停止过长
            {
                Debug.Log($"原地停留过久, 重新开始");
                EndEpisode();
                stoppedTime = 0;
            }
        }
    }

    // 初始化函数，在游戏开始或Agent启用时调用
    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("Rigidbody组件缺失！", this);
        }

        // 检查检查点管理器
        if (checkpointManager == null)
        {
            Debug.LogError("检查点管理器引用缺失，请在Inspector中设置！", this);
        }
        else
        {
            checkpointManager.RegisterAgent(this);
            Debug.Log($"Agent已注册至检查点管理器：{checkpointManager.name}", this);
        }

        // 检查 WheelCollider 引用
        if (frontLeftWheel == null || frontRightWheel == null || rearLeftWheel == null || rearRightWheel == null)
        {
            Debug.LogError("至少有一个 WheelCollider 引用缺失，请在 Inspector 中设置所有四个车轮碰撞器！", this);
        }
        else
        {
            Debug.Log("所有 WheelCollider 已分配。", this);
        }

        Debug.Log("赛车Agent初始化完成。", this);
    }
    private void HandleManualInput()
    {
        float h = Input.GetAxis("Horizontal"); // A/D 或左右箭头
        float v = Input.GetAxis("Vertical");   // W/S 或上下箭头

        // 直接调用核心控制逻辑
        ApplyWheelControl(h, v);
    }
    // 收集环境观察数据
    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. 射线观察
        for (int i = 0; i < rayDirections; i++)
        {
            float angle;
            if (rayDirections == 1) angle = 0;
            else angle = (i / (float)(rayDirections - 1) - 0.5f) * rayAngleSpread;

            Vector3 direction = Quaternion.Euler(0, angle, 0) * sensorOrigin.forward;
            float distanceNormalized = 1.0f; // 默认归一化距离为1（最远）

            if (Physics.Raycast(sensorOrigin.position, direction, out RaycastHit hit, maxRayDistance, rayLayerMask))
            {
                distanceNormalized = hit.distance / maxRayDistance; // 归一化碰撞距离（0到1）
                Debug.DrawRay(sensorOrigin.position, direction * hit.distance, Color.red, 0.01f); // 碰撞射线显示为红色
            }
            else
            {
                Debug.DrawRay(sensorOrigin.position, direction * maxRayDistance, Color.green, 0.01f); // 无碰撞射线显示为绿色
            }
            sensor.AddObservation(distanceNormalized);
        }

        // 2. 车辆状态
        float normalizedSpeed = Mathf.Clamp01(rb.linearVelocity.magnitude / maxSpeed);
        sensor.AddObservation(normalizedSpeed);

        float maxAngularVelocity = 2f; // 最大角速度参考值
        float normalizedAngularVelocity = Mathf.Clamp(rb.angularVelocity.y, -maxAngularVelocity, maxAngularVelocity) / maxAngularVelocity;
        sensor.AddObservation(normalizedAngularVelocity);

        // 3. 目标检查点信息
        if (checkpointManager != null && checkpointManager.GetAgentTargetCheckpoint(this) is { } targetCheckpoint)
        {
            Vector3 dirToTargetWorld = (targetCheckpoint.position - transform.position).normalized;
            Vector3 dirToTargetLocal = transform.InverseTransformDirection(dirToTargetWorld);
            sensor.AddObservation(dirToTargetLocal.x);
            sensor.AddObservation(dirToTargetLocal.z);
        }
        else
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }
        // 总观察值数量 = rayDirections + 1(速度) + 1(角速度) + 1(俯仰角) + 2(目标方向) = rayDirections + 5
    }

    // 检查点触发器检测
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Checkpoint"))
        {
            int checkpointInstanceID = other.transform.GetInstanceID();

            // 检查是否是同一个物理帧内或冷却时间内的重复触发
            if (triggeredCheckpoints.Contains(checkpointInstanceID))
            {
                // Debug.Log($"忽略重复触发的检查点：{other.name}", this);
                return; // 忽略重复触发
            }

            // 记录此检查点已触发
            triggeredCheckpoints.Add(checkpointInstanceID);

            var (correctCheckpoint, lapCompleted) = checkpointManager.AgentPassedCheckpoint(this, other.transform);

            if (correctCheckpoint)
            {
                float reward = 1.0f;
                Debug.Log($"通过正确检查点（{other.name}）！奖励+{reward}", this);
                if (lapCompleted)
                {
                    float lapBonus = 2.0f;
                    reward += lapBonus;
                    Debug.Log($"完成一圈！额外奖励+{lapBonus}，此检查点总奖励：{reward}", this);

                    // 完成一圈后清空已触发检查点记录
                    triggeredCheckpoints.Clear();
                }
                AddReward(reward);
                cumulativeReward += reward;
            }
            else
            {
                float penalty = -0.2f;
                Debug.LogWarning($"通过错误检查点（{other.name}）！惩罚{penalty}", this);
                AddReward(penalty);
                cumulativeReward += penalty;
            }
        }
    }

    // 物理碰撞检测
    void OnCollisionEnter(Collision collision)
    {
        Debug.Log($"检测到碰撞：{collision.gameObject.name}", this);
        if (wallCollisionName.Contains(collision.gameObject.name))
        {
            float penalty = -1.0f;
            Debug.Log($"撞击到'{wallCollisionName}'！回合结束，惩罚{penalty}", this);
            AddReward(penalty);
            cumulativeReward += penalty;
            EndEpisode(); // 撞墙终止当前回合
        }
    }

    // 接收神经网络的决策动作 (修改此部分以控制 WheelCollider)
    public override void OnActionReceived(ActionBuffers actions)
    {
        // 如果启用了手动控制，则忽略来自 ML-Agents 的动作
        if (enableManualControl)
        {
            return;
        }

        float steerAction = actions.ContinuousActions[0];
        float throttleBrakeAction = actions.ContinuousActions[1];

        currentSteerAction = steerAction;
        currentThrottleBrakeAction = throttleBrakeAction;

        // 调用核心控制逻辑
        ApplyWheelControl(steerAction, throttleBrakeAction);
    }
    private void ApplyWheelControl(float steerInput, float throttleBrakeInput)
    {
        if (frontLeftWheel == null || frontRightWheel == null || rearLeftWheel == null || rearRightWheel == null)
        {
            // Debug.LogWarning("WheelColliders 未设置，无法应用控制。");
            return; // 如果没有设置车轮，则不执行任何操作
        }

        // 1. 转向控制 (只应用于前轮)
        float currentSteerAngle = Mathf.Clamp(steerInput, -1f, 1f) * maxSteerAngle;
        frontLeftWheel.steerAngle = currentSteerAngle;
        frontRightWheel.steerAngle = currentSteerAngle;

        // 2. 加速/制动控制
        float currentMotorTorque = 0f;
        float currentBrakeTorque = 0f;

        if (throttleBrakeInput >= 0f) // 加速
        {
            currentMotorTorque = Mathf.Clamp(throttleBrakeInput, 0f, 1f) * motorForce;
            currentBrakeTorque = 0f; // 加速时不刹车

            // 应用到驱动轮 (RWD)
            rearLeftWheel.motorTorque = currentMotorTorque;
            rearRightWheel.motorTorque = currentMotorTorque;
            // 前轮也清除马达扭矩（如果是RWD）
            frontLeftWheel.motorTorque = 0f;
            frontRightWheel.motorTorque = 0f;

            // 清除所有轮子刹车力
            frontLeftWheel.brakeTorque = 0f;
            frontRightWheel.brakeTorque = 0f;
            rearLeftWheel.brakeTorque = 0f;
            rearRightWheel.brakeTorque = 0f;
        }
        else // 刹车
        {
            currentBrakeTorque = Mathf.Clamp(-throttleBrakeInput, 0f, 1f) * brakeForce;
            currentMotorTorque = 0f; // 刹车时不加速

            // 清除所有轮子驱动力
            frontLeftWheel.motorTorque = 0f;
            frontRightWheel.motorTorque = 0f;
            rearLeftWheel.motorTorque = 0f;
            rearRightWheel.motorTorque = 0f;

            // 应用刹车力到所有轮子
            frontLeftWheel.brakeTorque = currentBrakeTorque;
            frontRightWheel.brakeTorque = currentBrakeTorque;
            rearLeftWheel.brakeTorque = currentBrakeTorque;
            rearRightWheel.brakeTorque = currentBrakeTorque;
        }
        // Debug.Log($"Steer: {currentSteerAngle}, Motor: {currentMotorTorque}, Brake: {currentBrakeTorque}");
    }

    // 每轮训练开始时调用
    public override void OnEpisodeBegin()
    {

        Debug.Log($"------ 第{CompletedEpisodes + 1}回合开始 ------ 上一轮总分数: {cumulativeReward}");
        cumulativeReward = 0f; // 重置累计奖励记录

        // 1. 重置物理状态
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        Debug.Log("重置刚体速度。", this);

        // 2. 重置位置和朝向
        if (startPosition != null)
        {
            transform.SetPositionAndRotation(startPosition.position, startPosition.rotation);
            Debug.Log($"Agent已重置到起始位置：{startPosition.name}", this);
        }
        else
        {
            transform.SetPositionAndRotation(new Vector3(0, 0.5f, 0), Quaternion.identity);
            Debug.LogWarning("未设置起始位置！重置Agent到世界原点。", this);
        }

        // 3. 重置检查点状态
        triggeredCheckpoints.Clear();
        checkpointManager.ResetAgent(this);
        //Debug.Log("已通知检查点管理器重置Agent状态。", this);

        // 4. 重置 WheelCollider 状态 (清除上一轮的力和转向)
        if (frontLeftWheel != null && frontRightWheel != null && rearLeftWheel != null && rearRightWheel != null)
        {
            frontLeftWheel.motorTorque = 0f;
            frontRightWheel.motorTorque = 0f;
            rearLeftWheel.motorTorque = 0f;
            rearRightWheel.motorTorque = 0f;

            frontLeftWheel.brakeTorque = 0f;
            frontRightWheel.brakeTorque = 0f;
            rearLeftWheel.brakeTorque = 0f;
            rearRightWheel.brakeTorque = 0f;

            frontLeftWheel.steerAngle = 0f;
            frontRightWheel.steerAngle = 0f;
            Debug.Log("已重置 WheelCollider 状态。", this);
        }
    }

    // 人工控制测试方法
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActionsOut = actionsOut.ContinuousActions;
        continuousActionsOut[0] = Input.GetAxis("Horizontal"); // A/D键或左右方向键 -> 转向
        continuousActionsOut[1] = Input.GetAxis("Vertical");   // W/S键或上下方向键 -> 加速/刹车 (W=1, S=-1)
    }

    private Texture2D guiBackgroundTexture;
    private GUIStyle guiLabelStyle;

    void OnDestroy() // 清理纹理
    {
        if (guiBackgroundTexture != null)
        {
            Destroy(guiBackgroundTexture);
        }
    }
    void OnGUI()
    {
        if (guiLabelStyle == null)
        {
            guiBackgroundTexture = new Texture2D(1, 1);
            guiBackgroundTexture.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.1f, 0.7f));
            guiBackgroundTexture.Apply();

            guiLabelStyle = new GUIStyle(GUI.skin.label);
            guiLabelStyle.fontSize = 14;
            guiLabelStyle.normal.background = guiBackgroundTexture;
            guiLabelStyle.normal.textColor = Color.white;
            guiLabelStyle.padding = new RectOffset(5, 5, 5, 5);
        }

        Rect displayRect = new(10, 10, 250, 100); // 稍微调大一点高度以适应内边距

        string controlMode = enableManualControl ? "Manual" : "ML-Agent";
        string displayText = $"[{CompletedEpisodes}] Control: {controlMode}\n" +
                             $"Steer Input: {currentSteerAction:F2}\n" +
                             $"Throttle/Brake Input: {currentThrottleBrakeAction:F2}\n" +
                             $"Current Reward: {cumulativeReward}\n" +
                             $"Speed: {rb.linearVelocity.magnitude}";

        GUI.Label(displayRect, displayText, guiLabelStyle);
    }
}