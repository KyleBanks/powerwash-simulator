using System.Collections.Generic;
using KBCore.Refs;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonCharacterController : MonoBehaviour
{
	
	[Header("Base Movement")]
	[Tooltip("Movement speed of the character in m/s")]
	public float MoveSpeed = 4.0f;
	[Tooltip("Acceleration and deceleration")]
	public float SpeedChangeRateAccelerate = 10.0f;
	public float SpeedChangeRateDecelerate = 10.0f;
	
	[Header("Camera")]
	[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
	public Transform CameraContainer;
	[Tooltip("Rotation speed of the camera")]
	public float RotationSpeed = 1.0f;
	[Tooltip("How far in degrees can you move the camera up")]
	public float TopClamp = 90.0f;
	[Tooltip("How far in degrees can you move the camera down")]
	public float BottomClamp = -90.0f;

	[SerializeField, Self] private Transform _transform;
	[SerializeField, Self] private CharacterController _controller;

	private float _speed;
	private float _cameraPitch;
	private float _cameraYaw;
	private Queue<float> _cameraInputSmoothing = new();

	protected virtual void OnEnable()
	{
		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
		
		this._cameraPitch = ClampAngle(this.CameraContainer.localRotation.eulerAngles.x, this.BottomClamp, this.TopClamp);
		this._cameraYaw = this.CalculateCameraYaw(this._transform.rotation);
	}

#if UNITY_EDITOR
	protected virtual void OnValidate()
	{
		this.ValidateRefs();
	}
#endif

	protected virtual void Update()
	{
		this.UpdateMovement();
		this.UpdateManualRotation();
	}
	
	private void UpdateMovement()
	{
		float targetSpeed = this.MoveSpeed;

		// a reference to the players current horizontal velocity
		Vector3 currentVelocity = this._controller.velocity;
		float currentSpeed = new Vector3(currentVelocity.x, 0.0f, currentVelocity.z).magnitude;
		const float speedOffset = 0.1f;

		// accelerate or decelerate to target speed
		if (currentSpeed < targetSpeed - speedOffset || currentSpeed > targetSpeed + speedOffset)
		{
			// creates curved result rather than a linear one giving a more organic speed change
			this._speed = Mathf.Lerp(
				currentSpeed, 
				targetSpeed, 
				Time.deltaTime * (targetSpeed > currentSpeed ? this.SpeedChangeRateAccelerate : this.SpeedChangeRateDecelerate)
			);
		}
		else
		{
			this._speed = targetSpeed;
		}

		this._speed = Mathf.Clamp(this._speed, 0, this.MoveSpeed);

		
		Vector3 movement = Physics.gravity * Time.deltaTime;
		if (!Mathf.Approximately(this._speed, 0))
		{
			Quaternion rotation = this._transform.rotation;
			Vector3 inputDirection = rotation * Vector3.right * Input.GetAxis("Horizontal")
			                         + rotation * Vector3.forward * Input.GetAxis("Vertical");
			movement += inputDirection * (this._speed * Time.deltaTime);
		}
		
		this._controller.Move(movement);
	}

	private void UpdateManualRotation()
	{
		// Prevent camera jerkiness if there are any sudden frame spikes
		const int smoothOverFrames = 30;
		this._cameraInputSmoothing.Enqueue(Mathf.Min(smoothOverFrames / 1f, Time.deltaTime));
		while (this._cameraInputSmoothing.Count > smoothOverFrames)
			this._cameraInputSmoothing.Dequeue();

		Vector2 lookInput = Input.mousePositionDelta;
		if (Mathf.Approximately(lookInput.sqrMagnitude, 0))
			return;
		
		// get the average (smoothed) delta time in the queue
		float deltaTime = 0;
		for (int i = 0; i < this._cameraInputSmoothing.Count; i++)
		{
			// cannot access queue elements by index, so pop each item off the queue and add it to the back
			// until we loop back around to the beginning and end up with the same queue we started with
			float val = this._cameraInputSmoothing.Dequeue();
			deltaTime += val;
			this._cameraInputSmoothing.Enqueue(val);
		}
		if (this._cameraInputSmoothing.Count > 0)
			deltaTime /= this._cameraInputSmoothing.Count;
		
		float pitchInput = lookInput.y;
		float yawInput = lookInput.x;
		float speed = this.RotationSpeed * deltaTime;
		
		this.SetCameraRotation(
			this._cameraPitch - pitchInput * speed,	
			this._cameraYaw + yawInput * speed
		);
	}

	private void SetCameraRotation(float pitch, float yaw)
	{
		this._cameraPitch = this.ClampPitch(pitch);
		this._cameraYaw = this.ClampYaw(yaw);

		this.CameraContainer.localRotation = Quaternion.Euler(this._cameraPitch, 0, 0);
		this._transform.rotation = Quaternion.Euler(0, this._cameraYaw, 0);
	}

	protected virtual float ClampPitch(float pitch)
	{
		return ClampAngle(pitch, this.BottomClamp, this.TopClamp);
	}

	protected virtual float ClampYaw(float yaw)
	{
		return yaw;
	}
	
	protected float CalculateCameraPitch(Quaternion rotation)
		=> rotation.eulerAngles.x;

	private float CalculateCameraYaw(Quaternion rotation)
		=> rotation.eulerAngles.y;

	private static float ClampAngle(float angle, float min, float max)
	{
		while (angle > 180) angle -= 360;
		while (angle < -180) angle += 360;
		return Mathf.Clamp(angle, min, max);
	}
	
}