using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ToggSedan : Component, Component.IDamageable, Component.IPressable, PlayerController.IEvents, IScenePhysicsEvents
{
	public static Logger Logger = new( "Togg Sedan" );
	private ToggCamera CarCamera;
	public static CarDashboard Dashboard;
	[Property] public static float AccelerateSpeed { get; set; } = 500.0f;

	//[Property] public static bool debug_car { get; set; } = false;

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
	public Rigidbody SelfBody;
	private bool firstTimeToDriver = true;
	private PropertyDescription activeWeaponPropertyDesp;
	private MethodDescription switchWeaponMethodDesp;
	private List<GameObject> trash = new();

	[Sync] private float Health { get; set; } = 200f;
	[Sync] private float WheelSpeed { get; set; }
	[Sync] private float TurnDirection { get; set; }
	[Sync] private float AccelerationTilt { get; set; }
	[Sync] private float TurnLean { get; set; }
	[Sync] public float MovementSpeed { get; private set; }
	[Sync] public bool Grounded { get; private set; }

	//features
	[Property] private GameObject leftFrontLight;
	[Property] private GameObject rightFrontLight;
	[Property] private GameObject leftFrontHeadlight;
	[Property] private GameObject rightFrontHeadlight;
	//[Property] private GameObject leftBackLight;
	//[Property] private GameObject rightBackLight;

	[Property] private GameObject driverExitPoint { get; set; }
	[Property] private SoundEvent driverExitSound { get; set; }
	private Component driverActiveWeapon { get; set; }
	[Property] private SoundEvent horn { get; set; }
	[Property] private SoundEvent vehicleHitSound { get; set; }
	[Property] private SoundEvent vehicleExplosionSound { get; set; }

	[Sync] public bool shortLights { get; set; } = false;
	[Sync] public bool headLights { get; set; } = false;

	[Sync] public bool experimental_camera { get; set; } = false;
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

	[Sync] public GameObject Driver { get; set; }
	public Connection DriverNW
	{
		get
		{
			if ( Driver != null && Driver.IsValid() )
				return Driver.Network.Owner;
			return null;
		}
	}
	private PlayerController DriverPlayerController;

	public bool AmIDriver() => !IsProxy && DriverNW != null && DriverNW == Connection.Local;
	public bool IsExploded() => Health == -1;

	private GameObject chassis_axle_rear;
	private GameObject chassis_axle_front;
	private SkinnedModelRenderer wheel0;
	private SkinnedModelRenderer wheel1;
	private SkinnedModelRenderer wheel2;
	private SkinnedModelRenderer wheel3;

	protected override void OnStart()
	{
		base.OnStart();

		chassis_axle_front = GameObject.Children.First( x => x.Name == "chassis_axle_front" );
		chassis_axle_rear = GameObject.Children.First( x => x.Name == "chassis_axle_rear" );

		wheel0 = chassis_axle_front.Children.First( x => x.Name == "wheel0" ).GetComponent<SkinnedModelRenderer>();
		wheel1 = chassis_axle_front.Children.First( x => x.Name == "wheel1" ).GetComponent<SkinnedModelRenderer>();
		wheel2 = chassis_axle_rear.Children.First( x => x.Name == "wheel2" ).GetComponent<SkinnedModelRenderer>();
		wheel3 = chassis_axle_rear.Children.First( x => x.Name == "wheel3" ).GetComponent<SkinnedModelRenderer>();

		CarCamera = new ToggCamera( this );
		Dashboard = GameObject.GetComponentInChildren<CarDashboard>();

		SelfBody = GetComponent<Rigidbody>();
	}
	protected override void OnDestroy()
	{
		base.OnDestroy();

		RemoveDriver();

		if ( wheel0 != null && wheel0.GameObject != null && wheel0.GameObject.IsValid() )
			wheel0.GameObject.Destroy();

		if ( wheel1 != null && wheel1.GameObject != null && wheel1.GameObject.IsValid() )
			wheel1.GameObject.Destroy();

		if ( wheel2 != null && wheel2.GameObject != null && wheel2.GameObject.IsValid() )
			wheel2.GameObject.Destroy();

		if ( wheel3 != null && wheel3.GameObject != null && wheel3.GameObject.IsValid() )
			wheel3.GameObject.Destroy();

		foreach ( var go in trash )
			if( go != null && go.IsValid())
				go.Destroy();
	}

	public void ResetInput()
	{
		currentInput.Reset();
	}


	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( Driver != null && Driver.IsDestroyed )
		{
			RemoveDriver();
		}

		if( AmIDriver() && !IsExploded() )
		{
			SimulateDriver();
			CarCamera.BuildInput();
		}
	}

	void SimulateDriver()
	{
		if ( Input.Pressed( "use" ) )
		{
			if( firstTimeToDriver )
			{
				firstTimeToDriver = false;
				return;
			}
			RemoveDriver();

			return;
		}
		else
		{
			firstTimeToDriver = false;

			currentInput.Reset();
			currentInput.throttle = (Input.Down( "forward" ) ? 1 : 0) + (Input.Down( "backward" ) ? -1 : 0);
			currentInput.turning = (Input.Down( "left" ) ? 1 : 0) + (Input.Down( "right" ) ? -1 : 0);
			currentInput.breaking = (Input.Down( "jump" ) ? 1 : 0);
			currentInput.tilt = (Input.Down( "run" ) ? 1 : 0) + (Input.Down( "duck" ) ? -1 : 0);
			currentInput.roll = (Input.Down( "left" ) ? 1 : 0) + (Input.Down( "right" ) ? -1 : 0);
		}

		if( DriverPlayerController == null)
		{
			DriverPlayerController = Driver.GetComponent<PlayerController>();

			if ( DriverPlayerController == null )
				return;
		}

		var driverSceneModel = DriverPlayerController.Renderer.SceneModel;

		driverSceneModel.SetAnimParameter( "b_grounded", true );
		driverSceneModel.SetAnimParameter( "b_noclip", false );
		driverSceneModel.SetAnimParameter( "sit", 1 );

		//TODO: validate
		var viewRotation = WorldRotation;
		var aimRotation = viewRotation.Clamp( Driver.WorldRotation, 90f );

		var ray = DriverPlayerController.EyeTransform.ForwardRay;

		var aimPos = ray.Position + aimRotation.Forward * 200;
		var localPos = new Transform( ray.Position, Driver.WorldRotation ).PointToLocal( aimPos );

		driverSceneModel.SetAnimParameter( "aim_eyes", localPos );
		driverSceneModel.SetAnimParameter( "aim_head", localPos );
		driverSceneModel.SetAnimParameter( "aim_body", localPos );

		//TODO: validate
		driverSceneModel.SetAnimParameter( "holdtype", 0 );
		driverSceneModel.SetAnimParameter( "aim_body_weight", 0.5f );

	}

	public void PrePhysicsStep()
	{
		if ( IsExploded() )
			return;
		
		if ( !SelfBody.IsValid() )
			return;

		var body = SelfBody.PhysicsBody.SelfOrParent;
		if ( !body.IsValid() )
			return;

		var dt = Time.Delta;

		body.AngularDamping = 0f;
		body.LinearDamping = 0f;

		var rotation = SelfBody.PhysicsBody.Rotation;

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
			var acceleration = speedFactor * (accelerateDirection < 0.0f ? AccelerateSpeed * 0.5f : AccelerateSpeed) * accelerateDirection * dt; //0.5f->1f for breaking (reverted)
			var impulse = rotation * new Vector3( acceleration, 0, 0 );
			body.Velocity += impulse;
		}

		RaycastWheels( rotation, true, out frontWheelsOnGround, out backWheelsOnGround, dt );
		var onGround = frontWheelsOnGround || backWheelsOnGround;
		var fullyGrounded = (frontWheelsOnGround && backWheelsOnGround);
		Grounded = onGround;

		if ( fullyGrounded )
		{
			body.Velocity += Game.ActiveScene.PhysicsWorld.Gravity * dt;
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
			var s = SelfBody.PhysicsBody.Position + (rotation * SelfBody.PhysicsBody.LocalMassCenter);
			var tr = Game.ActiveScene.Trace.Ray( s, s + rotation.Down * 50 )
				.IgnoreGameObject( GameObject )
				.Run();

			/*if ( debug_car )
				DebugOverlay.Line( tr.StartPosition, tr.EndPosition, tr.Hit ? Color.Red : Color.Green );*/

			canAirControl = !tr.Hit;
		}

		if ( canAirControl && (airRoll != 0 || airTilt != 0) )
		{
			var offset = 50 * WorldScale;
			var s = SelfBody.PhysicsBody.Position + (rotation * SelfBody.PhysicsBody.LocalMassCenter) + (rotation.Right * airRoll * offset) + (rotation.Down * (10 * WorldScale));
			var tr = Game.ActiveScene.Trace.Ray( s, s + rotation.Up * (25 * WorldScale) )
				.IgnoreGameObject( GameObject )
				.Run();

			/*if ( debug_car )
				DebugOverlay.Line( tr.StartPosition, tr.EndPosition );*/

			bool dampen = false;

			if ( currentInput.roll.Clamp( -1, 1 ) != 0 )
			{
				var force = tr.Hit ? 400.0f : 100.0f;
				var roll = tr.Hit ? currentInput.roll.Clamp( -1, 1 ) : airRoll;
				body.ApplyForceAt( SelfBody.MassCenter + rotation.Left * (offset * roll), (rotation.Down * roll) * (roll * (body.Mass * force)) );

				/*if ( debug_car )
					DebugOverlay.Sphere( SelfBody.MassCenter + rotation.Left * (offset * roll), 8, Color.Red );*/

				dampen = true;
			}

			if ( !tr.Hit && currentInput.tilt.Clamp( -1, 1 ) != 0 )
			{
				var force = 200.0f;
				body.ApplyForceAt( SelfBody.MassCenter + rotation.Forward * (offset * airTilt), (rotation.Down * airTilt) * (airTilt * (body.Mass * force)) );

				/*if ( debug_car )
					DebugOverlay.Sphere( SelfBody.MassCenter + rotation.Forward * (offset * airTilt), 8, Color.Green );*/

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
		float forward = 42; // TODO: property?
		float right = 32; // TODO: property?

		float height = 15.5f; // TODO: property?

		var frontLeftPos = rotation.Forward * forward + rotation.Right * right + rotation.Up * height;//TODO property?
		var frontRightPos = rotation.Forward * forward - rotation.Right * right + rotation.Up * height;
		var backLeftPos = -rotation.Forward * forward + rotation.Right * right + rotation.Up * height;
		var backRightPos = -rotation.Forward * forward - rotation.Right * right + rotation.Up * height;



		var tiltAmount = AccelerationTilt * 1.5f;  // TODO: property? for back and down movement
		var leanAmount = TurnLean * 1.35f; // TODO: property?

		float length = 20.0f; // TODO: property?

		frontWheels =
			frontLeft.Raycast( length + tiltAmount - leanAmount, doPhysics, frontLeftPos * WorldScale, ref frontLeftDistance, dt ) |
			frontRight.Raycast( length + tiltAmount + leanAmount, doPhysics, frontRightPos * WorldScale, ref frontRightDistance, dt );

		backWheels =
			backLeft.Raycast( length - tiltAmount - leanAmount, doPhysics, backLeftPos * WorldScale, ref backLeftDistance, dt ) |
			backRight.Raycast( length - tiltAmount + leanAmount, doPhysics, backRightPos * WorldScale, ref backRightDistance, dt );
	}

	float wheelAngle = 0.0f;
	float wheelRevolute = 0.0f;


	protected override void OnUpdate()
	{
		if ( IsExploded() )
			return;

		//TODO: distance check

		wheelAngle = wheelAngle.LerpTo( TurnDirection * 25, 1.0f - MathF.Pow( 0.001f, Time.Delta ) );
		wheelRevolute += (WheelSpeed / (14.0f * WorldScale.x)).RadianToDegree() * Time.Delta;

		var wheelRotRight = Rotation.From( 0, wheelAngle - 90, -wheelRevolute );  //TODO property?
		var wheelRotLeft = Rotation.From( 0, wheelAngle + 90, wheelRevolute );
		var wheelRotBackRight = Rotation.From( 0, 90, wheelRevolute );
		var wheelRotBackLeft = Rotation.From( 0, -90, -wheelRevolute );


		RaycastWheels( WorldRotation, false, out _, out _, Time.Delta );

		float frontOffset = 15.5f - Math.Min( frontLeftDistance, frontRightDistance );  //TODO property? 
		float backOffset = 15.5f - Math.Min( backLeftDistance, backRightDistance );  //TODO property?

		//chassis_axle_front.SetBoneTransform( chassis_axle_front.Model.Bones.GetBone( "Axle_front_Center" ), new Transform( Vector3.Up * frontOffset ) );
		//chassis_axle_rear.SetBoneTransform( chassis_axle_rear.Model.Bones.GetBone( "Axle_Rear_Center" ), new Transform( Vector3.Up * backOffset ) );

		if ( wheel0 == null || !wheel0.IsValid() )
			return;

		wheel0.LocalTransform = wheel0.LocalTransform.WithPosition( wheel0.LocalTransform.Position.WithZ( frontOffset ) );
		wheel1.LocalTransform = wheel1.LocalTransform.WithPosition( wheel1.LocalTransform.Position.WithZ( frontOffset ) );
		wheel2.LocalTransform = wheel2.LocalTransform.WithPosition( wheel2.LocalTransform.Position.WithZ( backOffset ) );
		wheel3.LocalTransform = wheel3.LocalTransform.WithPosition( wheel3.LocalTransform.Position.WithZ( backOffset ) );

		wheel0.LocalRotation = wheelRotRight;
		wheel1.LocalRotation = wheelRotLeft;
		wheel2.LocalRotation = wheelRotBackRight;
		wheel3.LocalRotation = wheelRotBackLeft;

		if ( AmIDriver() )
			CarCamera.Update();
	}

	private void RemoveDriver()
	{
		if ( IsProxy )
			return;

		//DriverNW?.SetAnimParameter( "sit", 0 );

		timeSinceDriverLeft = 0;

		ResetInput();

		if ( Driver == null || !Driver.IsValid() )
			return;

		Driver.Parent = null;
		Driver.WorldPosition = driverExitPoint.WorldPosition;

		var playerInventoryComponent = Driver.Components.GetAll().FirstOrDefault( c => c.GetType().Name == "PlayerInventory" );
		if ( playerInventoryComponent != null && playerInventoryComponent.IsValid() )
			playerInventoryComponent.Enabled = true;

		if ( driverActiveWeapon != null )
		{
			driverActiveWeapon.GameObject.Enabled = true;

			if ( playerInventoryComponent != null && playerInventoryComponent.IsValid() )
			{
				activeWeaponPropertyDesp.SetValue( playerInventoryComponent, driverActiveWeapon );
				switchWeaponMethodDesp.Invoke( playerInventoryComponent, [driverActiveWeapon] );
			}

			driverActiveWeapon = null;

		}

		if ( driverExitSound != null )
			Sound.Play( driverExitSound, driverExitPoint.WorldPosition );

		var rigidBody = Driver.GetComponent<Rigidbody>( true );

		if ( rigidBody != null )
			rigidBody.Enabled = true;

		if( Dashboard != null && Dashboard.IsValid() && Dashboard.Panel != null && Dashboard.Panel.IsValid())
			Dashboard.Panel.Style.PointerEvents = PointerEvents.None;

		Driver = null;
		DriverPlayerController = null;

	}

	public bool Press( IPressable.Event e )
	{
		if ( e.Source.GameObject is GameObject player && player != null && player.IsValid() && timeSinceDriverLeft > 1.0f )
		{
			firstTimeToDriver = true;
			GameObject.Network.AssignOwnership( player.Network.Owner );
			Driver = player;

			player.Parent = GameObject;
			player.LocalPosition = Vector3.Up * 10;
			player.LocalRotation = Rotation.Identity;
			player.LocalScale = 1;
			player.GetComponent<Rigidbody>( true ).Enabled = false;



			// Active weapon handling
			var playerInventoryComponent = player.Components.GetAll().FirstOrDefault( c => c.GetType().Name == "PlayerInventory" );


			if ( playerInventoryComponent != null && playerInventoryComponent.IsValid() )
			{
				if ( activeWeaponPropertyDesp == null ) //cache property info
				{
					var playerType = Game.TypeLibrary.GetType( playerInventoryComponent.GetType() );
					activeWeaponPropertyDesp = playerType.GetProperty( "ActiveWeapon" );
					switchWeaponMethodDesp = playerType.GetMethod( "SwitchWeapon" );
				}
				if ( activeWeaponPropertyDesp != null )
				{
					driverActiveWeapon = (Component)activeWeaponPropertyDesp.GetValue( playerInventoryComponent );

					if( driverActiveWeapon != null && driverActiveWeapon.GameObject != null && driverActiveWeapon.GameObject.IsValid())
						driverActiveWeapon.GameObject.Enabled = false;

					activeWeaponPropertyDesp.SetValue( playerInventoryComponent, null );
					playerInventoryComponent.Enabled = false;
				}
			}
		}

		return true;
	}
	public bool CanPress( IPressable.Event e )
	{
		return Driver == null && !IsExploded();
	}

	[Rpc.Broadcast( Flags = NetFlags.OwnerOnly | NetFlags.Reliable )]
	public void featuresHandler( int flag )
	{
		switch ( flag )
		{
			case (0):
				enableDisableFrontLigths();
				break;
			case (1):
				enableDisableHeadlights();
				break;
			case (2):
				emitHorn();
				break;
			case (3):
				experimental_camera = !experimental_camera;
				break;
			default: break;
		}
	}

	[Rpc.Broadcast( Flags = NetFlags.HostOnly | NetFlags.UnreliableNoDelay )]
	public void featuresHandlerHost( int flag )
	{
		switch ( flag )
		{
			case (0):
				emitDamage();
				break;
			case (1):
				emitExplosion();
				break;
			default: break;
		}
	}

	private void enableDisableFrontLigths()
	{
		var isenabled = shortLights;

		leftFrontLight.Enabled = !isenabled;
		rightFrontLight.Enabled = !isenabled;

		shortLights = !isenabled;
	}

	private void enableDisableHeadlights( )
	{
		var isenabled = headLights;

		leftFrontHeadlight.Enabled = !isenabled;
		rightFrontHeadlight.Enabled = !isenabled;

		//if ( !isenabled && !lights.shortLights)
		//	enableDisableFrontLigths();

		headLights = !isenabled;
	}

	private void emitHorn()
	{
		if ( horn != null )
			Sound.Play( horn, GameObject.WorldPosition );
	}
	private void emitDamage()
	{
		if ( vehicleHitSound != null )
			Sound.Play( vehicleHitSound, GameObject.WorldPosition );
	}
	private void emitExplosion()
	{
		if ( vehicleExplosionSound != null )
			Sound.Play( vehicleExplosionSound, GameObject.WorldPosition );
	}

	public void OnCollisionHit(Collision collision)
	{
		var other = collision.Other;

		var minImpactSpeed = collision.Contact.NormalSpeed;//propData.MinImpactDamageSpeed;
		if ( minImpactSpeed <= 0.0f ) minImpactSpeed = 500;

		var impactDmg = collision.Contact.Impulse;//propData.ImpactDamage;
		if ( impactDmg <= 0.0f ) impactDmg = 10;

		var speed = collision.Contact.Speed.Length;//eventData.Speed;

		if ( speed > minImpactSpeed )
		{
			if ( other.GameObject.IsValid() && other.GameObject != GameObject && other.GameObject.GetComponent<ToggSedan>() is ToggSedan car && car != null)
			{
				var damage = speed / minImpactSpeed * impactDmg * 1.2f;
				var dmgInfo = new DamageInfo();

				dmgInfo.Damage = damage;
				dmgInfo.Attacker = Driver != null ? Driver : GameObject;
				dmgInfo.Weapon = GameObject;
				dmgInfo.Position = collision.Contact.Point;

				var physicsBody = other.GameObject.GetComponent<PhysicsBody>();
				if(physicsBody != null)
				{
					physicsBody.Velocity = collision.Contact.Speed;
					physicsBody.AngularVelocity = collision.Contact.NormalSpeed;
				}
			}
		}
	}
	public void OnDamage( in DamageInfo damage )
	{
		if ( Health > 0 && Health != -1 )
		{
			Health -= damage.Damage;

			if ( Health <= 0 )
				Health = 0;

			featuresHandlerHost( 0 );
		}
		else if(Health == 0)
		{
			RemoveDriver();

			SelfBody.GravityScale = 1.2f;

			GameObject.GetComponent<SkinnedModelRenderer>().Tint = Color.FromBytes( 36, 10, 10 );
			GameObject.Network.Refresh();

			SelfBody.Velocity = new Vector3(0,0,500f) + (Vector3.Random * 300f);

			wheel0.GameObject.SetParent( null );
			wheel0.GameObject.AddComponent<SphereCollider>().Radius = 12;
			wheel0.GameObject.AddComponent<Rigidbody>().Velocity = Vector3.Random * 300f;

			wheel1.GameObject.SetParent( null );
			wheel1.GameObject.AddComponent<SphereCollider>().Radius = 12;
			wheel1.GameObject.AddComponent<Rigidbody>().Velocity = Vector3.Random * 300f;

			wheel2.GameObject.SetParent( null );
			wheel2.GameObject.AddComponent<SphereCollider>().Radius = 12;
			wheel2.GameObject.AddComponent<Rigidbody>().Velocity = Vector3.Random * 300f;

			wheel3.GameObject.SetParent( null );
			wheel3.GameObject.AddComponent<SphereCollider>().Radius = 12;
			wheel3.GameObject.AddComponent<Rigidbody>().Velocity = Vector3.Random * 300f;

			featuresHandlerHost( 1 );

			var go = GameObject.Clone( "/prefabs/surface/cardboard-bullet.prefab" );
			go.WorldScale = 100f;
			go.WorldPosition = WorldPosition;
			//go.SetParent( GameObject );

			var go2 = GameObject.Clone( "/prefabs/surface/flesh_bullet.prefab" );
			go2.WorldScale = 20f;
			go2.WorldPosition = WorldPosition;
			//go2.SetParent( GameObject );

			trash.Add( go );
			trash.Add( go2 );

			foreach ( var child in GameObject.Children )
				child.Enabled = false;

			Health = -1;
		}
	}
}

