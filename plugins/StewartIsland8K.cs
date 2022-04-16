using Rust;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
namespace Oxide.Plugins
{
    [Info("StewartIsland8K", "bmgjet", "1.0.0")]
    [Description("Functions Required for True Stewart Island 8K Map")]
    public class StewartIsland8K : RustPlugin
    {
        public Timer CargoCheck;
		private Timer TrainCheck;
		public static StewartIsland8K plugin;
		public List<Vector3> RailPath = new List<Vector3>();
		public List<BaseEntity> Trains = new List<BaseEntity>();
		public bool TrainUnlimitedFuel = true;
		public bool AllowWorkCarts = true;
		public bool AllowAboveGroundCarts = true;
		private string WorkCartPrefab = "assets/content/vehicles/workcart/workcart.entity.prefab";
		private string AboveGroundTrainPrefab = "assets/content/vehicles/traintemp/trainenginetemp.entity.prefab";
		private void Init(){plugin = this;}
		private void OnServerInitialized(bool initial) { if (initial) { DelayStaryUp(); } else { Startup(); } }
        private void DelayStaryUp() { timer.Once(10f, () => { try { if (Rust.Application.isLoading) { DelayStaryUp(); return; } } catch { } Startup(); }); }
        private void Startup() { ServerMgr.Instance.StartCoroutine(GeneratRailGrid()); foreach (BaseNetworkable vehicle in BaseNetworkable.serverEntities.entityList.Values) { if (vehicle is BaseVehicle && vehicle.GetComponent<EdgeTeleport>() == null) vehicle.gameObject.AddComponent<EdgeTeleport>(); } }
        void OnTerrainCreate(TerrainGenerator _terrain) { Puts("Map by bmgjet"); World.Size = 8000; ConVar.Server.worldsize = (int)8000; }
        private void OnEntitySpawned(BaseEntity baseEntity)
        {
            if (!Rust.Application.isLoading && !Rust.Application.isLoadingSave)
            {
                if (baseEntity is CargoShip) { baseEntity.transform.position = TerrainMeta.Path.OceanPatrolFar.GetRandom(); return; }
                BaseVehicle vehicle = baseEntity as BaseVehicle;
                if (vehicle != null) if (vehicle.GetComponent<EdgeTeleport>() == null) { vehicle.gameObject.AddComponent<EdgeTeleport>(); return; }
            }
        }
        private void Unload()
        {
            foreach (BaseNetworkable vehicle in BaseNetworkable.serverEntities.entityList.Values)
            {
                if (vehicle is BaseVehicle)
                {
                    EdgeTeleport et = vehicle.GetComponent<EdgeTeleport>();
                    if (et != null) { UnityEngine.Object.Destroy(et); }
					LazyRail lr = vehicle.GetComponent<LazyRail>();
					if (lr != null) { UnityEngine.Object.Destroy(lr); }
				}
            }
			if (TrainCheck != null) { TrainCheck.Destroy(); }
			plugin = null;
        }
		private void OnEntityDeath(BaseCombatEntity entity, HitInfo info){if (entity != null && info != null) { if (Trains.Contains(entity)) { Trains.Remove(entity); } }}
		private object OnEntityDismounted(BaseMountable mount, BasePlayer player) { if (player.IsNpc && mount.VehicleParent() is CH47Helicopter) { player.Invoke(() => { if (player == null) { return; } BaseNavigator BN = player.GetComponent<BaseNavigator>(); if (BN == null) { return; } NavMeshHit hit; if (NavMesh.SamplePosition(player.transform.position, out hit, 30, -1)) { player.gameObject.layer = 17; player.ServerPosition = hit.position; BN.Agent.Warp(player.ServerPosition); player.Invoke(() => BN.CanUseNavMesh = true, 2f); } }, 1f); } return null; }
        private object OnCargoShipEgress(CargoShip cs)
        {
            if (cs != null)
            {
                bool BlockEgress = false;
                if (TerrainMeta.BiomeMap.GetBiomeMaxType(cs.transform.position, -1) == 8) { BlockEgress = true; }
                if (BlockEgress)
                {
                    Timer CheckEgress = timer.Once(30f, () => { if (cs != null) cs.StartEgress(); });
                    return true;
                }
				if(TrainCheck != null) { TrainCheck.Destroy(); }
                if (CargoCheck != null) { CargoCheck.Destroy(); }
            }
            return null;
        }
		private void CheckTrains()
		{
			if (Rust.Application.isLoading)
			{
				timer.Once(10f, () => { CheckTrains(); });
				return;
			}
			if (RailPath.Count > 5)
			{
				List<Vector3> BasePositions = new List<Vector3>();
				for (int s = 0; s < 2; s++)
				{
					if (Trains.Count >= 2) { break; }
					for (int i = 0; i < 100; i++)
					{
						Vector3 SpawnPos = RailPath.GetRandom();
						bool valid = true;
						foreach (Vector3 b in BasePositions)
						{
							if (Vector3.Distance(SpawnPos, b) < 400) { valid = false; break; }
						}
						if (valid)
						{
							BaseEntity train = TrainSpawn(SpawnPos, Random.Range(-1, 2));
							if (train != null)
							{
								Trains.Add(train);
								BasePositions.Add(SpawnPos);
								break;
							}
						}
					}
				}
			}
		}
		private BaseEntity TrainSpawn(Vector3 dropPosition, int Type, string prefab = "", string TrainType = "")
		{
			switch (Type)
			{
				case -1:
				case 0:
					if (AllowWorkCarts) { prefab = WorkCartPrefab; }
					break;
				case 1:
				case 2:
					if (AllowAboveGroundCarts) { prefab = AboveGroundTrainPrefab; }
					break;
			}
			if (prefab == "")
			{
				if (AllowWorkCarts) { prefab = WorkCartPrefab; }
				else if (AllowAboveGroundCarts) { prefab = AboveGroundTrainPrefab; }
				else { return null; }
			}
			if (prefab == AboveGroundTrainPrefab) { TrainType = "Above Ground Train"; }
			else { TrainType = "Workcart"; }
			BaseEntity baseEntity = GameManager.server.CreateEntity(prefab, dropPosition, Quaternion.identity, true);
			if (baseEntity == null) return null;
			baseEntity.syncPosition = true;
			baseEntity.globalBroadcast = true;
			baseEntity.enableSaving = false;
			baseEntity.Spawn();
			Puts("Spawned " + TrainType + " @ " + baseEntity.transform.position.ToString());
			return baseEntity;
		}
		private IEnumerator GeneratRailGrid()
		{
			var checks = 0;
			var _instruction = ConVar.FPS.limit > 80 ? CoroutineEx.waitForSeconds(0.01f) : null;
			foreach (PathList pathList in World.GetPaths("Rail").AsEnumerable<PathList>().Reverse<PathList>())
			{
				foreach (Vector3 v in pathList.Path.Points)
				{
					if (++checks >= 1000)
					{
						checks = 0;
						yield return _instruction;
					}
					RailPath.Add(v);
				}
			}
			CheckTrains();
			plugin.TrainCheck = timer.Every(120, () => { CheckTrains(); });
		}
		private class EdgeTeleport : MonoBehaviour
        {
			BaseEntity baseEntity;
            BaseMountable vehicle;
			TrainCar _train;
            bool halftick = false;
			bool run = false;
            private void Awake() {
				baseEntity = GetComponent<BaseEntity>();
				_train = baseEntity as TrainCar;
				if(_train != null)
                {
					baseEntity.gameObject.AddComponent<LazyRail>();
					return;
				}
				if(baseEntity is Horse || baseEntity is ModularCar){return;}
				vehicle = baseEntity as BaseMountable;
				if(vehicle != null){run = true;}
			}
            private void FixedUpdate()
            {
				if (!run) return;
                if (halftick) { halftick = false; return; } else { halftick = true; }
                if (vehicle == null) Destroy(this);
                if (!vehicle.IsMounted()) { return; }
                if (vehicle.transform.position.x > 3910f || vehicle.transform.position.x < -3910)
                {
                    vehicle.transform.position = new Vector3(vehicle.transform.position.x * -0.98f, vehicle.transform.position.y, vehicle.transform.position.z);
                    vehicle.TransformChanged();
                }
                else if (vehicle.transform.position.z > 3910f || vehicle.transform.position.z < -3910)
                {
                    vehicle.transform.position = new Vector3(vehicle.transform.position.x, vehicle.transform.position.y, vehicle.transform.position.z * -0.98f);
                    vehicle.TransformChanged();
                    return;
                }
            }
        }
		private class LazyRail : MonoBehaviour
		{
			public BaseEntity _train;
			public TrainEngine _trainEngine;
			public TrainCar _trainCar;
			public float CurrentSpeed = 0;
			private void Awake()
			{
				plugin.NextFrame(() =>
				{
					_train = GetComponent<BaseEntity>();
					if (_train == null) { return; }
					_trainEngine = _train as TrainEngine;
					if (_trainEngine == null) { return; }
					_trainEngine.collisionEffect.guid = null;
					_trainCar = _train as TrainCar;
					if (_trainCar == null) { return; }
					_trainCar.frontCollisionTrigger.interestLayers = Layers.Mask.Vehicle_World;
					_trainCar.rearCollisionTrigger.interestLayers = Layers.Mask.Vehicle_World;
					plugin.NextFrame(() => { if (_trainEngine != null) _trainEngine.CancelInvoke("DecayTick"); });
					if (plugin.TrainUnlimitedFuel)
					{
						_trainEngine.idleFuelPerSec = 0f;
						_trainEngine.maxFuelPerSec = 0f;
						StorageContainer fuelContainer = _trainEngine.GetFuelSystem()?.GetFuelContainer();
						if (fuelContainer != null)
						{
							fuelContainer.inventory.AddItem(fuelContainer.allowedItem, 1);
							fuelContainer.SetFlag(BaseEntity.Flags.Locked, true);
						}
					}
					_trainCar.FrontTrackSection.isStation = true;
				});
			}
			private void OnDestroy()
			{
					enabled = false;
					CancelInvoke();
			}
			public void Die() { if (this != null) { Destroy(this); } }
			public void movetrain()
			{
				Vector3 Direction = base.transform.forward;
				TrainTrackSpline preferredAltTrack = (_trainCar.RearTrackSection != _trainCar.FrontTrackSection) ? _trainCar.RearTrackSection : null;
				TrainTrackSpline trainTrackSpline;
				bool flag;
				_trainCar.FrontWheelSplineDist = _trainCar.FrontTrackSection.GetSplineDistAfterMove(_trainCar.FrontWheelSplineDist, Direction, 1, _trainCar.curTrackSelection, out trainTrackSpline, out flag, preferredAltTrack);
				Vector3 targetFrontWheelTangent;
				Vector3 positionAndTangent = trainTrackSpline.GetPositionAndTangent(_trainCar.FrontWheelSplineDist, Direction, out targetFrontWheelTangent);
				_trainCar.SetTheRestFromFrontWheelData(ref trainTrackSpline, positionAndTangent, targetFrontWheelTangent);
				_trainCar.FrontTrackSection = trainTrackSpline;
				float frontWheelSplineDist;
				if (TrainTrackSpline.TryFindTrackNearby(_trainCar.GetFrontWheelPos(), 2f, out trainTrackSpline, out frontWheelSplineDist) && trainTrackSpline.HasClearTrackSpaceNear(_trainCar))
				{
					_trainCar.FrontWheelSplineDist = frontWheelSplineDist;
					Vector3 positionAndTangent2 = trainTrackSpline.GetPositionAndTangent(_trainCar.FrontWheelSplineDist, Direction, out targetFrontWheelTangent);
					_trainCar.SetTheRestFromFrontWheelData(ref trainTrackSpline, positionAndTangent2, targetFrontWheelTangent);
					_trainCar.FrontTrackSection = trainTrackSpline;
					return;
				}
			}
			public void FixedUpdate()
			{
				if (_train == null || _trainEngine == null || _trainCar == null) { return; }
				if (_trainCar.TrackSpeed == 0) { return; }
				Vector3 test0 = _trainCar.GetFrontWheelPos();
				Vector3 test1 = _trainCar.GetRearWheelPos();
				Vector3 test2 = plugin.RailPath[plugin.RailPath.Count - 1];
				Vector3 test3 = plugin.RailPath[0];
				test0.y = 0; test1.y = 0; test2.y = 0; test3.y = 0;
				if (Vector3.Distance(test1, test2) < 0.1f)
				{
					if (_trainCar.TrackSpeed < 0 || _trainEngine.GetThrottleFraction() < 0)
					{
						_trainCar.transform.position = plugin.RailPath[5];
						movetrain();
						_trainCar.TrackSpeed = CurrentSpeed;
						return;
					}
				}
				else if (Vector3.Distance(test0, test3) < 0.1f)
				{
					if (_trainCar.TrackSpeed >= 0 || _trainEngine.GetThrottleFraction() >= 0)
					{
						_trainCar.transform.position = plugin.RailPath[plugin.RailPath.Count - 5];
						movetrain();
						_trainCar.TrackSpeed = CurrentSpeed;
						return;
					}
				}
				else if (Vector3.Distance(test0, test2) < 0.1f)
				{
					if (_trainCar.TrackSpeed >= 0 || _trainEngine.GetThrottleFraction() >= 0)
					{
						_trainCar.transform.position = plugin.RailPath[5];
						movetrain();
						_trainCar.TrackSpeed = CurrentSpeed;
						return;
					}
				}
				else if (Vector3.Distance(test1, test3) < 0.1f)
				{
					if (_trainCar.TrackSpeed <= 0 || _trainEngine.GetThrottleFraction() <= 0)
					{
						_trainCar.transform.position = plugin.RailPath[plugin.RailPath.Count - 5];
						movetrain();
						_trainCar.TrackSpeed = CurrentSpeed;
						return;
					}
				}
				CurrentSpeed = _trainCar.TrackSpeed;
			}
		}
	}
}
