﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WCell.Constants.Spells;
using WCell.Constants.Updates;
using WCell.RealmServer.Entities;

namespace WCell.RealmServer.Spells
{
	/// <summary>
	/// A set of Runes that Death Knights use
	/// </summary>
	public class RuneSet
	{
		public static float DefaultRuneCooldownPerSecond = 0.1f;

		public readonly RuneType[] ActiveRunes = new RuneType[SpellConstants.MaxRuneCount];

		public RuneSet(Character owner)
		{
			Owner = owner;
		}

		public Character Owner
		{
			get;
			internal set;
		}

		public float[] Cooldowns
		{
			get { return Owner.Record.RuneCooldowns; }
		}

		internal void InitRunes()
		{
			var runeSetMask = Owner.Record.RuneSetMask;
			UnpackRuneSetMask(runeSetMask);

			var runeCooldowns = Cooldowns;
			if (runeCooldowns == null || runeCooldowns.Length != SpellConstants.MaxRuneCount)
			{
				Owner.Record.RuneCooldowns = new float[SpellConstants.MaxRuneCount];
			}

			for (RuneType i = 0; i < RuneType.End; i++)
			{
				SetCooldownPerSecond(i, DefaultRuneCooldownPerSecond);
			}
		}

		#region Convert between Rune types
		public bool Convert(RuneType from, RuneType to)
		{
			for (var i = 0u; i < SpellConstants.MaxRuneCount; i++)
			{
				if (ActiveRunes[i] == from)
				{
					Convert(i, to);
					return true;
				}
			}
			return false;
		}

		public void ConvertToDefault(uint index)
		{
			Convert(index, SpellConstants.DefaultRuneSet[index]);
		}

		public void Convert(uint index, RuneType to)
		{
			ActiveRunes[index] = to;
			SpellHandler.SendConvertRune(Owner.Client, index, to);
		}
		#endregion

		#region Check & Consume Rune cost
		/// <summary>
		/// Returns how many runes of the given type are ready
		/// </summary>
		public int GetReadyRunes(RuneType type)
		{
			var count = 0;
			for (var i = 0; i< SpellConstants.MaxRuneCount; i++)
			{
				if (ActiveRunes[i] == type && Cooldowns[i] <= 0)
				{
					count++;
				}
			}
			return count;
		}

		/// <summary>
		/// Whether there are enough runes in this set to satisfy the given cost requirements
		/// </summary>
		public bool HasEnoughRunes(Spell spell)
		{
			var costs = spell.RuneCostEntry;
			if (costs == null || !costs.CostsRunes || Owner.Auras.GetModifiedInt(SpellModifierType.PowerCost, spell, 1) != 1)
			{
				// if we have any rune-related power cost modifier, we have no rune costs at all (only used for Freezing Fog right now)
				return true;
			}
			for (RuneType type = 0; type < (RuneType)costs.CostPerType.Length; type++)
			{
				var cost = costs.CostPerType[(int)type];
				if (cost > 0)
				{
					for (var i = 0; i < SpellConstants.MaxRuneCount; i++)
					{
						if ((ActiveRunes[i] == type || ActiveRunes[i] == RuneType.Death)
							&& Cooldowns[i] <= 0)
						{
							cost--;
						}
					}
					if (cost > 0)
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>
		/// Method is internal because we don't have a packet yet to signal the client spontaneous cooldown updates
		/// </summary>
		internal void ConsumeRunes(Spell spell)
		{
			var costs = spell.RuneCostEntry;
			if (costs == null || !costs.CostsRunes || Owner.Auras.GetModifiedInt(SpellModifierType.PowerCost, spell, 1) != 1)
			{
				// if we have any rune-related power cost modifier, we have no rune costs at all (only used for Freezing Fog right now)
				return;
			}
			for (RuneType type = 0; type < (RuneType)costs.CostPerType.Length; type++)
			{
				var cost = costs.CostPerType[(int)type];
				if (cost > 0)
				{
					// first look for normal runes
					for (var i = 0u; i < SpellConstants.MaxRuneCount; i++)
					{
						if (ActiveRunes[i] == type)
						{
							if (Cooldowns[i] <= 0)
							{
								StartCooldown(i);		// start cooldown
								cost--;
								if (cost == 0)
								{
									break;
								}
							}
						}
					}

					// then consume death runes
					for (var i = 0u; i < SpellConstants.MaxRuneCount; i++)
					{
						if (ActiveRunes[i] == RuneType.Death)
						{
							if (Cooldowns[i] <= 0)
							{
								ConvertToDefault(i);	// Convert death rune back to normal rune
								StartCooldown(i);		// start cooldown
								cost--;
								if (cost == 0)
								{
									break;
								}
							}
						}
					}
				}
			}
		}
		#endregion

		#region Cooldown
		/// <summary>
		/// TODO: Send update to client, if necessary
		/// </summary>
		internal void StartCooldown(uint index)
		{
			Cooldowns[index] = 1;
		}

		/// <summary>
		/// TODO: Send update to client, if necessary
		/// </summary>
		internal void UnsetCooldown(uint index)
		{
			Cooldowns[index] = 0;
		}

		internal void UpdateCooldown(float dt)
		{
			var cds = Cooldowns;
			for (var i = 0u; i < SpellConstants.MaxRuneCount; i++)
			{
				var cd = cds[i] - (dt * GetCooldownPerSecond(ActiveRunes[i]));
				if (cd > 0)
				{
					cds[i] = cd;
				}
				else
				{
					UnsetCooldown(i);
				}
			}
		}

		public float GetCooldownPerSecond(RuneType type)
		{
			return Owner.GetFloat(PlayerFields.RUNE_REGEN_1 + (int)type);
		}

		public void SetCooldownPerSecond(RuneType type, float cdPerSecond)
		{
			Owner.SetFloat(PlayerFields.RUNE_REGEN_1 + (int)type, cdPerSecond);
		}
		#endregion

		#region Serialize & Deserialize
		public int PackRuneSetMask()
		{
			var setMask = 0;
			for (var i = 0; i < SpellConstants.MaxRuneCount; i++)
			{
				setMask |= (((int)ActiveRunes[i] + 1) << (SpellConstants.BitsPerRune * i));	// always add one (since the lowest rune starts at 0)
			}
			return setMask;
		}

		public void UnpackRuneSetMask(int runeSetMask)
		{
			if (runeSetMask == 0)
			{
				// no runes set
				SpellConstants.DefaultRuneSet.CopyTo(ActiveRunes, 0);
			}
			else
			{
				for (var i = 0; i < SpellConstants.MaxRuneCount; i++)
				{
					// subtract one (since the lowest rune started at 0)
					var rune = (RuneType)((runeSetMask & SpellConstants.SingleRuneFullBitMask) - 1);
					if (rune >= RuneType.End || rune < 0)
					{
						ActiveRunes[i] = SpellConstants.DefaultRuneSet[i];
					}
					else
					{
						ActiveRunes[i] = rune;
					}
					runeSetMask >>= SpellConstants.BitsPerRune;
				}
			}
		}

		/// <summary>
		/// Used for packets
		/// </summary>
		internal byte GetActiveRuneMask()
		{
			var mask = 0;
			var cds = Cooldowns;
			for (var i = 0; i < SpellConstants.MaxRuneCount; i++)
			{
				if (cds[i] == 0)
				{
					mask |= 1 << i;
				}
			}
			return (byte) mask;
		}
		#endregion
	}
}