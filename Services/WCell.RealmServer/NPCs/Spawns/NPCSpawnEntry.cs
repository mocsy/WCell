using System;
using System.Collections.Generic;
using NLog;
using WCell.Constants.NPCs;
using WCell.Constants.World;
using WCell.RealmServer.Content;
using WCell.RealmServer.Entities;
using WCell.RealmServer.Spawns;
using WCell.RealmServer.Waypoints;
using WCell.Util;
using WCell.Util.Data;
using WCell.RealmServer.Global;
using WCell.Util.Graphics;

namespace WCell.RealmServer.NPCs.Spawns
{
	/// <summary>
	/// Spawn-information for NPCs
	/// </summary>
	[DataHolder]
	public partial class NPCSpawnEntry : SpawnEntry<NPCSpawnPoolTemplate, NPCSpawnEntry, NPC, NPCSpawnPoint, NPCSpawnPool>, INPCDataHolder, IWorldLocation
	{
		private static uint highestSpawnId;
		public static uint GenerateSpawnId()
		{
			return ++highestSpawnId;
		}

		public NPCSpawnEntry()
		{

		}

		public NPCId EntryId;

		[NotPersistent]
		public NPCEntry Entry
		{
			get;
			set;
		}

		public float Orientation;
		public AIMovementType MoveType;

		public bool IsDead;

		public uint DisplayIdOverride;

		public uint EquipmentId;

		public NPCAddonData AddonData
		{
			get;
			set;
		}

		[NotPersistent]
		public NPCEquipmentEntry Equipment;

		public override NPC SpawnObject(NPCSpawnPoint point)
		{
			return Entry.Create(point);
		}

		private void AddToPoolTemplate()
		{
			if (PoolId != 0 && NPCMgr.SpawnPoolTemplates.TryGetValue(PoolId, out m_PoolTemplate))
			{
				// pool already exists
				m_PoolTemplate.AddEntry(this);
			}
			else
			{
				// create and register new pool
				m_PoolTemplate = new NPCSpawnPoolTemplate(this);
			}
		}

		#region FinalizeDataHolder
		/// <summary>
		/// Finalize this NPCSpawnEntry
		/// </summary>
		/// <param name="addToPool">If set to false, will not try to add it to any pool (recommended for custom NPCSpawnEntry that share a pool)</param>
		public void FinalizeDataHolder()
		{
			FinalizeDataHolder(true);
		}

		/// <summary>
		/// Finalize this NPCSpawnEntry
		/// </summary>
		/// <param name="addToPool">If set to false, will not try to add it to any pool (recommended for custom NPCSpawnEntry that share a pool)</param>
		public void FinalizeDataHolder(bool addToPool)
		{
			if (Entry == null)
			{
				Entry = NPCMgr.GetEntry(EntryId);
				if (Entry == null)
				{
					ContentMgr.OnInvalidDBData("{0} had an invalid EntryId.", this);
					return;
				}
			}

			var respawn = RespawnSeconds != 0 ? RespawnSeconds : NPCMgr.DefaultMinRespawnDelay;
			AutoSpawns = RespawnSeconds > 0;
			if (RespawnSecondsMin == 0)
			{
				RespawnSecondsMin = respawn;
			}
			if (RespawnSecondsMax == 0)
			{
				RespawnSecondsMax = Math.Max(respawn, RespawnSecondsMin);
			}

			if (PhaseMask == 0)
			{
				PhaseMask = 1;
			}

			if (EquipmentId != 0)
			{
				Equipment = NPCMgr.GetEquipment(EquipmentId);
			}

			// seems to be a DB issue where the id was inserted as a float
			if (DisplayIdOverride == 16777215)
			{
				DisplayIdOverride = 0;
			}

			if (SpawnId > highestSpawnId)
			{
				highestSpawnId = SpawnId;
			}
			else if (SpawnId == 0)
			{
				SpawnId = GenerateSpawnId();
			}

			if (AddonData != null)
			{
				AddonData.InitAddonData(this);
			}


			if (MapId != MapId.End)
			{
				// valid map
				// add to NPCEntry
				Entry.SpawnEntries.Add(this);

				// add to list of NPCSpawnEntries
				ArrayUtil.Set(ref NPCMgr.SpawnEntries, SpawnId, this);

				if (addToPool)
				{
					// add to pool
					AddToPoolTemplate();
				}
			}

			// finished initializing, now call the hooks
			foreach (var handler in Entry.SpawnTypeHandlers)
			{
				if (handler != null)
				{
					handler(this);
				}
			}
		}
		#endregion

		#region Events
		internal void NotifySpawned(NPC npc)
		{
			var evt = Spawned;
			if (evt != null)
			{
				evt(npc);
			}
		}
		#endregion

		#region Waypoints
		[NotPersistent]
		public readonly LinkedList<WaypointEntry> Waypoints = new LinkedList<WaypointEntry>();

		private bool m_HasDefaultWaypoints;

		/// <summary>
		/// Whether this SpawnEntry has fixed Waypoints from DB
		/// </summary>
		[NotPersistent]
		public bool HasDefaultWaypoints
		{
			get { return m_HasDefaultWaypoints; }
			set
			{
				m_HasDefaultWaypoints = value;
				Entry.MovesRandomly = false;
			}
		}

		public void RecreateRandomWaypoints()
		{
			Waypoints.Clear();
			CreateRandomWaypoints();
		}

		public void CreateRandomWaypoints()
		{
			var terrain = TerrainMgr.GetTerrain(MapId);
			if (terrain != null)
			{
				var gen = new RandomWaypointGenerator();
				var wps = gen.GenerateWaypoints(terrain, Position);
				AddWaypoints(wps);
			}
		}
		public int WaypointCount
		{
			get { return Waypoints.Count; }
		}

		/// <summary>
		/// Creates a Waypoint but does not add it
		/// </summary>
		public WaypointEntry CreateWaypoint(Vector3 pos, float orientation)
		{
			var last = Waypoints.Last;
			WaypointEntry entry;
			if (last != null)
			{
				entry = new WaypointEntry
				{
					Id = last.Value.Id + 1,
					SpawnEntry = this,
				};
			}
			else
			{
				entry = new WaypointEntry
				{
					Id = 1,
					SpawnEntry = this,
				};
			}

			entry.Position = pos;
			entry.Orientation = orientation;
			return entry;
		}

		/// <summary>
		/// Adds the given positions as WPs
		/// </summary>
		/// <param name="wps"></param>
		public void AddWaypoints(Vector3[] wps)
		{
			if (wps.Length < 1)
			{
				throw new ArgumentException("wps are empty.");
			}
			var len = wps.Length;
			Vector3 first;
			if (Waypoints.Count > 0)
			{
				// adding to already existing WPs: Make sure that the orientation is correct
				first = Waypoints.First.Value.Position;
				var last = Waypoints.Last.Value;
				last.Orientation = last.Position.GetAngleTowards(wps[0]);
			}
			else
			{
				first = wps[0];
			}

			for (var i = 0; i < len; i++)
			{
				var pos = wps[i];
				WaypointEntry wp;
				if (i < len - 1)
				{
					wp = CreateWaypoint(pos, pos.GetAngleTowards(wps[i + 1]));
				}
				else if (i > 0)
				{
					wp = CreateWaypoint(pos, pos.GetAngleTowards(first));
				}
				else
				{
					wp = CreateWaypoint(pos, Utility.Random(0f, 2 * MathUtil.PI));
				}
				Waypoints.AddLast(wp);
			}
		}

		public LinkedListNode<WaypointEntry> AddWaypoint(Vector3 pos, float orientation)
		{
			var newWp = CreateWaypoint(pos, orientation);
			return Waypoints.AddLast(newWp);
		}

		public LinkedListNode<WaypointEntry> InsertWaypointAfter(WaypointEntry entry, Vector3 pos, float orientation)
		{
			var newWp = CreateWaypoint(pos, orientation);
			return Waypoints.AddAfter(entry.Node, newWp);
		}

		public LinkedListNode<WaypointEntry> InsertWaypointBefore(WaypointEntry entry, Vector3 pos, float orientation)
		{
			var newWp = CreateWaypoint(pos, orientation);
			return Waypoints.AddBefore(entry.Node, newWp);
		}

		/// <summary>
		/// Increases the Id of all Waypoints, starting from the given node
		/// </summary>
		/// <param name="node">May be null</param>
		public void IncreaseWPIds(LinkedListNode<WaypointEntry> node)
		{
			while (node != null)
			{
				node.Value.Id++;
				node = node.Next;
			}
		}
		#endregion

		public override string ToString()
		{
			return string.Format(GetType().Name + " #{0} ({1} #{2})", SpawnId, EntryId, (uint)EntryId);
		}

		public static IEnumerable<NPCSpawnEntry> GetAllDataHolders()
		{
			return NPCMgr.SpawnEntries;
		}
	}
}