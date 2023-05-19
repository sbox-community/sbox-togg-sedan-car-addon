using Sandbox;
using System;
using Sandbox.Utility;

namespace sbox.Community
{
	public class ToggCamera : IComponent
	{
		public Entity Car;

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

		public bool Enabled { get; set; } = true;

		public bool IsClientOnly => false;

		public bool IsServerOnly => true;

		public string Name { get; set; } = "ToggCamera";

		public bool orbitEnabled;
		public TimeSince timeSinceOrbit;
		public Angles orbitAngles;
		public Rotation orbitYawRot;
		public Rotation orbitPitchRot;
		public float currentFov;
		public float carPitch;
		public Vector3 carPosition;
		private Vector3 lastCarPosition = new();

		private Vector3 Position = new();
		private Rotation Rotation = new();
		private float FieldOfView = 0f;

		public void Activate()
		{
			orbitEnabled = false;
			timeSinceOrbit = 0.0f;
			orbitAngles = Angles.Zero;
			orbitYawRot = Rotation.Identity;
			orbitPitchRot = Rotation.Identity;
			currentFov = MinFov;
			carPitch = 0;

			orbitYawRot = Rotation.FromYaw( Car.Rotation.Yaw() );
			orbitPitchRot = Rotation.Identity;
			orbitAngles = (orbitYawRot * orbitPitchRot).Angles();
		}

		public void Update()
		{
			var car = Car as ToggSedan;
			if ( !car.IsValid() ) return;
			var body = car.PhysicsBody;
			if ( !body.IsValid() ) return;

			var speed = car.MovementSpeed;
			var speedAbs = Math.Abs( speed );
			if ( orbitEnabled && timeSinceOrbit > OrbitCooldown )
				orbitEnabled = false;

			var carRot = car.Rotation;
			carPitch = carPitch.LerpTo( car.Grounded ? carRot.Pitch().Clamp( MinCarPitch, MaxCarPitch ) * (speed < 0.0f ? -1.0f : 1.0f) : 0.0f, Time.Delta * CarPitchSmoothingSpeed );
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
			DoThirdPerson( car, body );

			if ( car.experimental_camera )
			{
				Position = Vector3.Lerp( Position, carPosition, ToggSedan.extra_lerp - 0.1f ); // experimental camera
				Position += ((lastCarPosition - car.Position) / 10f).ClampLength( 2f ); // experimental camera
			}
			else
				Position = carPosition;

			lastCarPosition = car.Position;

			currentFov = MaxFovSpeed > 0.0f ? currentFov.LerpTo( MinFov.LerpTo( MaxFov, speedAbs / MaxFovSpeed ), Time.Delta * FovSmoothingSpeed ) : MaxFov;
			FieldOfView = currentFov;

			ApplyShake( speedAbs );
			FrameSimulate();
		}

		private void DoThirdPerson( ToggSedan car, PhysicsBody body )
		{
			if ( car.experimental_camera )
				Rotation = Rotation.From( Angles.Lerp( car.Rotation.Angles(), orbitAngles, ToggSedan.extra_lerp ) ); // experimental camera ( TODO: in order to fix to rotation; car.Rotation->Rotation to rotation fix )
			else
				Rotation = orbitYawRot * orbitPitchRot;

			var carPos = car.Position + car.Rotation * (body.LocalMassCenter * car.Scale);
			var startPos = carPos;
			var targetPos = startPos + Rotation.Backward * (OrbitDistance * car.Scale * 2) + (Vector3.Up * (OrbitHeight * car.Scale));

			var tr = Trace.Ray( startPos, targetPos )
				.Ignore( car )
				.Radius( Math.Clamp( CollisionRadius * car.Scale, 2.0f, 10.0f ) )
				.WorldOnly()
				.Run();

			carPosition = tr.EndPosition;

			//Viewer = null;
		}

		public void BuildInput()
		{
			var pawn = Game.LocalPawn;
			if ( pawn == null ) return;

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
			//pawn.ViewAngles = orbitEnabled ? orbitAngles : Entity.Rotation.Angles(); // ViewAngles not exists
			//pawn.Rotation = Rotation.From( orbitEnabled ? orbitAngles : Entity.Rotation.Angles() );
		}

		public void FrameSimulate()
		{
			Camera.Rotation = Rotation;
			Camera.Position = Position;
			Camera.FieldOfView = FieldOfView;
			Camera.FirstPersonViewer = null;
			//Camera.FirstPersonViewer = this;
			//Camera.ZNear = 1f;
			//Camera.ZFar = 5000.0f;
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
}
