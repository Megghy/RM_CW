using System.Collections.Generic;
using System.Linq;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class RacerAgent : Agent
{
    private int agentId;
    private static int totalAgents = 0;
    [Header("Debug & Manual Control")]
    [Tooltip("启用键盘手动控制")]
    public bool enableManualControl = false;

    [Header("Sensors")]
    public Transform sensorOrigin; // 传感器射线起点，通常位于车辆前部
    public float maxRayDistance = 50f; // 射线最大探测距离
    public LayerMask rayLayerMask = 1; // 射线碰撞层设置
    public int rayDirections = 5; // 射线探测方向数量
    public float rayAngleSpread = 90f; // 射线探测扇形角度范围（例如90度表示左右各45度）

    [Header("Wheel Control")]
    public WheelCollider frontLeftWheel;
    public WheelCollider frontRightWheel;
    public WheelCollider rearLeftWheel;
    public WheelCollider rearRightWheel;
    public float maxSteerAngle = 30f;    // 最大转向角度
    public float motorForce = 500f;   // 电机驱动力
    public float brakeForce = 1000f;   // 刹车力

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
    public float minSpeedForReward = 0.5f;
    [Tooltip("在原地停止多久时触发重新开始 (s)")]
    public float timeToRestart = 3f;
    [Tooltip("到达目标检查点时给予的奖励")]
    public float checkpointReward = 1f;
    [Tooltip("朝向目标检查点移动时给予的奖励的系数")]
    public float towardsCheckpointRewardAmount = 0.001f; // 控制朝向奖励的大小
    [Tooltip("当车辆非常靠近人行道（但仍在路上）时施加的每步惩罚值, 应为负数")]
    public float sidewalkProximityPenalty = -0.002f;
    [Tooltip("击中护栏时施加的惩罚值")]
    public float guardHitPenalty = -0.5f;

    [Header("Road Detection")]
    [Tooltip("定义道路标签")]
    public string roadTag = "Road";
    [Tooltip("定义人行道标签")]
    public string sidewalkTag = "Sidewalk";
    [Tooltip("离开道路时施加的惩罚值")]
    public float offRoadPenalty = -1.0f;
    [Tooltip("检测左右两侧人行道的向下射线的水平偏移距离")]
    public float sidewalkCheckOffsetWidth = 0.8f; // 根据车辆宽度调整
    [Tooltip("检测左右两侧人行道的向下射线的最大检测距离")]
    public float sidewalkCheckDistance = 1.5f;

    // 内部状态
    private Rigidbody rb;
    private float cumulativeReward = 0f; // 当前回合累计奖励值，用于调试
    private float currentSteerAction = 0f;
    private float currentThrottleBrakeAction = 0f;

    private const float maxSpeed = 10f;

    private int stoppedTime = 0;

    // 初始化函数，在游戏开始或Agent启用时调用
    public override void Initialize()
    {
        agentId = totalAgents++;
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("Rigidbody组件缺失！", this);
        }
        checkpointManager = CheckpointManager.Instance;
        // 检查检查点管理器
        checkpointManager.RegisterAgent(this);


        // 检查 WheelCollider 引用
        if (frontLeftWheel == null || frontRightWheel == null || rearLeftWheel == null || rearRightWheel == null)
        {
            Debug.LogError("至少有一个 WheelCollider 引用缺失，请在 Inspector 中设置所有四个车轮碰撞器！", this);
        }
        else
        {
            Debug.Log("所有 WheelCollider 已分配", this);
        }

        Debug.Log($"赛车Agent {agentId} 初始化完成, Decision Id: {GetComponent<DecisionRequester>().GetInstanceID()}", this);
    }


    // 物理相关逻辑更新，用于持续性奖励/惩罚
    void FixedUpdate()
    {
        // 如果启用了手动控制，则覆盖 ML-Agents 的行为
        if (enableManualControl)
        {
            HandleManualInput();
        }
        else // 否则执行 ML-Agents 的常规奖励/惩罚逻辑
        {

            // 时间惩罚
            float timePenalty;
            if (MaxStep > 0)
            {
                timePenalty = baseTimePenaltyFactor / MaxStep;
            }
            else
            {
                timePenalty = fixedTimePenalty;
                if (fixedTimePenalty > 0)
                {
                    Debug.LogWarning("fixedTimePenalty 应为负值以作为惩罚", this);
                    fixedTimePenalty = -Mathf.Abs(fixedTimePenalty);
                }
            }
            AddReward(timePenalty);
            // 人行道检测
            Vector3 verticalOffset = Vector3.up * 0.1f;
            Vector3 rightOffset = transform.right * sidewalkCheckOffsetWidth;

            // 使用辅助方法检查两侧
            var (isCloseToSidewalkLeft, isOnRoadLeft) = CheckSidewalkAndOnRoad(transform.position + verticalOffset - rightOffset);
            var (isCloseToSidewalkRight, isOnRoadRight) = CheckSidewalkAndOnRoad(transform.position + verticalOffset + rightOffset);


            if (!isOnRoadLeft && !isOnRoadRight)
            {
                Debug.Log($"车辆已脱离道路 ({transform.position})！回合结束，惩罚 {offRoadPenalty}");
                AddReward(offRoadPenalty);
                cumulativeReward += offRoadPenalty;
                EndEpisode();
                return; // 脱离道路，立即结束当前 FixedUpdate 帧的处理
            }

            // 如果左侧或右侧紧邻人行道 (但车辆本身仍在路上)，则施加惩罚
            if (isCloseToSidewalkLeft || isCloseToSidewalkRight)
            {
                AddReward(sidewalkProximityPenalty);
                cumulativeReward += sidewalkProximityPenalty;
            }

            // 速度奖励 & 原地停止检测 & 朝向检查点奖励
            float speed = rb.linearVelocity.magnitude;
            if (speed >= minSpeedForReward) // 只有在超过最小速度时才进行后续奖励和检测
            {
                // 速度奖励
                float speedReward = (speed / maxSpeed) * 0.005f;
                AddReward(speedReward);
                cumulativeReward += speedReward;

                // 朝向检查点奖励
                Transform targetCheckpoint = checkpointManager?.GetAgentTargetCheckpoint(this);
                if (targetCheckpoint != null)
                {
                    // 计算从车辆指向目标检查点的方向向量, 抬高0.5f
                    Vector3 directionToTarget = ((targetCheckpoint.position + Vector3.up * 0.5f) - transform.position).normalized;
                    // 获取车辆当前速度方向（归一化）
                    Vector3 velocityDirection = rb.linearVelocity.normalized;

                    // 计算两个方向的点积（结果范围 -1 到 1）
                    float dotProduct = Vector3.Dot(velocityDirection, directionToTarget);

                    // 只在朝向目标时给予奖励 (dotProduct > 0)
                    // 使用 Mathf.Max(0f, dotProduct) 将负值截断为0
                    float towardsReward = Mathf.Max(0f, dotProduct) * towardsCheckpointRewardAmount;
                    AddReward(towardsReward);
                    cumulativeReward += towardsReward; // 更新累计奖励记录
                }

                stoppedTime = 0; // 正在移动，重置停止计时器
            }
            else // 速度低于 minSpeedForReward，检查是否停止过久
            {
                stoppedTime++; // 增加停止的物理帧数计数
                // 使用 Time.fixedDeltaTime 将帧数转换为秒数进行比较
                if ((stoppedTime * Time.fixedDeltaTime) > timeToRestart)
                {
                    Debug.Log($"原地停留过久 ({stoppedTime * Time.fixedDeltaTime:F1}s > {timeToRestart}s), 重新开始");
                    AddReward(-5f);
                    EndEpisode();
                    stoppedTime = 0; // 重置计数器
                }
            }

            if (GetCumulativeReward() < -20f)
            {
                Debug.Log("超过惩罚限制");
                AddReward(-5f);
                EndEpisode();
            }
        }
    }

    // 收集环境观察数据
    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(agentId / totalAgents); // 添加 Agent ID 作为观察数据
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
        if (checkpointManager.GetAgentTargetCheckpoint(this) is { } targetCheckpoint)
        {
            // 抬高0.5m以免陷入地面
            var targetPos = targetCheckpoint.position + Vector3.up * 0.5f;
            Vector3 vectorToTargetWorld = targetPos - transform.position;
            float distanceToTarget = vectorToTargetWorld.magnitude;
            Vector3 dirToTargetWorld = vectorToTargetWorld.normalized; // 目标检查点方向
            Vector3 dirToTargetLocal = transform.InverseTransformDirection(dirToTargetWorld);

            // 射线连接目标点和自身位置
            Debug.DrawLine(transform.position, targetPos, Color.blue, 0.01f);

            sensor.AddObservation(dirToTargetLocal.x);
            sensor.AddObservation(dirToTargetLocal.z);
            float maxExpectedDistance = 50f; // 正则化最大距离
            sensor.AddObservation(Mathf.Clamp01(distanceToTarget / maxExpectedDistance));
        }
        else
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }

        // 4. 自身姿态信息 (Pitch & Roll)
        // Pitch (绕X轴旋转)
        float pitch = (transform.eulerAngles.x > 180f) ? transform.eulerAngles.x - 360f : transform.eulerAngles.x;
        sensor.AddObservation(pitch / 180f); // 归一化到 [-1, 1]

        // Roll (绕Z轴旋转)
        float roll = (transform.eulerAngles.z > 180f) ? transform.eulerAngles.z - 360f : transform.eulerAngles.z;
        sensor.AddObservation(roll / 180f); // 归一化到 [-1, 1]

        // 5. 垂直速度
        float verticalVelocity = rb.linearVelocity.y;
        // 需要根据赛道和车辆能力设定一个合理的归一化范围
        float maxVerticalSpeed = 10f;
        sensor.AddObservation(Mathf.Clamp(verticalVelocity / maxVerticalSpeed, -1f, 1f));

        // 6. 前方地面坡度
        float groundSlope = 0f;
        float slopeRayLength = 2f; // 向下探测的距离
        if (Physics.Raycast(sensorOrigin.position, Vector3.down, out RaycastHit slopeHit, slopeRayLength, rayLayerMask))
        {
            // 计算地面法线与世界垂直方向(Vector3.up)的夹角
            float angle = Vector3.Angle(slopeHit.normal, Vector3.up);
            // 可以根据上坡/下坡给一个符号，例如通过法线的y分量判断
            float slopeSign = Mathf.Sign(Vector3.Dot(slopeHit.normal, transform.forward) * -1); // 粗略判断前后坡度
            groundSlope = Mathf.Clamp(angle / 45f, 0f, 1f) * slopeSign;
            Debug.DrawRay(sensorOrigin.position, Vector3.down * slopeHit.distance, Color.blue);
        }
        else
        {
            Debug.DrawRay(sensorOrigin.position, Vector3.down * slopeRayLength, Color.cyan);
        }
        sensor.AddObservation(groundSlope);

        // 7. 左右侧地面类型检测 (是否为人行道)
        // 计算左右两侧射线的起始点 (略微抬高，并向左右偏移)
        Vector3 verticalOffset = Vector3.up * 0.1f;
        Vector3 rightOffset = transform.right * sidewalkCheckOffsetWidth;
        Vector3 leftRayOrigin = transform.position + verticalOffset - rightOffset;
        Vector3 rightRayOrigin = transform.position + verticalOffset + rightOffset;

        // 绘制左右侧射线
        Debug.DrawRay(leftRayOrigin, Vector3.down * sidewalkCheckDistance, Color.green);
        Debug.DrawRay(rightRayOrigin, Vector3.down * sidewalkCheckDistance, Color.green);

        var (isSidewalkLeft, _) = CheckSidewalkAndOnRoad(leftRayOrigin);
        var (isSidewalkRight, _) = CheckSidewalkAndOnRoad(rightRayOrigin);

        // 添加观察值 (1.0 表示是人行道, 0.0 表示不是)
        sensor.AddObservation(isSidewalkLeft ? 1.0f : 0.0f);
        sensor.AddObservation(isSidewalkRight ? 1.0f : 0.0f);
    }
    private (bool isSidewalk, bool isOnRoad) CheckSidewalkAndOnRoad(Vector3 rayOrigin)
    {
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, sidewalkCheckDistance))
        {
            return (hit.transform.CompareTag(sidewalkTag), hit.transform.CompareTag(roadTag));
        }
        return (false, false);
    }

    private void HandleManualInput()
    {
        float h = Input.GetAxis("Horizontal"); // A/D 或左右箭头
        float v = Input.GetAxis("Vertical");   // W/S 或上下箭头

        // 直接调用核心控制逻辑
        ApplyWheelControl(h, v);
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

            var (correctCheckpoint, lapCompleted) = checkpointManager.AgentPassedCheckpoint(this, other.transform);

            if (correctCheckpoint)
            {
                Debug.Log($"通过正确检查点（{other.name}）！奖励+{checkpointReward}", this);
                if (lapCompleted)
                {
                    float lapBonus = 10.0f;
                    var lapReward = checkpointReward + lapBonus;
                    cumulativeReward += lapReward;
                    Debug.Log($"完成一圈！额外奖励+{lapBonus}，此检查点总奖励：{checkpointReward}", this);

                    // 完成一圈后清空已触发检查点记录
                    triggeredCheckpoints.Clear();
                }
                AddReward(checkpointReward);
                cumulativeReward += checkpointReward;

                // 记录此检查点已触发
                triggeredCheckpoints.Add(checkpointInstanceID);
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
        if (wallCollisionName.Contains(collision.gameObject.name))
        {
            Debug.Log($"撞击到'{collision.gameObject.name}'，惩罚{guardHitPenalty}", this);
            AddReward(guardHitPenalty);
            cumulativeReward += guardHitPenalty;
            //EndEpisode(); // 撞墙不重开只扣分
        }
        else
        {
            Debug.Log($"检测到碰撞：{collision.gameObject.name}", this);

        }
    }

    // 接收神经网络的决策动作
    public override void OnActionReceived(ActionBuffers actions)
    {
        // 如果启用了手动控制，则忽略来自 ML-Agents 的动作
        if (enableManualControl)
        {
            return;
        }

        float steerAction = actions.ContinuousActions[0];
        float throttleBrakeAction = actions.ContinuousActions[1];

        steerAction = Mathf.Clamp(steerAction, -1f, 1f);
        throttleBrakeAction = Mathf.Clamp(throttleBrakeAction, -1f, 1f);

        // 调用核心控制逻辑
        ApplyWheelControl(steerAction, throttleBrakeAction);
    }
    private void ApplyWheelControl(float steerInput, float throttleBrakeInput)
    {
        if (frontLeftWheel == null || frontRightWheel == null || rearLeftWheel == null || rearRightWheel == null)
        {
            // Debug.LogWarning("WheelColliders 未设置，无法应用控制");
            return; // 如果没有设置车轮，则不执行任何操作
        }

        currentSteerAction = steerInput;
        currentThrottleBrakeAction = throttleBrakeInput;

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

            // 应用到后轮
            rearLeftWheel.motorTorque = currentMotorTorque;
            rearRightWheel.motorTorque = currentMotorTorque;

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
        Debug.Log("重置刚体速度", this);

        // 2. 重置位置和朝向
        if (startPosition != null)
        {
            // 左右随机偏移
            //transform.SetPositionAndRotation(startPosition.position, startPosition.rotation);
            transform.SetPositionAndRotation(new(startPosition.position.x, startPosition.position.y, startPosition.position.z + Random.Range(-2f, 2f)), startPosition.rotation);
            Debug.Log($"Agent已重置到起始位置：{startPosition.name}", this);
        }
        else
        {
            transform.SetPositionAndRotation(new Vector3(0, 0.5f, 0), Quaternion.identity);
            Debug.LogWarning("未设置起始位置！重置Agent到世界原点", this);
        }

        // 3. 重置检查点状态
        triggeredCheckpoints.Clear();
        checkpointManager.ResetAgent(this);
        //Debug.Log("已通知检查点管理器重置Agent状态", this);

        // 4. 重置 WheelCollider 状态 (清除上一轮的力和转向)
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
            guiLabelStyle.fontSize = 12; // Smaller font for above car
            guiLabelStyle.normal.background = guiBackgroundTexture;
            guiLabelStyle.normal.textColor = Color.white;
            guiLabelStyle.padding = new RectOffset(5, 5, 5, 5);
            guiLabelStyle.alignment = TextAnchor.MiddleCenter; // Center align the text
        }

        // Convert the position 2 units above the car to screen space
        Vector3 worldPosition = transform.position + Vector3.up * 2.0f;
        Vector3 screenPosition = Camera.main.WorldToScreenPoint(worldPosition);

        // If the car is behind the camera, don't show the UI
        if (screenPosition.z < 0)
            return;

        // Calculate display rect at the world-to-screen position
        float width = 200;
        float height = 80;
        Rect displayRect = new Rect(
            screenPosition.x - (width / 2), // Center horizontally
            Screen.height - screenPosition.y - (height / 2), // Invert Y (GUI Y is inverted from screen Y)
            width,
            height
        );

        // Prepare the display text
        string controlMode = enableManualControl ? "Manual" : "ML-Agent";
        string displayText = $"[{CompletedEpisodes}] {controlMode}\n" +
                             $"Steer: {currentSteerAction:F2}\n" +
                             $"Throttle: {currentThrottleBrakeAction:F2}\n" +
                             $"Reward: {GetCumulativeReward():F1}\n" +
                             $"Speed: {rb.linearVelocity.magnitude:F1}";

        // Draw the label at the position above the car
        GUI.Label(displayRect, displayText, guiLabelStyle);
    }
}