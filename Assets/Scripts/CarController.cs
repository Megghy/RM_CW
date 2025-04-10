using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleWheelController : MonoBehaviour
{//轮子碰撞器组件
    public WheelCollider frontLeftWheel;
    public WheelCollider frontRightWheel;
    public WheelCollider rearLeftWheel;
    public WheelCollider rearRightWheel;

    // 轮子模型
    public Transform frontLeftWheelModel;
    public Transform frontRightWheelModel;
    public Transform rearLeftWheelModel;
    public Transform rearRightWheelModel;

    // 车辆参数
    [Header("车辆参数")]
    public float maxMotorTorque = 1500f;
    public float maxBrakeTorque = 3000f;
    public float maxSteeringAngle = 30f;
    // 新增参数
    [Header("高级设置")]
    public float autoBreakSpeed = 0.5f;     // 自动刹车的速度阈值
    public float autoBreakTorque = 500f;    // 自动刹车扭矩
    // 输入值
    private float motorInput;
    private float steeringInput;
    private bool isBraking;
    private Rigidbody vehicleRigidbody;

    private void Awake()
    {
        vehicleRigidbody = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        GetInput();
        UpdateWheelModels();
    }

    private void FixedUpdate()
    {
        // 应用驱动力
        ApplyMotor();

        // 应用转向
        ApplySteering();

        // 应用刹车
        ApplyBrake();
    }

    private void GetInput()
    {
        // 获取输入
        motorInput = Keyboard.current.wKey.isPressed ? 1f : (Keyboard.current.sKey.isPressed ? -1f : 0f);
        steeringInput = Keyboard.current.aKey.isPressed ? -1f : (Keyboard.current.dKey.isPressed ? 1f : 0f);
        isBraking = Keyboard.current.spaceKey.isPressed;
    }

    private void ApplyMotor()
    {
        // 当有输入时应用马达扭矩，否则确保扭矩为零
        float torque = maxMotorTorque * motorInput;

        // 应用马达扭矩
        rearLeftWheel.motorTorque = torque;
        rearRightWheel.motorTorque = torque;

        // 确保在没有输入时，如果车辆几乎静止，应用轻微刹车
        if (Mathf.Abs(motorInput) < 0.1f && vehicleRigidbody.linearVelocity.magnitude < autoBreakSpeed)
        {
            ApplyAutoBreak();
        }
    }

    private void ApplySteering()
    {
        float steeringAngle = maxSteeringAngle * steeringInput;
        frontLeftWheel.steerAngle = steeringAngle;
        frontRightWheel.steerAngle = steeringAngle;
    }

    private void ApplyBrake()
    {
        // 用户刹车
        if (isBraking)
        {
            frontLeftWheel.brakeTorque = maxBrakeTorque;
            frontRightWheel.brakeTorque = maxBrakeTorque;
            rearLeftWheel.brakeTorque = maxBrakeTorque;
            rearRightWheel.brakeTorque = maxBrakeTorque;
        }
        // 没有用户刹车且没有自动刹车时，确保刹车扭矩为0
        else if (Mathf.Abs(motorInput) >= 0.1f || vehicleRigidbody.linearVelocity.magnitude >= autoBreakSpeed)
        {
            frontLeftWheel.brakeTorque = 0;
            frontRightWheel.brakeTorque = 0;
            rearLeftWheel.brakeTorque = 0;
            rearRightWheel.brakeTorque = 0;
        }
    }

    // 新增：自动刹车功能
    private void ApplyAutoBreak()
    {
        frontLeftWheel.brakeTorque = autoBreakTorque;
        frontRightWheel.brakeTorque = autoBreakTorque;
        rearLeftWheel.brakeTorque = autoBreakTorque;
        rearRightWheel.brakeTorque = autoBreakTorque;

        // 确保马达扭矩为0
        rearLeftWheel.motorTorque = 0;
        rearRightWheel.motorTorque = 0;
    }

    private void UpdateWheelModels()
    {
        // 如果没有设置轮子模型，则跳过
        if (frontLeftWheelModel == null || frontRightWheelModel == null ||
            rearLeftWheelModel == null || rearRightWheelModel == null)
            return;

        UpdateWheelModel(frontLeftWheel, frontLeftWheelModel);
        UpdateWheelModel(frontRightWheel, frontRightWheelModel);
        UpdateWheelModel(rearLeftWheel, rearLeftWheelModel);
        UpdateWheelModel(rearRightWheel, rearRightWheelModel);
    }

    private void UpdateWheelModel(WheelCollider collider, Transform model)
    {
        collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
        model.SetPositionAndRotation(position, rotation);
    }
}