using Sandbox;
using Sandbox.Internal;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace sbox.Community
{
	[Spawnable]
	[Library( "ent_car_togg_sedan", Title = "Togg Sedan" )]
	public partial class ToggSedan : Prop, IUse
	{
		public static float car_accelspeed { get; set; } = 500.0f;
		public static float extra_lerp { get; set; } = 0.5f; //for experimental camera

		/*public static bool debug_car { get; set; } = false;*/

		private ToggWheel frontLeft;
		private ToggWheel frontRight;
		private ToggWheel backLeft;
		private ToggWheel backRight;
		private float frontLeftDistance;
		private float frontRightDistance;
		private float backLeftDistance;
		private float backRightDistance;

		private bool frontWheelsOnGround;
		private bool backWheelsOnGround;
		private float accelerateDirection;
		private float airRoll;
		private float airTilt;
		private float grip;
		private TimeSince timeSinceDriverLeft;

		[Net] private float WheelSpeed { get; set; }
		[Net] private float TurnDirection { get; set; }
		[Net] private float AccelerationTilt { get; set; }
		[Net] private float TurnLean { get; set; }
		[Net] public float MovementSpeed { get; private set; }
		[Net] public bool Grounded { get; private set; }

		//features

		public static CarDashboard dashboard; //clside

		private SpotLightEntity leftFrontLight;
		private SpotLightEntity rightFrontLight;
		private SpotLightEntity leftBackLight;
		private SpotLightEntity rightBackLight;

		private SpotLightEntity leftFrontHeadlight;
		private SpotLightEntity rightFrontHeadlight;
		private SpotLightEntity leftBackHeadlightight;
		private SpotLightEntity rightBackHeadlight;

		private Particles leftFrontLightParticle;
		private Particles rightFrontLightParticle;
		private Particles leftBackLightParticle;
		private Particles rightBackLightParticle;

		private Particles leftFrontHeadlightParticle;
		private Particles rightFrontHeadlightParticle;
		private Particles leftBackHeadlightParticle;
		private Particles rightBackHeadlightParticle;

		[Net] public (bool, bool, bool) lights { get; set; } = new( false, false, false ); //short lights, headlights, stop lights
		[Net] public bool experimental_camera { get; set; } = false;
		private struct InputState
		{
			public float throttle;
			public float turning;
			public float breaking;
			public float tilt;
			public float roll;

			public void Reset()
			{
				throttle = 0;
				turning = 0;
				breaking = 0;
				tilt = 0;
				roll = 0;
			}
		}

		private InputState currentInput;

		public ToggSedan()
		{
			frontLeft = new ToggWheel( this );
			frontRight = new ToggWheel( this );
			backLeft = new ToggWheel( this );
			backRight = new ToggWheel( this );
		}

		[Net] public AnimatedEntity Driver { get; private set; }

		private ModelEntity chassis_axle_rear;
		private ModelEntity chassis_axle_front;
		private ModelEntity wheel0;
		private ModelEntity wheel1;
		private ModelEntity wheel2;
		private ModelEntity wheel3;

		public override void Spawn()
		{
			base.Spawn();

			Predictable = true;

			var modelName = "models/togg_sedan_vehicle.vmdl";

			Components.Create<ToggCamera>();

			SetModel( modelName );
			SetupPhysicsFromModel( PhysicsMotionType.Dynamic, false );

			EnableSelfCollisions = false;

			spawnFeatures();
		}

		public override void ClientSpawn()
		{
			base.ClientSpawn();
			{
				chassis_axle_front = new ModelEntity();
				chassis_axle_front.SetModel( "entities/modular_vehicle/chassis_axle_front.vmdl" );
				chassis_axle_front.Transform = Transform;
				chassis_axle_front.Parent = this;
				chassis_axle_front.LocalPosition = new Vector3( 1.76f, 0, 0.39f ) * 40.0f;

				{
					wheel0 = new ModelEntity();
					wheel0.SetModel( "models/togg_sedan_vehicle_wheel.vmdl" );
					wheel0.SetParent( chassis_axle_front, "Wheel_Steer_R", new Transform( Vector3.Backward * (0.1f * 40), Rotation.From( 0, 180, 0 ) ) );
				}

				{
					wheel1 = new ModelEntity();
					wheel1.SetModel( "models/togg_sedan_vehicle_wheel.vmdl" );
					wheel1.SetParent( chassis_axle_front, "Wheel_Steer_L", new Transform( Vector3.Forward * (0.1f * 55), Rotation.From( 0, 0, 0 ) ) );
				}
			}

			{
				chassis_axle_rear = new ModelEntity();
				chassis_axle_rear.SetModel( "entities/modular_vehicle/chassis_axle_rear.vmdl" );
				chassis_axle_rear.Transform = Transform;
				chassis_axle_rear.Parent = this;
				chassis_axle_rear.LocalPosition = new Vector3( -1.35f, 0, 0.39f ) * 40.0f;

				{
					wheel2 = new ModelEntity();
					wheel2.SetModel( "models/togg_sedan_vehicle_wheel.vmdl" );
					wheel2.SetParent( chassis_axle_rear, "Axle_Rear_Center", new Transform( Vector3.Left * (0.8f * 42), Rotation.From( 0, 90, 0 ) ) );
				}

				{
					wheel3 = new ModelEntity();
					wheel3.SetModel( "models/togg_sedan_vehicle_wheel.vmdl" );
					wheel3.SetParent( chassis_axle_rear, "Axle_Rear_Center", new Transform( Vector3.Right * (0.76f * 43), Rotation.From( 0, -90, 0 ) ) );
				}
			}
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			
			if ( IsServer && Driver is AnimatedEntity player )
			{
				RemoveDriver( player );
			}

			if ( IsServer )
			{
				if ( leftFrontLightParticle != null )
					leftFrontLightParticle.Destroy();
				if ( rightFrontLightParticle != null )
					rightFrontLightParticle.Destroy();
				if ( leftBackLightParticle != null )
					leftBackLightParticle.Destroy();
				if ( rightBackLightParticle != null )
					rightBackLightParticle.Destroy();

				if ( leftFrontHeadlightParticle != null )
					leftFrontHeadlightParticle.Destroy();
				if ( rightFrontHeadlightParticle != null )
					rightFrontHeadlightParticle.Destroy();
			}
		}

		public void ResetInput()
		{
			currentInput.Reset();
		}

		[Event.Tick.Server]
		protected void Tick()
		{
			if ( Driver is AnimatedEntity player && player.LifeState != LifeState.Alive )
			{
				RemoveDriver( player );
			}
		}

		public override void Simulate( Client client )
		{
			SimulateDriver( client );
		}

		void SimulateDriver( Client client )
		{
			if ( !Driver.IsValid() ) return;

			if ( IsServer )
			{
				if ( Input.Pressed( InputButton.Use ) )
				{
					RemoveDriver( Driver as AnimatedEntity );

					return;
				}
				else
				{
					currentInput.Reset();
					currentInput.throttle = (Input.Down( InputButton.Forward ) ? 1 : 0) + (Input.Down( InputButton.Back ) ? -1 : 0);
					currentInput.turning = (Input.Down( InputButton.Left ) ? 1 : 0) + (Input.Down( InputButton.Right ) ? -1 : 0);
					currentInput.breaking = (Input.Down( InputButton.Jump ) ? 1 : 0);
					currentInput.tilt = (Input.Down( InputButton.Run ) ? 1 : 0) + (Input.Down( InputButton.Duck ) ? -1 : 0);
					currentInput.roll = (Input.Down( InputButton.Left ) ? 1 : 0) + (Input.Down( InputButton.Right ) ? -1 : 0);
				}
			}

			Driver.SetAnimParameter( "b_grounded", true );
			Driver.SetAnimParameter( "b_noclip", false );
			Driver.SetAnimParameter( "sit", 1 );

			//TODO: validate
			var viewRotation = Driver.Rotation;
			var aimRotation = viewRotation.Clamp( Driver.Rotation, 90f );

			var aimPos = Driver.EyePosition + aimRotation.Forward * 200;
			var localPos = new Transform( Driver.EyePosition, Driver.Rotation ).PointToLocal( aimPos );

			Driver.SetAnimParameter( "aim_eyes", localPos );
			Driver.SetAnimParameter( "aim_head", localPos );
			Driver.SetAnimParameter( "aim_body", localPos );

			// can't cast Derived Player Class to Player Class (BaseClass), because of targeting restrictions?
			/*if ( Driver.ActiveChild is BaseCarriable carry )
			{
				//carry.SimulateAnimator( null );
			}
			else
			{
				Driver.SetAnimParameter( "holdtype", 0 );
				Driver.SetAnimParameter( "aim_body_weight", 0.5f );
			}*/
		}

		/*public override void FrameSimulate( Client client )
		{
			base.FrameSimulate( client );

			Driver?.FrameSimulate( client );
		}*/

		[Event.Physics.PreStep]
		public void OnPrePhysicsStep()
		{
			if ( !IsServer )
				return;

			var selfBody = PhysicsBody;
			if ( !selfBody.IsValid() )
				return;

			var body = selfBody.SelfOrParent;
			if ( !body.IsValid() )
				return;

			var dt = Time.Delta;

			body.DragEnabled = false;

			var rotation = selfBody.Rotation;

			accelerateDirection = currentInput.throttle.Clamp( -1, 1 ) * (1.0f - currentInput.breaking);
			TurnDirection = TurnDirection.LerpTo( currentInput.turning.Clamp( -1, 1 ), 1.0f - MathF.Pow( 0.001f, dt ) );

			airRoll = airRoll.LerpTo( currentInput.roll.Clamp( -1, 1 ), 1.0f - MathF.Pow( 0.0001f, dt ) );
			airTilt = airTilt.LerpTo( currentInput.tilt.Clamp( -1, 1 ), 1.0f - MathF.Pow( 0.0001f, dt ) );

			float targetTilt = 0;
			float targetLean = 0;

			var localVelocity = rotation.Inverse * body.Velocity;

			if ( backWheelsOnGround || frontWheelsOnGround )
			{
				var forwardSpeed = MathF.Abs( localVelocity.x );
				var speedFraction = MathF.Min( forwardSpeed / 500.0f, 1 );

				targetTilt = accelerateDirection.Clamp( -1.0f, 1.0f );
				targetLean = speedFraction * TurnDirection;
			}

			AccelerationTilt = AccelerationTilt.LerpTo( targetTilt, 1.0f - MathF.Pow( 0.01f, dt ) );
			TurnLean = TurnLean.LerpTo( targetLean, 1.0f - MathF.Pow( 0.01f, dt ) );

			if ( backWheelsOnGround )
			{
				var forwardSpeed = MathF.Abs( localVelocity.x );
				var speedFactor = 1.0f - (forwardSpeed / 5000.0f).Clamp( 0.0f, 1.0f );
				var acceleration = speedFactor * (accelerateDirection < 0.0f ? car_accelspeed * 0.5f : car_accelspeed) * accelerateDirection * dt; //0.5f->1f for breaking (reverted)
				var impulse = rotation * new Vector3( acceleration, 0, 0 );
				body.Velocity += impulse;
			}

			RaycastWheels( rotation, true, out frontWheelsOnGround, out backWheelsOnGround, dt );
			var onGround = frontWheelsOnGround || backWheelsOnGround;
			var fullyGrounded = (frontWheelsOnGround && backWheelsOnGround);
			Grounded = onGround;

			if ( fullyGrounded )
			{
				body.Velocity += Map.Physics.Gravity * dt;
			}

			body.GravityScale = fullyGrounded ? 0 : 1;

			bool canAirControl = false;

			var v = rotation * localVelocity.WithZ( 0 );
			var vDelta = MathF.Pow( (v.Length / 1000.0f).Clamp( 0, 1 ), 5.0f ).Clamp( 0, 1 );
			if ( vDelta < 0.01f ) vDelta = 0;

			/*if ( debug_car )
			{
				DebugOverlay.Line( body.MassCenter, body.MassCenter + rotation.Forward.Normal * 100, Color.White, 0, false );
				DebugOverlay.Line( body.MassCenter, body.MassCenter + v.Normal * 100, Color.Green, 0, false );
			}*/

			var angle = (rotation.Forward.Normal * MathF.Sign( localVelocity.x )).Normal.Dot( v.Normal ).Clamp( 0.0f, 1.0f );
			angle = angle.LerpTo( 1.0f, 1.0f - vDelta );
			grip = grip.LerpTo( angle, 1.0f - MathF.Pow( 0.001f, dt ) );

			/*if ( debug_car )
			{
				DebugOverlay.ScreenText( $"{grip}", new Vector2( 200, 200 ) );
			}*/

			var angularDamping = 0.0f;
			angularDamping = angularDamping.LerpTo( 5.0f, grip );

			body.LinearDamping = 0.0f;
			body.AngularDamping = fullyGrounded ? angularDamping : 0.5f;

			if ( onGround )
			{
				localVelocity = rotation.Inverse * body.Velocity;
				WheelSpeed = localVelocity.x;
				var turnAmount = frontWheelsOnGround ? (MathF.Sign( localVelocity.x ) * 25.0f * CalculateTurnFactor( TurnDirection, MathF.Abs( localVelocity.x ) ) * dt) : 0.0f;
				body.AngularVelocity += rotation * new Vector3( 0, 0, turnAmount );

				airRoll = 0;
				airTilt = 0;

				var forwardGrip = 0.1f;
				forwardGrip = forwardGrip.LerpTo( 0.9f, currentInput.breaking );
				body.Velocity = VelocityDamping( body.Velocity, rotation, new Vector3( forwardGrip, grip, 0 ), dt );
			}
			else
			{
				var s = selfBody.Position + (rotation * selfBody.LocalMassCenter);
				var tr = Trace.Ray( s, s + rotation.Down * 50 )
					.Ignore( this )
					.Run();

				/*if ( debug_car )
					DebugOverlay.Line( tr.StartPosition, tr.EndPosition, tr.Hit ? Color.Red : Color.Green );*/

				canAirControl = !tr.Hit;
			}

			if ( canAirControl && (airRoll != 0 || airTilt != 0) )
			{
				var offset = 50 * Scale;
				var s = selfBody.Position + (rotation * selfBody.LocalMassCenter) + (rotation.Right * airRoll * offset) + (rotation.Down * (10 * Scale));
				var tr = Trace.Ray( s, s + rotation.Up * (25 * Scale) )
					.Ignore( this )
					.Run();

				/*if ( debug_car )
					DebugOverlay.Line( tr.StartPosition, tr.EndPosition );*/

				bool dampen = false;

				if ( currentInput.roll.Clamp( -1, 1 ) != 0 )
				{
					var force = tr.Hit ? 400.0f : 100.0f;
					var roll = tr.Hit ? currentInput.roll.Clamp( -1, 1 ) : airRoll;
					body.ApplyForceAt( selfBody.MassCenter + rotation.Left * (offset * roll), (rotation.Down * roll) * (roll * (body.Mass * force)) );

					/*if ( debug_car )
						DebugOverlay.Sphere( selfBody.MassCenter + rotation.Left * (offset * roll), 8, Color.Red );*/

					dampen = true;
				}

				if ( !tr.Hit && currentInput.tilt.Clamp( -1, 1 ) != 0 )
				{
					var force = 200.0f;
					body.ApplyForceAt( selfBody.MassCenter + rotation.Forward * (offset * airTilt), (rotation.Down * airTilt) * (airTilt * (body.Mass * force)) );

					/*if ( debug_car )
						DebugOverlay.Sphere( selfBody.MassCenter + rotation.Forward * (offset * airTilt), 8, Color.Green );*/

					dampen = true;
				}

				if ( dampen )
					body.AngularVelocity = VelocityDamping( body.AngularVelocity, rotation, 0.95f, dt );
			}

			localVelocity = rotation.Inverse * body.Velocity;
			MovementSpeed = localVelocity.x;
		}

		private static float CalculateTurnFactor( float direction, float speed )
		{
			var turnFactor = MathF.Min( speed / 500.0f, 1 );
			var yawSpeedFactor = 1.0f - (speed / 1000.0f).Clamp( 0, 0.6f );

			return direction * turnFactor * yawSpeedFactor;
		}

		private static Vector3 VelocityDamping( Vector3 velocity, Rotation rotation, Vector3 damping, float dt )
		{
			var localVelocity = rotation.Inverse * velocity;
			var dampingPow = new Vector3( MathF.Pow( 1.0f - damping.x, dt ), MathF.Pow( 1.0f - damping.y, dt ), MathF.Pow( 1.0f - damping.z, dt ) );
			return rotation * (localVelocity * dampingPow);
		}

		private void RaycastWheels( Rotation rotation, bool doPhysics, out bool frontWheels, out bool backWheels, float dt )
		{
			float forward = 42;
			float right = 32;

			var frontLeftPos = rotation.Forward * forward + rotation.Right * right + rotation.Up * 20;
			var frontRightPos = rotation.Forward * forward - rotation.Right * right + rotation.Up * 20;
			var backLeftPos = -rotation.Forward * forward + rotation.Right * right + rotation.Up * 20;
			var backRightPos = -rotation.Forward * forward - rotation.Right * right + rotation.Up * 20;

			var tiltAmount = AccelerationTilt * 2.5f;
			var leanAmount = TurnLean * 2.5f;

			float length = 20.0f;

			frontWheels =
				frontLeft.Raycast( length + tiltAmount - leanAmount, doPhysics, frontLeftPos * Scale, ref frontLeftDistance, dt ) |
				frontRight.Raycast( length + tiltAmount + leanAmount, doPhysics, frontRightPos * Scale, ref frontRightDistance, dt );

			backWheels =
				backLeft.Raycast( length - tiltAmount - leanAmount, doPhysics, backLeftPos * Scale, ref backLeftDistance, dt ) |
				backRight.Raycast( length - tiltAmount + leanAmount, doPhysics, backRightPos * Scale, ref backRightDistance, dt );
		}

		float wheelAngle = 0.0f;
		float wheelRevolute = 0.0f;

		[Event.Frame]
		public void OnFrame()
		{
			wheelAngle = wheelAngle.LerpTo( TurnDirection * 25, 1.0f - MathF.Pow( 0.001f, Time.Delta ) );
			wheelRevolute += (WheelSpeed / (14.0f * Scale)).RadianToDegree() * Time.Delta;

			var wheelRotRight = Rotation.From( -wheelAngle, 180, -wheelRevolute );
			var wheelRotLeft = Rotation.From( wheelAngle, 0, wheelRevolute );
			var wheelRotBackRight = Rotation.From( 0, 90, -wheelRevolute );
			var wheelRotBackLeft = Rotation.From( 0, -90, wheelRevolute );

			RaycastWheels( Rotation, false, out _, out _, Time.Delta );

			float frontOffset = 20.0f - Math.Min( frontLeftDistance, frontRightDistance );
			float backOffset = 20.0f - Math.Min( backLeftDistance, backRightDistance );

			chassis_axle_front.SetBoneTransform( "Axle_front_Center", new Transform( Vector3.Up * frontOffset ), false );
			chassis_axle_rear.SetBoneTransform( "Axle_Rear_Center", new Transform( Vector3.Up * backOffset ), false );

			wheel0.LocalRotation = wheelRotRight;
			wheel1.LocalRotation = wheelRotLeft;
			wheel2.LocalRotation = wheelRotBackRight;
			wheel3.LocalRotation = wheelRotBackLeft;

			//var comp = Components.Get<ToggCamera>();
			//comp.Update();

			//EyeRotation = Rotation.From( Angles.Lerp( EyeRotation.Angles(), comp.orbitAngles, extra_lerp ) );
		}

		private void RemoveDriver( AnimatedEntity player )
		{
			if ( !IsServer )
				return;

			Driver?.SetAnimParameter( "sit", 0 );

			Driver = null;
			timeSinceDriverLeft = 0;

			ResetInput();

			if ( !player.IsValid() )
				return;

			player.Parent = null;
			player.Position += Vector3.Up * 100;

			dashboardHandler( To.Single( player ) );

			if ( player.PhysicsBody.IsValid() )
			{
				player.PhysicsBody.Enabled = true;
				player.PhysicsBody.Position = player.Position;
			}

			player.Client.Pawn = player;
		}

		public bool OnUse( Entity client )
		{
			if ( client is AnimatedEntity player && timeSinceDriverLeft > 1.0f )
			{
				player.Parent = this;
				player.LocalPosition = Vector3.Up * 10;
				player.LocalRotation = Rotation.Identity;
				player.LocalScale = 1;
				player.PhysicsBody.Enabled = false;

				Driver = player;

				Components.Get<ToggCamera>().Activated();
				player.Client.Pawn = this;

				dashboardHandler( To.Single( client ), true, NetworkIdent );
			}

			return false;
		}

		[ClientRpc]
		public static void dashboardHandler( bool enable = false, int networkIdent = 0)
		{
			if(enable)
			{
				var car =  All.Where( x => x.NetworkIdent == networkIdent );

				if ( !car.Any() )
					return;

				dashboard = Local.Hud.FindRootPanel().AddChild<CarDashboard>();
				dashboard.Car = car.FirstOrDefault() as ToggSedan;
			}
			else
			{
				dashboard.Delete();
			}

		}

		public bool IsUsable( Entity user )
		{
			return Driver == null;
		}

		protected override void OnPhysicsCollision( CollisionEventData eventData )
		{
			if ( !IsServer )
				return;

			var other = eventData.Other;

			if ( other.Entity is AnimatedEntity )
				return;

			var propData = GetModelPropData();

			var minImpactSpeed = propData.MinImpactDamageSpeed;
			if ( minImpactSpeed <= 0.0f ) minImpactSpeed = 500;

			var impactDmg = propData.ImpactDamage;
			if ( impactDmg <= 0.0f ) impactDmg = 10;

			var speed = eventData.Speed;

			if ( speed > minImpactSpeed )
			{
				if ( other.Entity.IsValid() && other.Entity != this )
				{
					var damage = speed / minImpactSpeed * impactDmg * 1.2f;
					other.Entity.TakeDamage( DamageInfo.Generic( damage )
						.WithFlag( DamageFlags.PhysicsImpact )
						.WithFlag( DamageFlags.Vehicle )
						.WithAttacker( Driver != null ? Driver : this, Driver != null ? this : null )
						.WithPosition( eventData.Position )
						.WithForce( other.PreVelocity ) );

					if ( other.Entity.LifeState == LifeState.Dead && other.Entity is not Player )
					{
						PhysicsBody.Velocity = eventData.This.PreVelocity;
						PhysicsBody.AngularVelocity = eventData.This.PreAngularVelocity;
					}
				}
			}
		}

		// Spawn the features

		private void spawnFeatures()
		{
			if(IsServer)
				spawnLights();
		}

		[ConCmd.Server]
		public static void featuresHandler(int flag)
		{
			var pawn = ConsoleSystem.Caller.Pawn;

			if ( pawn as ToggSedan is var car )
			{
				switch (flag)
				{
					case (0): //enable-disable front lights
						car.enableDisableFrontLigths();
						break;
					case (1): //enable-disable headlights
						car.enableDisableHeadlights();
						break;
					case (2): //enable-disable experimental camera
						car.experimental_camera = !car.experimental_camera;
						break;
					default: break;

				}
			}
		}
		 
		private void spawnLights()
		{
			leftFrontLight = new();
			leftFrontLight.Enabled = false;
			leftFrontLight.DynamicShadows = true;
			leftFrontLight.Brightness = 10f;
			leftFrontLight.Parent = this;
			leftFrontLight.LocalPosition = new Vector3( 2.45f, 0.72f, 0.82f ) * 40.0f;
			leftFrontLight.Rotation = Rotation.From( new Angles( 0, Rotation.Angles().yaw, 0 ) );

			leftFrontLightParticle = Particles.Create( "particles/togg/head_light.vpcf" );
			leftFrontLightParticle.EnableDrawing = false;
			leftFrontLightParticle.SetEntity( 0, leftFrontLight );

			rightFrontLight = new();
			rightFrontLight.Enabled = false;
			rightFrontLight.DynamicShadows = true;
			rightFrontLight.Brightness = 10f;
			rightFrontLight.Parent = this;
			rightFrontLight.LocalPosition = new Vector3( 2.45f, -0.68f, 0.82f ) * 40.0f;
			rightFrontLight.Rotation = Rotation.From( new Angles( 0, Rotation.Angles().yaw, 0 ) );

			rightBackLightParticle = Particles.Create( "particles/togg/head_light.vpcf" );
			rightBackLightParticle.EnableDrawing = false;
			rightBackLightParticle.SetEntity( 0, rightFrontLight );

			leftFrontHeadlight = new();
			leftFrontHeadlight.Enabled = false;
			leftFrontHeadlight.DynamicShadows = true;
			leftFrontHeadlight.Brightness = 30f;
			leftFrontHeadlight.Parent = this;
			leftFrontHeadlight.LocalPosition = new Vector3( 2.29f, 0.86f, 0.85f ) * 40.0f;
			leftFrontHeadlight.Rotation = Rotation.From( new Angles( 0, Rotation.Angles().yaw, 0 ) );

			leftFrontHeadlightParticle = Particles.Create( "particles/togg/head_light.vpcf" );
			leftFrontHeadlightParticle.EnableDrawing = false;
			leftFrontHeadlightParticle.SetEntity( 0, leftFrontHeadlight );

			rightFrontHeadlight = new();
			rightFrontHeadlight.Enabled = false;
			rightFrontHeadlight.DynamicShadows = true;
			rightFrontHeadlight.Brightness = 30f;
			rightFrontHeadlight.Parent = this;
			rightFrontHeadlight.LocalPosition = new Vector3( 2.29f, -0.82f, 0.85f ) * 40.0f;
			rightFrontHeadlight.Rotation = Rotation.From( new Angles( 0, Rotation.Angles().yaw, 0 ) );

			rightFrontHeadlightParticle = Particles.Create( "particles/togg/head_light.vpcf" );
			rightFrontHeadlightParticle.EnableDrawing = false;
			rightFrontHeadlightParticle.SetEntity( 0, rightFrontHeadlight );

		}
		private void enableDisableFrontLigths()
		{
			var isenabled = lights.Item1;

			leftFrontLight.Enabled = !isenabled;
			rightFrontLight.Enabled = !isenabled;

			leftFrontLightParticle.EnableDrawing = !isenabled;
			rightBackLightParticle.EnableDrawing = !isenabled;

			lights = (!isenabled, lights.Item2, lights.Item3);
		}

		private void enableDisableHeadlights()
		{
			var isenabled = lights.Item2;

			leftFrontHeadlight.Enabled = !isenabled;
			rightFrontHeadlight.Enabled = !isenabled;

			leftFrontHeadlightParticle.EnableDrawing = !isenabled;
			rightFrontHeadlightParticle.EnableDrawing = !isenabled;

			lights = (lights.Item1, !isenabled, lights.Item3);
		}

		[ConCmd.Admin( "togg_spawn" )]
		public static void spawnTogg()
		{
			var caller = ConsoleSystem.Caller;

			var Tr = Trace.Ray( caller.Pawn.EyePosition, caller.Pawn.EyePosition + caller.Pawn.EyeRotation.Forward * 200 )
			.UseHitboxes()
			.Ignore( caller.Pawn )
			.Size( 1 )
			.Run();

			var togg = CreateByName<ToggSedan>( "ToggSedan" );
			togg.Position = Tr.EndPosition + new Vector3( 0f, 0f, 20f );
			togg.Rotation = Rotation.From( new Angles( 0, caller.Pawn.EyeRotation.Angles().yaw + 90, 0 ) );
		}
	}
}
