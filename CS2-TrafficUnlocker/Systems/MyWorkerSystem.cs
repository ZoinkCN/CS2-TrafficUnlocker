using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Triggers;
using Game;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Burst.Intrinsics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace TrafficUnlocker.Systems
{
    public partial class MyWorkerSystem : GameSystemBase
    {
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        public static float GetWorkOffset(Citizen citizen)
        {
            return (float)(-10922 + citizen.GetPseudoRandom(CitizenPseudoRandom.WorkOffset).NextInt(21845)) / 262144f;
        }

        public static bool IsTodayOffDay(Citizen citizen, ref EconomyParameterData economyParameters, uint frame, TimeData timeData, int population)
        {
            int num = math.min(Mod.m_Setting.WorkProbability, Mathf.RoundToInt(100f / math.max(1f, math.sqrt(Mod.m_Setting.GetRealTrafficReduction() * population))));
            int day = TimeSystem.GetDay(frame, timeData);
            return Unity.Mathematics.Random.CreateFromIndex((uint)((int)citizen.m_PseudoRandom + day)).NextInt(100) > num;
        }

        public static bool IsTimeToWork(Citizen citizen, Worker worker, ref EconomyParameterData economyParameters, float timeOfDay)
        {
            float2 timeToWork = MyWorkerSystem.GetTimeToWork(citizen, worker, ref economyParameters, true);
            if (timeToWork.x >= timeToWork.y)
            {
                return timeOfDay >= timeToWork.x || timeOfDay <= timeToWork.y;
            }
            return timeOfDay >= timeToWork.x && timeOfDay <= timeToWork.y;
        }

        public static float2 GetTimeToWork(Citizen citizen, Worker worker, ref EconomyParameterData economyParameters, bool includeCommute)
        {
            float num = MyWorkerSystem.GetWorkOffset(citizen);
            if (worker.m_Shift == Workshift.Evening)
            {
                num += 0.33f;
            }
            else if (worker.m_Shift == Workshift.Night)
            {
                num += 0.67f;
            }
            float num2 = math.frac((float)Mathf.RoundToInt(24f * (economyParameters.m_WorkDayStart + num)) / 24f);
            float y = math.frac((float)Mathf.RoundToInt(24f * (economyParameters.m_WorkDayEnd + num)) / 24f);
            float num3 = 0f;
            if (includeCommute)
            {
                num3 = 60f * worker.m_LastCommuteTime;
                if (num3 < 60f)
                {
                    num3 = 40000f;
                }
                num3 /= 262144f;
            }
            return new float2(math.frac(num2 - num3), y);
        }

        //[Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            this.m_CitizenBehaviorSystem = base.World.GetOrCreateSystemManaged<MyCitizenBehaviorSystem>();
            this.m_EndFrameBarrier = base.World.GetOrCreateSystemManaged<EndFrameBarrier>();
            this.m_TimeSystem = base.World.GetOrCreateSystemManaged<TimeSystem>();
            this.m_SimulationSystem = base.World.GetOrCreateSystemManaged<SimulationSystem>();
            this.m_TriggerSystem = base.World.GetOrCreateSystemManaged<TriggerSystem>();
            this.m_WorkerQuery = base.GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<Worker>(),
                ComponentType.ReadOnly<Citizen>(),
                ComponentType.ReadOnly<TravelPurpose>(),
                ComponentType.ReadOnly<CurrentBuilding>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>()
            });
            this.m_GotoWorkQuery = base.GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<Worker>(),
                ComponentType.ReadOnly<Citizen>(),
                ComponentType.ReadOnly<CurrentBuilding>(),
                ComponentType.Exclude<TravelPurpose>(),
                ComponentType.Exclude<HealthProblem>(),
                ComponentType.Exclude<ResourceBuyer>(),
                ComponentType.ReadWrite<TripNeeded>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>()
            });
            this.m_EconomyParameterQuery = base.GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<EconomyParameterData>()
            });
            this.m_TimeDataQuery = base.GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<TimeData>()
            });
            this.m_PopulationQuery = base.GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<Population>()
            });
            base.RequireAnyForUpdate(new EntityQuery[]
            {
                this.m_GotoWorkQuery,
                this.m_WorkerQuery
            });
            base.RequireForUpdate(this.m_EconomyParameterQuery);
        }

        //[Preserve]
        protected override void OnUpdate()
        {
            uint updateFrameWithInterval = SimulationUtils.GetUpdateFrameWithInterval(this.m_SimulationSystem.frameIndex, (uint)this.GetUpdateInterval(SystemUpdatePhase.GameSimulation), 16);
            this.__TypeHandle.__Game_City_Population_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_AttendingMeeting_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Objects_OutsideConnection_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Buildings_PropertyRenter_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_CarKeeper_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Buildings_Building_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Simulation_UpdateFrame_SharedComponentTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_TripNeeded_RW_BufferTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_Worker_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_Citizen_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Unity_Entities_Entity_TypeHandle.Update(ref base.CheckedStateRef);
            MyWorkerSystem.GoToWorkJob jobData = default(MyWorkerSystem.GoToWorkJob);
            jobData.m_EntityType = this.__TypeHandle.__Unity_Entities_Entity_TypeHandle;
            jobData.m_CitizenType = this.__TypeHandle.__Game_Citizens_Citizen_RW_ComponentTypeHandle;
            jobData.m_CurrentBuildingType = this.__TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentTypeHandle;
            jobData.m_WorkerType = this.__TypeHandle.__Game_Citizens_Worker_RO_ComponentTypeHandle;
            jobData.m_TripType = this.__TypeHandle.__Game_Citizens_TripNeeded_RW_BufferTypeHandle;
            jobData.m_UpdateFrameType = this.__TypeHandle.__Game_Simulation_UpdateFrame_SharedComponentTypeHandle;
            jobData.m_Buildings = this.__TypeHandle.__Game_Buildings_Building_RO_ComponentLookup;
            jobData.m_CarKeepers = this.__TypeHandle.__Game_Citizens_CarKeeper_RO_ComponentLookup;
            jobData.m_Properties = this.__TypeHandle.__Game_Buildings_PropertyRenter_RO_ComponentLookup;
            jobData.m_OutsideConnections = this.__TypeHandle.__Game_Objects_OutsideConnection_RO_ComponentLookup;
            jobData.m_Purposes = this.__TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentLookup;
            jobData.m_Attendings = this.__TypeHandle.__Game_Citizens_AttendingMeeting_RO_ComponentLookup;
            jobData.m_PopulationData = this.__TypeHandle.__Game_City_Population_RO_ComponentLookup;
            jobData.m_TriggerBuffer = this.m_TriggerSystem.CreateActionBuffer().AsParallelWriter();
            jobData.m_EconomyParameters = this.m_EconomyParameterQuery.GetSingleton<EconomyParameterData>();
            jobData.m_TimeOfDay = this.m_TimeSystem.normalizedTime;
            jobData.m_UpdateFrameIndex = updateFrameWithInterval;
            jobData.m_Frame = this.m_SimulationSystem.frameIndex;
            jobData.m_TimeData = this.m_TimeDataQuery.GetSingleton<TimeData>();
            jobData.m_PopulationEntity = this.m_PopulationQuery.GetSingletonEntity();
            JobHandle job;
            jobData.m_CarReserverQueue = this.m_CitizenBehaviorSystem.GetCarReserverQueue(out job);
            jobData.m_CommandBuffer = this.m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter();
            JobHandle jobHandle = jobData.ScheduleParallel(this.m_GotoWorkQuery, JobHandle.CombineDependencies(base.Dependency, job));
            this.m_EndFrameBarrier.AddJobHandleForProducer(jobHandle);
            this.m_CitizenBehaviorSystem.AddCarReserveWriter(jobHandle);
            this.m_TriggerSystem.AddActionBufferWriter(jobHandle);
            this.__TypeHandle.__Game_Companies_WorkProvider_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_AttendingMeeting_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_Citizen_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Simulation_UpdateFrame_SharedComponentTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_Worker_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Unity_Entities_Entity_TypeHandle.Update(ref base.CheckedStateRef);
            MyWorkerSystem.WorkJob jobData2 = default(MyWorkerSystem.WorkJob);
            jobData2.m_EntityType = this.__TypeHandle.__Unity_Entities_Entity_TypeHandle;
            jobData2.m_WorkerType = this.__TypeHandle.__Game_Citizens_Worker_RO_ComponentTypeHandle;
            jobData2.m_PurposeType = this.__TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentTypeHandle;
            jobData2.m_UpdateFrameType = this.__TypeHandle.__Game_Simulation_UpdateFrame_SharedComponentTypeHandle;
            jobData2.m_CitizenType = this.__TypeHandle.__Game_Citizens_Citizen_RW_ComponentTypeHandle;
            jobData2.m_Attendings = this.__TypeHandle.__Game_Citizens_AttendingMeeting_RO_ComponentLookup;
            jobData2.m_Workplaces = this.__TypeHandle.__Game_Companies_WorkProvider_RO_ComponentLookup;
            jobData2.m_TriggerBuffer = this.m_TriggerSystem.CreateActionBuffer().AsParallelWriter();
            jobData2.m_EconomyParameters = this.m_EconomyParameterQuery.GetSingleton<EconomyParameterData>();
            jobData2.m_UpdateFrameIndex = updateFrameWithInterval;
            jobData2.m_TimeOfDay = this.m_TimeSystem.normalizedTime;
            jobData2.m_Frame = this.m_SimulationSystem.frameIndex;
            jobData2.m_TimeData = this.m_TimeDataQuery.GetSingleton<TimeData>();
            jobData2.m_CommandBuffer = this.m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter();
            JobHandle jobHandle2 = jobData2.ScheduleParallel(this.m_WorkerQuery, JobHandle.CombineDependencies(base.Dependency, jobHandle));
            this.m_EndFrameBarrier.AddJobHandleForProducer(jobHandle2);
            this.m_TriggerSystem.AddActionBufferWriter(jobHandle2);
            base.Dependency = jobHandle2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void __AssignQueries(ref SystemState state)
        {
        }

        protected override void OnCreateForCompiler()
        {
            base.OnCreateForCompiler();
            this.__AssignQueries(ref base.CheckedStateRef);
            this.__TypeHandle.__AssignHandles(ref base.CheckedStateRef);
        }

        //[Preserve]
        public MyWorkerSystem()
        {
        }

        private EndFrameBarrier m_EndFrameBarrier;

        private TimeSystem m_TimeSystem;

        private MyCitizenBehaviorSystem m_CitizenBehaviorSystem;

        private EntityQuery m_EconomyParameterQuery;

        private EntityQuery m_GotoWorkQuery;

        private EntityQuery m_WorkerQuery;

        private EntityQuery m_TimeDataQuery;

        private EntityQuery m_PopulationQuery;

        private SimulationSystem m_SimulationSystem;

        private TriggerSystem m_TriggerSystem;

        private MyWorkerSystem.TypeHandle __TypeHandle;

        //[BurstCompile]
        private struct GoToWorkJob : IJobChunk
        {
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (chunk.GetSharedComponent<UpdateFrame>(this.m_UpdateFrameType).m_Index != this.m_UpdateFrameIndex)
                {
                    return;
                }
                NativeArray<Entity> nativeArray = chunk.GetNativeArray(this.m_EntityType);
                NativeArray<Citizen> nativeArray2 = chunk.GetNativeArray<Citizen>(ref this.m_CitizenType);
                NativeArray<Worker> nativeArray3 = chunk.GetNativeArray<Worker>(ref this.m_WorkerType);
                NativeArray<CurrentBuilding> nativeArray4 = chunk.GetNativeArray<CurrentBuilding>(ref this.m_CurrentBuildingType);
                BufferAccessor<TripNeeded> bufferAccessor = chunk.GetBufferAccessor<TripNeeded>(ref this.m_TripType);
                int population = this.m_PopulationData[this.m_PopulationEntity].m_Population;
                for (int i = 0; i < nativeArray.Length; i++)
                {
                    Entity entity = nativeArray[i];
                    Citizen citizen = nativeArray2[i];
                    if (!MyWorkerSystem.IsTodayOffDay(citizen, ref this.m_EconomyParameters, this.m_Frame, this.m_TimeData, population) && MyWorkerSystem.IsTimeToWork(citizen, nativeArray3[i], ref this.m_EconomyParameters, this.m_TimeOfDay))
                    {
                        DynamicBuffer<TripNeeded> dynamicBuffer = bufferAccessor[i];
                        if (!this.m_Attendings.HasComponent(entity) && (citizen.m_State & CitizenFlags.MovingAway) == CitizenFlags.None)
                        {
                            Entity workplace = nativeArray3[i].m_Workplace;
                            Entity entity2 = Entity.Null;
                            if (this.m_Properties.HasComponent(workplace))
                            {
                                entity2 = this.m_Properties[workplace].m_Property;
                            }
                            else if (this.m_Buildings.HasComponent(workplace))
                            {
                                entity2 = workplace;
                            }
                            else if (this.m_OutsideConnections.HasComponent(workplace))
                            {
                                entity2 = workplace;
                            }
                            if (entity2 != Entity.Null)
                            {
                                if (nativeArray4[i].m_CurrentBuilding != entity2)
                                {
                                    if (!this.m_CarKeepers.IsComponentEnabled(entity))
                                    {
                                        this.m_CarReserverQueue.Enqueue(entity);
                                    }
                                    dynamicBuffer.Add(new TripNeeded
                                    {
                                        m_TargetAgent = workplace,
                                        m_Purpose = Purpose.GoingToWork
                                    });
                                }
                            }
                            else
                            {
                                citizen.SetFailedEducationCount(0);
                                nativeArray2[i] = citizen;
                                if (this.m_Purposes.HasComponent(entity) && (this.m_Purposes[entity].m_Purpose == Purpose.GoingToWork || this.m_Purposes[entity].m_Purpose == Purpose.Working))
                                {
                                    this.m_CommandBuffer.RemoveComponent<TravelPurpose>(unfilteredChunkIndex, entity);
                                }
                                this.m_CommandBuffer.RemoveComponent<Worker>(unfilteredChunkIndex, entity);
                                this.m_TriggerBuffer.Enqueue(new TriggerAction(TriggerType.CitizenBecameUnemployed, Entity.Null, entity, workplace, 0f));
                            }
                        }
                    }
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                this.Execute(chunk, unfilteredChunkIndex, useEnabledMask, chunkEnabledMask);
            }

            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            public ComponentTypeHandle<Citizen> m_CitizenType;

            [ReadOnly]
            public ComponentTypeHandle<Worker> m_WorkerType;

            [ReadOnly]
            public ComponentTypeHandle<CurrentBuilding> m_CurrentBuildingType;

            public BufferTypeHandle<TripNeeded> m_TripType;

            [ReadOnly]
            public SharedComponentTypeHandle<UpdateFrame> m_UpdateFrameType;

            [ReadOnly]
            public ComponentLookup<TravelPurpose> m_Purposes;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> m_Properties;

            [ReadOnly]
            public ComponentLookup<Building> m_Buildings;

            [ReadOnly]
            public ComponentLookup<CarKeeper> m_CarKeepers;

            [ReadOnly]
            public ComponentLookup<Game.Objects.OutsideConnection> m_OutsideConnections;

            [ReadOnly]
            public ComponentLookup<AttendingMeeting> m_Attendings;

            [ReadOnly]
            public ComponentLookup<Population> m_PopulationData;

            public NativeQueue<TriggerAction>.ParallelWriter m_TriggerBuffer;

            public uint m_Frame;

            public TimeData m_TimeData;

            public uint m_UpdateFrameIndex;

            public float m_TimeOfDay;

            public Entity m_PopulationEntity;

            public EconomyParameterData m_EconomyParameters;

            public NativeQueue<Entity>.ParallelWriter m_CarReserverQueue;

            public EntityCommandBuffer.ParallelWriter m_CommandBuffer;
        }

        //[BurstCompile]
        private struct WorkJob : IJobChunk
        {
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (chunk.GetSharedComponent<UpdateFrame>(this.m_UpdateFrameType).m_Index != this.m_UpdateFrameIndex)
                {
                    return;
                }
                NativeArray<Entity> nativeArray = chunk.GetNativeArray(this.m_EntityType);
                NativeArray<Worker> nativeArray2 = chunk.GetNativeArray<Worker>(ref this.m_WorkerType);
                NativeArray<TravelPurpose> nativeArray3 = chunk.GetNativeArray<TravelPurpose>(ref this.m_PurposeType);
                NativeArray<Citizen> nativeArray4 = chunk.GetNativeArray<Citizen>(ref this.m_CitizenType);
                for (int i = 0; i < nativeArray.Length; i++)
                {
                    Entity entity = nativeArray[i];
                    Entity workplace = nativeArray2[i].m_Workplace;
                    Worker worker = nativeArray2[i];
                    Citizen citizen = nativeArray4[i];
                    if (!this.m_Workplaces.HasComponent(workplace))
                    {
                        citizen.SetFailedEducationCount(0);
                        nativeArray4[i] = citizen;
                        TravelPurpose travelPurpose = nativeArray3[i];
                        if (travelPurpose.m_Purpose == Purpose.GoingToWork || travelPurpose.m_Purpose == Purpose.Working)
                        {
                            this.m_CommandBuffer.RemoveComponent<TravelPurpose>(unfilteredChunkIndex, entity);
                        }
                        this.m_CommandBuffer.RemoveComponent<Worker>(unfilteredChunkIndex, entity);
                        this.m_TriggerBuffer.Enqueue(new TriggerAction(TriggerType.CitizenBecameUnemployed, Entity.Null, entity, workplace, 0f));
                    }
                    else if ((!MyWorkerSystem.IsTimeToWork(citizen, worker, ref this.m_EconomyParameters, this.m_TimeOfDay) || this.m_Attendings.HasComponent(entity)) && nativeArray3[i].m_Purpose == Purpose.Working)
                    {
                        this.m_CommandBuffer.RemoveComponent<TravelPurpose>(unfilteredChunkIndex, entity);
                    }
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                this.Execute(chunk, unfilteredChunkIndex, useEnabledMask, chunkEnabledMask);
            }

            [ReadOnly]
            public ComponentTypeHandle<Worker> m_WorkerType;

            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public ComponentTypeHandle<TravelPurpose> m_PurposeType;

            [ReadOnly]
            public SharedComponentTypeHandle<UpdateFrame> m_UpdateFrameType;

            public ComponentTypeHandle<Citizen> m_CitizenType;

            [ReadOnly]
            public ComponentLookup<WorkProvider> m_Workplaces;

            [ReadOnly]
            public ComponentLookup<AttendingMeeting> m_Attendings;

            public EconomyParameterData m_EconomyParameters;

            public NativeQueue<TriggerAction>.ParallelWriter m_TriggerBuffer;

            public float m_TimeOfDay;

            public uint m_UpdateFrameIndex;

            public uint m_Frame;

            public TimeData m_TimeData;

            public EntityCommandBuffer.ParallelWriter m_CommandBuffer;
        }

        private struct TypeHandle
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void __AssignHandles(ref SystemState state)
            {
                this.__Unity_Entities_Entity_TypeHandle = state.GetEntityTypeHandle();
                this.__Game_Citizens_Citizen_RW_ComponentTypeHandle = state.GetComponentTypeHandle<Citizen>(false);
                this.__Game_Citizens_CurrentBuilding_RO_ComponentTypeHandle = state.GetComponentTypeHandle<CurrentBuilding>(true);
                this.__Game_Citizens_Worker_RO_ComponentTypeHandle = state.GetComponentTypeHandle<Worker>(true);
                this.__Game_Citizens_TripNeeded_RW_BufferTypeHandle = state.GetBufferTypeHandle<TripNeeded>(false);
                this.__Game_Simulation_UpdateFrame_SharedComponentTypeHandle = state.GetSharedComponentTypeHandle<UpdateFrame>();
                this.__Game_Buildings_Building_RO_ComponentLookup = state.GetComponentLookup<Building>(true);
                this.__Game_Citizens_CarKeeper_RO_ComponentLookup = state.GetComponentLookup<CarKeeper>(true);
                this.__Game_Buildings_PropertyRenter_RO_ComponentLookup = state.GetComponentLookup<PropertyRenter>(true);
                this.__Game_Objects_OutsideConnection_RO_ComponentLookup = state.GetComponentLookup<Game.Objects.OutsideConnection>(true);
                this.__Game_Citizens_TravelPurpose_RO_ComponentLookup = state.GetComponentLookup<TravelPurpose>(true);
                this.__Game_Citizens_AttendingMeeting_RO_ComponentLookup = state.GetComponentLookup<AttendingMeeting>(true);
                this.__Game_City_Population_RO_ComponentLookup = state.GetComponentLookup<Population>(true);
                this.__Game_Citizens_TravelPurpose_RO_ComponentTypeHandle = state.GetComponentTypeHandle<TravelPurpose>(true);
                this.__Game_Companies_WorkProvider_RO_ComponentLookup = state.GetComponentLookup<WorkProvider>(true);
            }

            [ReadOnly]
            public EntityTypeHandle __Unity_Entities_Entity_TypeHandle;

            public ComponentTypeHandle<Citizen> __Game_Citizens_Citizen_RW_ComponentTypeHandle;

            [ReadOnly]
            public ComponentTypeHandle<CurrentBuilding> __Game_Citizens_CurrentBuilding_RO_ComponentTypeHandle;

            [ReadOnly]
            public ComponentTypeHandle<Worker> __Game_Citizens_Worker_RO_ComponentTypeHandle;

            public BufferTypeHandle<TripNeeded> __Game_Citizens_TripNeeded_RW_BufferTypeHandle;

            public SharedComponentTypeHandle<UpdateFrame> __Game_Simulation_UpdateFrame_SharedComponentTypeHandle;

            [ReadOnly]
            public ComponentLookup<Building> __Game_Buildings_Building_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<CarKeeper> __Game_Citizens_CarKeeper_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> __Game_Buildings_PropertyRenter_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Game.Objects.OutsideConnection> __Game_Objects_OutsideConnection_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<TravelPurpose> __Game_Citizens_TravelPurpose_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<AttendingMeeting> __Game_Citizens_AttendingMeeting_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Population> __Game_City_Population_RO_ComponentLookup;

            [ReadOnly]
            public ComponentTypeHandle<TravelPurpose> __Game_Citizens_TravelPurpose_RO_ComponentTypeHandle;

            [ReadOnly]
            public ComponentLookup<WorkProvider> __Game_Companies_WorkProvider_RO_ComponentLookup;
        }
    }
}
