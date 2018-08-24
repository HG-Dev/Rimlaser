using System;

using UnityEngine;
using RimWorld;
using Verse;
using System.Collections.Generic;

namespace Rimlaser
{
    public class LaserBeam : Bullet
    {
        new LaserBeamDef def => base.def as LaserBeamDef;
        Thing Emitter => base.launcher.EquipmentOrBuilding();
        /// <summary>
        /// List of cells assumed to have nothing of interest inside.
        /// </summary>
        protected static int lastChecked = Find.TickManager.TicksGame;
        protected static List<IntVec3> checkedCells = new List<IntVec3>();

        public float Range
        {
            get
            {
                if (!Emitter.def.Verbs.NullOrEmpty<VerbProperties>())
                {
                    return Emitter.def.Verbs[0].range;
                }
                return 0f;
            }
        }

        public override void Draw()
        {

        }

        void TriggerEffect(EffecterDef effect, Vector3 position)
        {
            TriggerEffect(effect, IntVec3.FromVector3(position));
        }

        void TriggerEffect(EffecterDef effect, IntVec3 dest)
        {
            if (effect == null) return;

            var targetInfo = new TargetInfo(dest, Map, false);

            Effecter effecter = effect.Spawn();
            effecter.Trigger(targetInfo, targetInfo);
            effecter.Cleanup();
        }

        /// <param name="a">Origin</param>
        /// <param name="b">Destination</param>
        void SpawnBeam(Vector3 a, Vector3 b)
        {
            LaserBeamGraphic graphic = ThingMaker.MakeThing(def.beamGraphic, null) as LaserBeamGraphic;
            if (graphic == null) return;

            graphic.def = def;
            graphic.Setup(Emitter, a, b);
            GenSpawn.Spawn(graphic, origin.ToIntVec3(), Map, WipeMode.Vanish);
        }

        void SpawnBeamReflections(Vector3 a, Vector3 b, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = (b - a).normalized;
                Vector3 c = b - dir.RotatedBy(Rand.Range(-22.5f, 22.5f)) * Rand.Range(1f, 4f);

                SpawnBeam(b, c);
            }
        }

        void SpawnBeamRefractions(Vector3 a, Vector3 b, Thing shieldedThing)
        {
            Vector3 beamOrigin = (b - a);
            Vector3 impactOrigin = (shieldedThing.TrueCenter() - b);
            Vector3 warpCenter = b + (impactOrigin * 2);
            float leftoverRange = Range - beamOrigin.MagnitudeHorizontal();
            float impactSiteAngle = impactOrigin.AngleFlat();
            float beamAngle = beamOrigin.AngleFlat();
            float impactAngle = (impactSiteAngle - beamAngle);
            int impactAngleInt;
            //Restrict impact angle to -90 ~ 90 degrees
            if (impactAngle < -90) impactAngle += 360;
            if (impactAngle >  90) impactAngle -= 360;
            if (impactAngle > 0)
                impactAngleInt = Mathf.CeilToInt(impactAngle);
            else
                impactAngleInt = Mathf.FloorToInt(impactAngle);
            int dir = Mathf.Clamp(impactAngleInt, -1, 1);

            Vector3 prevPos = b; //Start beam warping effect from impact site
            Vector3 offset;
            int segments = 10;
            for (int i = 1; i <= segments; i++)
            {
                offset = new Vector3(
                    Mathf.Sin((Mathf.PI * 0.42f * -dir) * i / segments) * Mathf.Lerp(0.5f, 0.9f, (impactAngle + 90) / 180), 0,
                    Mathf.Cos((Mathf.PI * 0.42f) * i / segments) * impactOrigin.MagnitudeHorizontal() * -1.7f);
                offset = offset.RotatedBy(impactOrigin.AngleFlat());
                
                if (i != segments)
                {
                    SpawnBeam(prevPos, warpCenter + offset);
                }
                else //Spawn final offshoot beam
                {
                    Vector3 finalOffset = (warpCenter + offset - prevPos).normalized * leftoverRange;
                    IntVec3 targeted = (prevPos + finalOffset).ToIntVec3();
                    Vector3 beamEnd = prevPos + finalOffset;
                    Thing hitThing = null;
                    //Shorten beam on aggressive intercept
                    if (CheckForInterceptBetween(prevPos, beamEnd, out beamEnd, out hitThing, true))
                        Impact(hitThing); //Damage whatever intercepted the beam
                    else
                        Log.Message("Beam interception: nothing found.");

                    SpawnBeam(prevPos, beamEnd);
                }
                prevPos = warpCenter + offset;
            }
        }

        protected override void Impact(Thing hitThing)
        {
            bool shielded = hitThing.IsShielded() && def.IsWeakToShields;
            LaserGunDef defWeapon = equipmentDef as LaserGunDef;
            Vector3 dir = (destination - origin).normalized;
            dir.y = 0;

            Vector3 a = origin + dir * (defWeapon == null ? 0.9f : defWeapon.barrelLength);
            Vector3 b = shielded ? hitThing.TrueCenter() - dir.RotatedBy(Rand.Range(-22.5f,22.5f)) * 0.8f : destination;
            a.y = b.y = def.Altitude;

            SpawnBeam(a, b);

            IDrawnWeaponWithRotation weapon = Emitter as IDrawnWeaponWithRotation;
            if (weapon != null)
            {
                float angle = (b - a).AngleFlat() - (intendedTarget.CenterVector3 - a).AngleFlat();
                weapon.RotationOffset = (angle + 180) % 360 - 180;
            }

            if (hitThing == null)
            {
                TriggerEffect(def.explosionEffect, destination);
            }
            else
            {
                if (hitThing is Pawn)
                {
                    Pawn hitPawn = hitThing as Pawn;
                    if (shielded)
                    {
                        weaponDamageMultiplier *= def.shieldDamageMultiplier;

                        //SpawnBeamReflections(a, b, 5);
                        SpawnBeamRefractions(a, b, hitThing);
                    }
                }

                TriggerEffect(def.explosionEffect, ExactPosition);
            }

            base.Impact(hitThing);
        }

        bool CheckForInterceptBetween(Vector3 checkStart, Vector3 checkEnd, out Vector3 point, out Thing hitThing, bool aggressive = false)
        {
            IntVec3 a = checkStart.ToIntVec3(), b = checkEnd.ToIntVec3();
            point = checkEnd;
            hitThing = CheckForIntercept(b, true);
            float originalAngle = (checkEnd - checkStart).AngleFlat();
            if (a != b && a.InBounds(Map) && b.InBounds(Map))
            {
                if (lastChecked + 1 < Find.TickManager.TicksGame)
                {
                    //Update empty cell list every two ticks
                    lastChecked = Find.TickManager.TicksGame;
                    checkedCells.Clear();
                }

                Vector3 checkPoint = checkStart;
                Vector3 increment = (checkEnd - checkStart).normalized * 0.2f;
                int checkPointsMax = Mathf.FloorToInt((checkEnd - checkStart).MagnitudeHorizontal() / 0.2f);
                for (int i = 0; i < checkPointsMax; i++)
                {
                    checkPoint += increment;
                    IntVec3 checkCell = checkPoint.ToIntVec3();
                    if (!LaserBeam.checkedCells.Contains(checkCell))
                    {
                        Log.Message("Checking cell...");
                        //Stray: how far the actual point on the line strays from the center of the checked cell.
                        //This is not a perfect method of judging a direct hit, but it should work on the whole.
                        Vector3 stray = checkCell.ToVector3() - checkPoint;
                        Thing hit = CheckForIntercept(checkCell, i <= 1, stray);
                        if (hit != null)
                        {
                            //Return hit info
                            Log.Message("Found thing to hit! " + hit.def.defName);
                            hitThing = hit;
                            point = checkPoint;
                            return true;
                        }
                        //Nothing "hittable" was found
                        checkedCells.Add(checkCell);
                    }
                }
            }
            return (hitThing != null);
        }

        protected Thing CheckForIntercept(IntVec3 cell, bool intended = false, Vector3? stray = null)
        {
            List<Thing> thingList = cell.GetThingList(base.Map);
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                if (thing.Spawned && thing != launcher) //CURRENTLY SEARCHING FOR CONDITIONS TO REPLACE CanHit()
                {
                    Log.Message("Found thing that could be hit.");
                    bool isStructure = false;
                    if (thing.def.Fillage == FillCategory.Full)
                    {
                        //Check for door
                        Building_Door door = thing as Building_Door;
                        if (door == null || !door.Open)
                        {
                            return door;
                        }
                        isStructure = true;
                    }

                    float hitChance = 0f;
                    Pawn pawn = thing as Pawn;
                    if (pawn != null)
                    {
                        hitChance = 0.4f * Mathf.Clamp(pawn.BodySize, 0.1f, 2f);
                        if (pawn.GetPosture() != PawnPosture.Standing)
                        {
                            hitChance *= 0.1f;
                        }
                        if (this.launcher != null && pawn.Faction != null && this.launcher.Faction != null && !pawn.Faction.HostileTo(this.launcher.Faction))
                        {
                            hitChance *= 0.4f;
                        }
                    }
                    else if (thing.def.fillPercent > 0.2f)
                    {
                        if (isStructure && !intended)
                        {
                            //Increase this test value if corners are being ignored.
                            //When stray distance has been supplied, it can be assumed that the beam
                            //could potentially pass through solid rock if we don't return a hit.
                            hitChance = (stray.HasValue && stray.Value.MagnitudeHorizontalSquared() < 0.5f) ? 1f : 0.05f;
                        }
                        else if (intended)
                        {
                            hitChance = thing.def.fillPercent * 1f;
                        }
                        else
                        {
                            hitChance = thing.def.fillPercent * 0.15f;
                        }
                    }
                    if (hitChance > 1E-05f)
                    {
                        if (Rand.Chance(hitChance))
                        {
                            return thing;
                        }
                    }
                }
            }

            return null;
        }

        //Currently reference use only
        protected bool DebugCanHit(Thing thing)
        {
            if (!thing.Spawned)
            {
                return false;
            }
            if (thing == this.launcher)
            {
                return false;
            }
            bool flag = false;
            CellRect.CellRectIterator iterator = thing.OccupiedRect().GetIterator();
            while (!iterator.Done())
            {
                List<Thing> thingList = iterator.Current.GetThingList(base.Map);
                bool flag2 = false;
                for (int i = 0; i < thingList.Count; i++)
                {
                    Log.Message("CanHit: Found a thing. Altitudes = " + thingList[i].def.Altitude.ToString() + " " + thing.def.Altitude.ToString());
                    if (thingList[i] != thing && thingList[i].def.Fillage == FillCategory.Full && thingList[i].def.Altitude >= thing.def.Altitude)
                    {
                        flag2 = true;
                        break;
                    }
                }
                if (!flag2)
                {
                    flag = true;
                    break;
                }
                iterator.MoveNext();
            }
            if (!flag)
            {
                Log.Message("CanHit: !flag");
                return false;
            }
            ProjectileHitFlags hitFlags = this.HitFlags;
            if (thing == this.intendedTarget && (hitFlags & ProjectileHitFlags.IntendedTarget) != ProjectileHitFlags.None)
            {
                return true;
            }
            if (thing != this.intendedTarget)
            {
                if (thing is Pawn)
                {
                    if ((hitFlags & ProjectileHitFlags.NonTargetPawns) != ProjectileHitFlags.None)
                    {
                        return true;
                    }
                }
                else if ((hitFlags & ProjectileHitFlags.NonTargetWorld) != ProjectileHitFlags.None)
                {
                    return true;
                }
            }
            Log.Message("CanHit: Returning STUPID");
            return thing == this.intendedTarget && thing.def.Fillage == FillCategory.Full;
        }

    }
}
