// SPDX-License-Identifier: MPL-2.0
using FAF.Cabinet.Core;
using FAF.Transport;
using RimWorld;
using Verse;

namespace FAF.Cabinet
{
    public sealed partial class Building_FlyingFridgeCabinet
    {
        private float internalVacuum = 1f;
        private bool cabinAtmosphereInitialized;
        private bool lastLifeSupportPowered;

        // This is pressure state only, never another power/fuel balance or occupant registry.
        // 此处只保存压力状态，不是另一份电力/燃料余额或乘员名册。
        internal void ExposeCabinEnvironment()
        {
            Scribe_Values.Look(ref internalVacuum, "fafInternalVacuum", 1f);
            Scribe_Values.Look(ref cabinAtmosphereInitialized, "fafAtmosphereInitialized", false);
        }
        public float InternalVacuum { get { return internalVacuum; } }
        internal void InitializeCabinEnvironment()
        {
            if (cabinAtmosphereInitialized) return;
            ExteriorReading exterior = ReadExterior(CabinetOccupancy.Read(this));
            if (!exterior.Known) return;
            internalVacuum = exterior.Vacuum;
            cabinAtmosphereInitialized = true;
        }

        private struct ExteriorReading
        {
            public bool Known;
            public float Temperature;
            public float Vacuum;
        }

        private ExteriorReading ReadExterior(CabinetOccupancySnapshot occupancy)
        {
            CabinetExteriorContext exterior = occupancy.Exterior;
            if (exterior?.Map != null && exterior.Cell.InBounds(exterior.Map))
                return new ExteriorReading {
                    Known = true,
                    Temperature = GenTemperature.GetTemperatureForCell(exterior.Cell, exterior.Map),
                    Vacuum = Integration.CabinetBedVacuumScope.ExteriorVacuumAt(exterior.Cell, exterior.Map)
                };
            if (exterior != null && exterior.Tile.Valid)
            {
                BiomeDef biome = Find.WorldGrid[exterior.Tile]?.PrimaryBiome;
                if (biome == null) return new ExteriorReading { Temperature = internalTemperature, Vacuum = 1f };
                bool space = exterior.InSpace || biome.inVacuum;
                // Keep the old surface cold-chain Info contract; only space needs an explicit biome exterior.
                // 保留旧地表冷链Info约定；空间航段才须使用明确的biome外界。
                float ambient = !space && occupancy.Payload != null ? AmbientTemperature
                    : biome.constantOutdoorTemperature ?? GenTemperature.GetTemperatureAtTile(exterior.Tile);
                return new ExteriorReading { Known = true, Temperature = ambient, Vacuum = biome.inVacuum ? 1f : 0f };
            }
            // Unknown is not a free 14 C / zero-vacuum life-support environment.
            // 未知环境不是免费的14°C/零真空生命保障环境。
            return new ExteriorReading { Temperature = internalTemperature, Vacuum = 1f };
        }

        private void AdvanceCabinAtmosphere(ExteriorReading exterior, float poweredTicks, int delta)
        {
            if (!cabinAtmosphereInitialized && exterior.Known)
            {
                // Existing saves retain their temperature but do not receive free pressurisation.
                // 旧存档保留内温，但不会免费获得加压。
                internalVacuum = exterior.Vacuum;
                cabinAtmosphereInitialized = true;
            }
            CompProperties_FridgeCabinetTemperature settings = TemperatureControl.ThermalProps;
            internalVacuum = CabinetAtmosphereModel.Advance(internalVacuum, exterior.Vacuum, poweredTicks,
                delta, settings.vacuumLeakTimeConstantTicks, settings.vacuumRecoveryTimeConstantTicks);
            lastLifeSupportPowered = poweredTicks >= delta && delta > 0;
        }

        /// <summary>Real occupants only; no fake CurrentBed, spawn state or duplicated pawn ticks. / 仅真实乘员；不伪造CurrentBed、生成状态或重复角色tick。</summary>
        public static bool TryGetCabinEnvironment(Pawn pawn, out float temperature, out float vacuum)
        {
            temperature = 0f; vacuum = 1f;
            if (pawn == null || pawn.Destroyed) return false;
            // A breached cabinet no longer supplies an internal environment; failed drops remain truly exposed.
            // 柜体破损后不再提供内界；未能放置的余物仍真正暴露于外界。
            if (pawn.ParentHolder is CabinetBreach breach && breach.Spawned
                && breach.GetDirectlyHeldThings().Contains(pawn))
            {
                temperature = GenTemperature.GetTemperatureForCell(breach.Position, breach.Map);
                vacuum = Integration.CabinetBedVacuumScope.ExteriorVacuumAt(breach.Position, breach.Map);
                return true;
            }
            if (!CabinetOccupancy.TryResolve(pawn, out CabinetOccupancySnapshot occupancy)) return false;
            Building_FlyingFridgeCabinet cabinet = occupancy.Cabinet;
            temperature = cabinet.InternalTemperature;
            vacuum = cabinet.InternalVacuum;
            return true;
        }
    }
}
