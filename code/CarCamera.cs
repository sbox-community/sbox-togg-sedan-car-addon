using Sandbox;
using System;
using Sandbox.Utility;
using System.Linq;
using Sandbox.Diagnostics;
public class ToggCamera
{
	private ToggSedan parent;
	public static Logger Logger = new( "Togg Sedan Camera" );
	protected virtual float MinFov => 80.0f;
	protected virtual float MaxFov => 100.0f;
	protected virtual float MaxFovSpeed => 1000.0f;
	protected virtual float FovSmoothingSpeed => 4.0f;
	protected virtual float OrbitCooldown => 0.6f;
	protected virtual float OrbitSmoothingSpeed => 25.0f;
	protected virtual float OrbitReturnSmoothingSpeed => 4.0f;
	protected virtual float MinOrbitPitch => -25.0f;
	protected virtual float MaxOrbitPitch => 70.0f;
	protected virtual float FixedOrbitPitch => 10.0f;
	protected virtual float OrbitHeight => 35.0f;
	protected virtual float OrbitDistance => 130.0f;
	protected virtual float MaxOrbitReturnSpeed => 100.0f;
	protected virtual float MinCarPitch => -60.0f;
	protected virtual float MaxCarPitch => 60.0f;
	protected virtual float CarPitchSmoothingSpeed => 0.4f;
	protected virtual float CollisionRadius => 8.0f;
	protected virtual float ShakeSpeed => 200.0f;
	protected virtual float ShakeSpeedThreshold => 1000.0f;
	protected virtual float ShakeMaxSpeed => 2500.0f;
	protected virtual float ShakeMaxLength => 2.0f;

	public bool orbitEnabled = false;
	public TimeSince timeSinceOrbit = 0.0f;
	public Angles orbitAngles = Angles.Zero;
	public Rotation orbitYawRot = Rotation.Identity;
	public Rotation orbitPitchRot = Rotation.Identity;
	public float currentFov;
	public float carPitch = 0;
	public Vector3 carPosition;
	private Vector3 lastCarPosition = new();

	private Vector3 Position = new();
	private Rotation Rotation = new();
	private float FieldOfView = 0f;

	private CameraComponent Camera;
	public ToggCamera( ToggSedan car )
	{
		parent = car;
		Camera = Game.ActiveScene.GetAllObjects( true ).First( x => x.Name == "Camera" ).GetComponent<CameraComponent>();
		currentFov = MinFov;
		orbitYawRot = Rotation.FromYaw( parent.WorldRotation.Yaw() );
		orbitAngles = (orbitYawRot * orbitPitchRot).Angles();
	}

	public void Update()
	{
		if ( !parent.IsValid() || parent.GameObject.GetComponent<Rigidbody>() is not Rigidbody physicbody ) return; // TODO: statik yap
		var body = physicbody.PhysicsBody; 

		if ( !body.IsValid() ) return;

		var speed = parent.MovementSpeed;
		var speedAbs = Math.Abs( speed );
		if ( orbitEnabled && timeSinceOrbit > OrbitCooldown )
			orbitEnabled = false;

		var carRot = parent.WorldRotation;
		carPitch = carPitch.LerpTo( parent.Grounded ? carRot.Pitch().Clamp( MinCarPitch, MaxCarPitch ) * (speed < 0.0f ? -1.0f : 1.0f) : 0.0f, Time.Delta * CarPitchSmoothingSpeed );
		if ( orbitEnabled )
		{
			var slerpAmount = Time.Delta * OrbitSmoothingSpeed;

			orbitYawRot = Rotation.Slerp( orbitYawRot, Rotation.From( 0.0f, orbitAngles.yaw, 0.0f ), slerpAmount );
			orbitPitchRot = Rotation.Slerp( orbitPitchRot, Rotation.From( orbitAngles.pitch + carPitch, 0.0f, 0.0f ), slerpAmount );
		}
		else
		{
			var targetPitch = FixedOrbitPitch.Clamp( MinOrbitPitch, MaxOrbitPitch );
			var targetYaw = speed < 0.0f ? carRot.Yaw() + 180.0f : carRot.Yaw();
			var slerpAmount = MaxOrbitReturnSpeed > 0.0f ? Time.Delta * (speedAbs / MaxOrbitReturnSpeed).Clamp( 0.0f, OrbitReturnSmoothingSpeed ) : 1.0f;

			orbitYawRot = Rotation.Slerp( orbitYawRot, Rotation.FromYaw( targetYaw ), slerpAmount );
			orbitPitchRot = Rotation.Slerp( orbitPitchRot, Rotation.FromPitch( targetPitch + carPitch ), slerpAmount );

			orbitAngles.pitch = orbitPitchRot.Pitch();
			orbitAngles.yaw = orbitYawRot.Yaw();
			orbitAngles = orbitAngles.Normal;
		}
		DoThirdPerson( parent, body );

		if ( parent.experimental_camera )
		{
			Position = Vector3.Lerp( Position, carPosition, 5f * Time.Delta ); // experimental camera
			Position += ((lastCarPosition - parent.WorldPosition) / 10f).ClampLength( 2f ); // experimental camera
		}
		else
			Position = carPosition;

		lastCarPosition = parent.WorldPosition;

		currentFov = MaxFovSpeed > 0.0f ? currentFov.LerpTo( MinFov.LerpTo( MaxFov, speedAbs / MaxFovSpeed ), Time.Delta * FovSmoothingSpeed ) : MaxFov;
		FieldOfView = currentFov;

		ApplyShake( speedAbs );
		FrameSimulate();
	}

	private void DoThirdPerson( ToggSedan car, PhysicsBody body )
	{
		//if ( car.experimental_camera )
		//	Rotation = Rotation.Lerp( orbitYawRot, orbitYawRot * orbitPitchRot, 5f * Time.Delta );
		//else
			Rotation = orbitYawRot * orbitPitchRot;

		var carPos = car.WorldPosition + car.WorldRotation * (body.LocalMassCenter * car.WorldScale);
		var startPos = carPos;
		var targetPos = startPos + Rotation.Backward * (OrbitDistance * car.WorldScale * 2) + (Vector3.Up * (OrbitHeight * car.WorldScale));

		var tr = Game.ActiveScene.Trace.Ray( startPos, targetPos )
			.IgnoreGameObject( car.GameObject )
			.IgnoreGameObject( car.Driver )
			.Radius( Math.Clamp( CollisionRadius * car.WorldScale.x, 2.0f, 10.0f ) )
			.UsePhysicsWorld(true)
			//.UseRenderMeshes(true)
			.Run();

		carPosition = tr.EndPosition;

		//Viewer = null;
	}

	public void BuildInput()
	{
		if ( (Math.Abs( Input.AnalogLook.pitch ) + Math.Abs( Input.AnalogLook.yaw )) > 0.0f )
		{
			if ( !orbitEnabled )
			{
				orbitAngles = (orbitYawRot * orbitPitchRot).Angles();
				orbitAngles = orbitAngles.Normal;

				orbitYawRot = Rotation.From( 0.0f, orbitAngles.yaw, 0.0f );
				orbitPitchRot = Rotation.From( orbitAngles.pitch, 0.0f, 0.0f );
			}

			orbitEnabled = true;
			timeSinceOrbit = 0.0f;

			var rotationang = Rotation.Angles();

			orbitAngles.yaw += Input.AnalogLook.yaw;
			orbitAngles.pitch += Input.AnalogLook.pitch;
			orbitAngles = orbitAngles.Normal;
			orbitAngles.pitch = orbitAngles.pitch.Clamp( MinOrbitPitch, MaxOrbitPitch );

			rotationang.yaw += Input.AnalogLook.yaw;
			rotationang.pitch += Input.AnalogLook.pitch;
			rotationang = rotationang.Normal;
			rotationang.pitch = rotationang.pitch.Clamp( MinOrbitPitch, MaxOrbitPitch );


			Rotation = Rotation.From( rotationang );
		}
	}

	public void FrameSimulate()
	{
		Camera.WorldRotation = Rotation;
		Camera.WorldPosition = Position;
		Camera.FieldOfView = FieldOfView;
	}

	private void ApplyShake( float speed )
	{
		if ( speed < ShakeSpeedThreshold )
			return;

		var pos = Time.Now * ShakeSpeed;
		var length = (speed - ShakeSpeedThreshold) / (ShakeMaxSpeed - ShakeSpeedThreshold);
		length = length.Clamp( 0, ShakeMaxLength );

		float x = (0.5f - Noise.Simplex( pos )) * 2.0f * length;
		float y = (0.5f - Noise.Perlin( pos, 5.0f )) * 2.0f * length;

		Position += Rotation.Right * x + Rotation.Up * y;
		Rotation *= Rotation.FromAxis( Vector3.Up, x );
		Rotation *= Rotation.FromAxis( Vector3.Right, y );
	}
}
