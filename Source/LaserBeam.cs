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
            float leftoverRange = Range - beamOrigin.magnitude;
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
                    Mathf.Cos((Mathf.PI * 0.42f) * i / segments) * impactOrigin.magnitude * -1.7f);
                offset = offset.RotatedBy(impactOrigin.AngleFlat());
                
                if (i != segments)
                {
                    SpawnBeam(prevPos, warpCenter + offset);
                }
                else //Spawn final offshoot beam
                {
                    Vector3 finalOffset = (warpCenter + offset - prevPos).normalized * leftoverRange;
                    IntVec3 targeted = (prevPos + finalOffset).ToIntVec3();

                    SpawnBeam(prevPos, prevPos + finalOffset);
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

    }
}
