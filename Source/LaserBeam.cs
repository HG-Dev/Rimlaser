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

        /// <summary>
        /// Spawns a LaserBeamGraphic object utilizing settings obtained
        /// from a weapon held by a launcher or launcher itself,
        /// plus an exact origin and destination Vector3.
        /// </summary>
        /// <param name="a">Origin</param>
        /// <param name="b">Destination</param>
        void SpawnBeam(Vector3 a, Vector3 b)
        {
            LaserBeamGraphic graphic = ThingMaker.MakeThing(def.beamGraphic, null) as LaserBeamGraphic;
            if (graphic == null) return;

            graphic.def = def;
            graphic.Setup(launcher, a, b);
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

        void SpawnBeamRefractions(LaserBeam original, Thing shieldedThing)
        {
            Vector3 beamOrigin = (original.ExactPosition - original.origin);
            Vector3 impactOrigin = (original.ExactPosition - original.launcher.TrueCenter());
            float impactSiteAngle = impactOrigin.AngleFlat();
            float beamAngle = beamOrigin.AngleFlat();
            float impactAngle = impactSiteAngle - beamAngle;
            Log.Message("Impact angle per shield center: " + impactSiteAngle.ToString());
            Log.Message("Original angle of beam: " + beamAngle.ToString());
            Log.Message("Impact angle on shield: " + impactAngle.ToString());

        }

        protected override void Impact(Thing hitThing)
        {
            Log.Message("IMPACT!");
            bool shielded = hitThing.IsShielded() && def.IsWeakToShields;
            LaserGunDef defWeapon = equipmentDef as LaserGunDef;
            Vector3 dir = (destination - origin).normalized;
            dir.y = 0;

            Vector3 a = origin + dir * (defWeapon == null ? 0.9f : defWeapon.barrelLength);
            Vector3 b = shielded ? hitThing.TrueCenter() - dir.RotatedBy(Rand.Range(-22.5f,22.5f)) * 0.8f : destination;
            a.y = b.y = def.Altitude;

            SpawnBeam(a, b);

            Pawn pawn = launcher as Pawn;
            IDrawnWeaponWithRotation weapon = null;
            if (pawn != null && pawn.equipment != null) weapon = pawn.equipment.Primary as IDrawnWeaponWithRotation;
            if (weapon == null) weapon = launcher as IDrawnWeaponWithRotation;
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

                        SpawnBeamReflections(a, b, 5);
                        SpawnBeamRefractions(this, hitThing);
                    }
                }

                TriggerEffect(def.explosionEffect, ExactPosition);
            }

            base.Impact(hitThing);
        }

    }
}
