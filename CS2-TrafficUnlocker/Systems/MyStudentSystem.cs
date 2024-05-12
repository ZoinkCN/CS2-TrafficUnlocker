using Game;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using System.Runtime.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace TrafficUnlocker.Systems
{
    public partial class MyStudentSystem : GameSystemBase
    {
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        public static float GetStudyOffset(Citizen citizen)
        {
            return (-10922 + citizen.GetPseudoRandom(CitizenPseudoRandom.WorkOffset).NextInt(21845)) / 262144f;
        }

        public static bool IsTimeToStudy(Citizen citizen, Game.Citizens.Student student, ref EconomyParameterData economyParameters, float timeOfDay, uint frame, TimeData timeData, int population)
        {
            int num = math.min(Mod.m_Setting.SchoolProbability, Mathf.RoundToInt(100f / math.max(1f, math.sqrt(Mod.m_Setting.GetRealTrafficReduction() * population))));
            int day = TimeSystem.GetDay(frame, timeData);
            float2 timeToStudy = GetTimeToStudy(citizen, student, ref economyParameters);
            if (Unity.Mathematics.Random.CreateFromIndex((uint)(citizen.m_PseudoRandom + day)).NextInt(100) > num)
            {
                return false;
            }
            if (timeToStudy.x >= timeToStudy.y)
            {
                return timeOfDay >= timeToStudy.x || timeOfDay <= timeToStudy.y;
            }
            return timeOfDay >= timeToStudy.x && timeOfDay <= timeToStudy.y;
        }

        public static float2 GetTimeToStudy(Citizen citizen, Game.Citizens.Student student, ref EconomyParameterData economyParameters)
        {
            float studyOffset = GetStudyOffset(citizen);
            float num = 60f * student.m_LastCommuteTime;
            if (num < 60f)
            {
                num = 1800f;
            }
            num /= 262144f;
            return new float2(math.frac(economyParameters.m_WorkDayStart + studyOffset - num), math.frac(economyParameters.m_WorkDayEnd + studyOffset));
        }

        //[Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_CitizenBehaviorSystem = World.GetOrCreateSystemManaged<CitizenBehaviorSystem>();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_StudentQuery = GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<Game.Citizens.Student>(),
                ComponentType.ReadOnly<Citizen>(),
                ComponentType.ReadOnly<TravelPurpose>(),
                ComponentType.ReadOnly<CurrentBuilding>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>()
            });
            m_GotoSchoolQuery = GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<Game.Citizens.Student>(),
                ComponentType.ReadOnly<Citizen>(),
                ComponentType.ReadOnly<CurrentBuilding>(),
                ComponentType.Exclude<ResourceBuyer>(),
                ComponentType.Exclude<TravelPurpose>(),
                ComponentType.Exclude<HealthProblem>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>()
            });
            m_EconomyParameterQuery = GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<EconomyParameterData>()
            });
            m_TimeDataQuery = GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<TimeData>()
            });
            m_PopulationQuery = GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<Population>()
            });
            RequireAnyForUpdate(new EntityQuery[]
            {
                m_StudentQuery,
                m_GotoSchoolQuery
            });
            RequireForUpdate(m_EconomyParameterQuery);
        }

        //[Preserve]
        protected override void OnUpdate()
        {
            __TypeHandle.__Game_City_Population_RO_ComponentLookup.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Citizens_AttendingMeeting_RO_ComponentLookup.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Objects_OutsideConnection_RO_ComponentLookup.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Buildings_PropertyRenter_RO_ComponentLookup.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Citizens_CarKeeper_RO_ComponentLookup.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Buildings_Building_RO_ComponentLookup.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentLookup.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Citizens_TripNeeded_RW_BufferTypeHandle.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Citizens_Student_RO_ComponentTypeHandle.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentTypeHandle.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Citizens_Citizen_RO_ComponentTypeHandle.Update(ref CheckedStateRef);
            __TypeHandle.__Unity_Entities_Entity_TypeHandle.Update(ref CheckedStateRef);
            GoToSchoolJob jobData = default;
            jobData.m_EntityType = __TypeHandle.__Unity_Entities_Entity_TypeHandle;
            jobData.m_CitizenType = __TypeHandle.__Game_Citizens_Citizen_RO_ComponentTypeHandle;
            jobData.m_CurrentBuildingType = __TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentTypeHandle;
            jobData.m_StudentType = __TypeHandle.__Game_Citizens_Student_RO_ComponentTypeHandle;
            jobData.m_TripType = __TypeHandle.__Game_Citizens_TripNeeded_RW_BufferTypeHandle;
            jobData.m_Purposes = __TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentLookup;
            jobData.m_Buildings = __TypeHandle.__Game_Buildings_Building_RO_ComponentLookup;
            jobData.m_CarKeepers = __TypeHandle.__Game_Citizens_CarKeeper_RO_ComponentLookup;
            jobData.m_Properties = __TypeHandle.__Game_Buildings_PropertyRenter_RO_ComponentLookup;
            jobData.m_OutsideConnections = __TypeHandle.__Game_Objects_OutsideConnection_RO_ComponentLookup;
            jobData.m_Attendings = __TypeHandle.__Game_Citizens_AttendingMeeting_RO_ComponentLookup;
            jobData.m_PopulationData = __TypeHandle.__Game_City_Population_RO_ComponentLookup;
            jobData.m_EconomyParameters = m_EconomyParameterQuery.GetSingleton<EconomyParameterData>();
            jobData.m_TimeOfDay = m_TimeSystem.normalizedTime;
            jobData.m_Frame = m_SimulationSystem.frameIndex;
            jobData.m_PopulationEntity = m_PopulationQuery.GetSingletonEntity();
            jobData.m_TimeData = m_TimeDataQuery.GetSingleton<TimeData>();
            JobHandle job;
            jobData.m_CarReserverQueue = m_CitizenBehaviorSystem.GetCarReserverQueue(out job);
            jobData.m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter();
            JobHandle jobHandle = jobData.ScheduleParallel(m_GotoSchoolQuery, JobHandle.CombineDependencies(Dependency, job));
            m_EndFrameBarrier.AddJobHandleForProducer(jobHandle);
            m_CitizenBehaviorSystem.AddCarReserveWriter(jobHandle);
            __TypeHandle.__Game_Buildings_School_RO_ComponentLookup.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Common_Target_RO_ComponentLookup.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentLookup.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Citizens_AttendingMeeting_RO_ComponentLookup.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Citizens_Citizen_RO_ComponentTypeHandle.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentTypeHandle.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Citizens_Student_RO_ComponentTypeHandle.Update(ref CheckedStateRef);
            __TypeHandle.__Unity_Entities_Entity_TypeHandle.Update(ref CheckedStateRef);
            StudyJob jobData2 = default;
            jobData2.m_EntityType = __TypeHandle.__Unity_Entities_Entity_TypeHandle;
            jobData2.m_StudentType = __TypeHandle.__Game_Citizens_Student_RO_ComponentTypeHandle;
            jobData2.m_PurposeType = __TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentTypeHandle;
            jobData2.m_CitizenType = __TypeHandle.__Game_Citizens_Citizen_RO_ComponentTypeHandle;
            jobData2.m_Attendings = __TypeHandle.__Game_Citizens_AttendingMeeting_RO_ComponentLookup;
            jobData2.m_CurrentBuildings = __TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentLookup;
            jobData2.m_Targets = __TypeHandle.__Game_Common_Target_RO_ComponentLookup;
            jobData2.m_Schools = __TypeHandle.__Game_Buildings_School_RO_ComponentLookup;
            jobData2.m_EconomyParameters = m_EconomyParameterQuery.GetSingleton<EconomyParameterData>();
            jobData2.m_TimeOfDay = m_TimeSystem.normalizedTime;
            jobData2.m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter();
            JobHandle jobHandle2 = jobData2.ScheduleParallel(m_StudentQuery, JobHandle.CombineDependencies(Dependency, jobHandle));
            m_EndFrameBarrier.AddJobHandleForProducer(jobHandle2);
            Dependency = jobHandle2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void __AssignQueries(ref SystemState state)
        {
        }

        protected override void OnCreateForCompiler()
        {
            base.OnCreateForCompiler();
            __AssignQueries(ref CheckedStateRef);
            __TypeHandle.__AssignHandles(ref CheckedStateRef);
        }

        //[Preserve]
        public MyStudentSystem()
        {
        }

        private EndFrameBarrier m_EndFrameBarrier;

        private TimeSystem m_TimeSystem;

        private CitizenBehaviorSystem m_CitizenBehaviorSystem;

        private SimulationSystem m_SimulationSystem;

        private EntityQuery m_EconomyParameterQuery;

        private EntityQuery m_GotoSchoolQuery;

        private EntityQuery m_StudentQuery;

        private EntityQuery m_TimeDataQuery;

        private EntityQuery m_PopulationQuery;

        private TypeHandle __TypeHandle;

        //[BurstCompile]
        private struct GoToSchoolJob : IJobChunk
        {
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> nativeArray = chunk.GetNativeArray(m_EntityType);
                NativeArray<Citizen> nativeArray2 = chunk.GetNativeArray(ref m_CitizenType);
                NativeArray<Game.Citizens.Student> nativeArray3 = chunk.GetNativeArray(ref m_StudentType);
                NativeArray<CurrentBuilding> nativeArray4 = chunk.GetNativeArray(ref m_CurrentBuildingType);
                BufferAccessor<TripNeeded> bufferAccessor = chunk.GetBufferAccessor(ref m_TripType);
                int population = m_PopulationData[m_PopulationEntity].m_Population;
                for (int i = 0; i < nativeArray.Length; i++)
                {
                    Entity entity = nativeArray[i];
                    Citizen citizen = nativeArray2[i];
                    if (IsTimeToStudy(citizen, nativeArray3[i], ref m_EconomyParameters, m_TimeOfDay, m_Frame, m_TimeData, population))
                    {
                        DynamicBuffer<TripNeeded> dynamicBuffer = bufferAccessor[i];
                        if (!m_Attendings.HasComponent(entity) && (citizen.m_State & CitizenFlags.MovingAway) == CitizenFlags.None)
                        {
                            Entity school = nativeArray3[i].m_School;
                            Entity entity2 = Entity.Null;
                            if (m_Properties.HasComponent(school))
                            {
                                entity2 = m_Properties[school].m_Property;
                            }
                            else if (m_Buildings.HasComponent(school) || m_OutsideConnections.HasComponent(school))
                            {
                                entity2 = school;
                            }
                            if (entity2 != Entity.Null)
                            {
                                if (nativeArray4[i].m_CurrentBuilding != entity2)
                                {
                                    if (!m_CarKeepers.IsComponentEnabled(entity))
                                    {
                                        m_CarReserverQueue.Enqueue(entity);
                                    }
                                    dynamicBuffer.Add(new TripNeeded
                                    {
                                        m_TargetAgent = school,
                                        m_Purpose = Purpose.GoingToSchool
                                    });
                                }
                            }
                            else
                            {
                                if (m_Purposes.HasComponent(entity) && (m_Purposes[entity].m_Purpose == Purpose.Studying || m_Purposes[entity].m_Purpose == Purpose.GoingToSchool))
                                {
                                    m_CommandBuffer.RemoveComponent<TravelPurpose>(unfilteredChunkIndex, entity);
                                }
                                m_CommandBuffer.AddComponent<StudentsRemoved>(unfilteredChunkIndex, school);
                                m_CommandBuffer.RemoveComponent<Game.Citizens.Student>(unfilteredChunkIndex, entity);
                            }
                        }
                    }
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(chunk, unfilteredChunkIndex, useEnabledMask, chunkEnabledMask);
            }

            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public ComponentTypeHandle<Citizen> m_CitizenType;

            [ReadOnly]
            public ComponentTypeHandle<Game.Citizens.Student> m_StudentType;

            [ReadOnly]
            public ComponentTypeHandle<CurrentBuilding> m_CurrentBuildingType;

            public BufferTypeHandle<TripNeeded> m_TripType;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> m_Properties;

            [ReadOnly]
            public ComponentLookup<Building> m_Buildings;

            [ReadOnly]
            public ComponentLookup<CarKeeper> m_CarKeepers;

            [ReadOnly]
            public ComponentLookup<TravelPurpose> m_Purposes;

            [ReadOnly]
            public ComponentLookup<Game.Objects.OutsideConnection> m_OutsideConnections;

            [ReadOnly]
            public ComponentLookup<AttendingMeeting> m_Attendings;

            [ReadOnly]
            public ComponentLookup<Population> m_PopulationData;

            public float m_TimeOfDay;

            public uint m_Frame;

            public TimeData m_TimeData;

            public Entity m_PopulationEntity;

            public EconomyParameterData m_EconomyParameters;

            public NativeQueue<Entity>.ParallelWriter m_CarReserverQueue;

            public EntityCommandBuffer.ParallelWriter m_CommandBuffer;
        }

        //[BurstCompile]
        private struct StudyJob : IJobChunk
        {
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> nativeArray = chunk.GetNativeArray(m_EntityType);
                NativeArray<Game.Citizens.Student> nativeArray2 = chunk.GetNativeArray(ref m_StudentType);
                NativeArray<TravelPurpose> nativeArray3 = chunk.GetNativeArray(ref m_PurposeType);
                NativeArray<Citizen> nativeArray4 = chunk.GetNativeArray(ref m_CitizenType);
                for (int i = 0; i < nativeArray.Length; i++)
                {
                    Entity entity = nativeArray[i];
                    Entity school = nativeArray2[i].m_School;
                    float studyOffset = GetStudyOffset(nativeArray4[i]);
                    if (!m_Schools.HasComponent(school))
                    {
                        TravelPurpose travelPurpose = nativeArray3[i];
                        if (travelPurpose.m_Purpose == Purpose.GoingToSchool || travelPurpose.m_Purpose == Purpose.Studying)
                        {
                            m_CommandBuffer.RemoveComponent<TravelPurpose>(unfilteredChunkIndex, entity);
                        }
                        m_CommandBuffer.RemoveComponent<Game.Citizens.Student>(unfilteredChunkIndex, entity);
                    }
                    else if (!m_Targets.HasComponent(entity) && m_CurrentBuildings.HasComponent(entity) && m_CurrentBuildings[entity].m_CurrentBuilding != school)
                    {
                        if (nativeArray3[i].m_Purpose == Purpose.Studying)
                        {
                            m_CommandBuffer.RemoveComponent<TravelPurpose>(unfilteredChunkIndex, entity);
                        }
                    }
                    else if ((m_TimeOfDay > m_EconomyParameters.m_WorkDayEnd + studyOffset || m_TimeOfDay < m_EconomyParameters.m_WorkDayStart + studyOffset || m_Attendings.HasComponent(entity)) && nativeArray3[i].m_Purpose == Purpose.Studying)
                    {
                        m_CommandBuffer.RemoveComponent<TravelPurpose>(unfilteredChunkIndex, entity);
                    }
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(chunk, unfilteredChunkIndex, useEnabledMask, chunkEnabledMask);
            }

            [ReadOnly]
            public ComponentTypeHandle<Game.Citizens.Student> m_StudentType;

            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public ComponentTypeHandle<TravelPurpose> m_PurposeType;

            [ReadOnly]
            public ComponentTypeHandle<Citizen> m_CitizenType;

            [ReadOnly]
            public ComponentLookup<Game.Buildings.School> m_Schools;

            [ReadOnly]
            public ComponentLookup<Target> m_Targets;

            [ReadOnly]
            public ComponentLookup<CurrentBuilding> m_CurrentBuildings;

            [ReadOnly]
            public ComponentLookup<AttendingMeeting> m_Attendings;

            public EconomyParameterData m_EconomyParameters;

            public float m_TimeOfDay;

            public EntityCommandBuffer.ParallelWriter m_CommandBuffer;
        }

        private struct TypeHandle
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void __AssignHandles(ref SystemState state)
            {
                __Unity_Entities_Entity_TypeHandle = state.GetEntityTypeHandle();
                __Game_Citizens_Citizen_RO_ComponentTypeHandle = state.GetComponentTypeHandle<Citizen>(true);
                __Game_Citizens_CurrentBuilding_RO_ComponentTypeHandle = state.GetComponentTypeHandle<CurrentBuilding>(true);
                __Game_Citizens_Student_RO_ComponentTypeHandle = state.GetComponentTypeHandle<Game.Citizens.Student>(true);
                __Game_Citizens_TripNeeded_RW_BufferTypeHandle = state.GetBufferTypeHandle<TripNeeded>(false);
                __Game_Citizens_TravelPurpose_RO_ComponentLookup = state.GetComponentLookup<TravelPurpose>(true);
                __Game_Buildings_Building_RO_ComponentLookup = state.GetComponentLookup<Building>(true);
                __Game_Citizens_CarKeeper_RO_ComponentLookup = state.GetComponentLookup<CarKeeper>(true);
                __Game_Buildings_PropertyRenter_RO_ComponentLookup = state.GetComponentLookup<PropertyRenter>(true);
                __Game_Objects_OutsideConnection_RO_ComponentLookup = state.GetComponentLookup<Game.Objects.OutsideConnection>(true);
                __Game_Citizens_AttendingMeeting_RO_ComponentLookup = state.GetComponentLookup<AttendingMeeting>(true);
                __Game_City_Population_RO_ComponentLookup = state.GetComponentLookup<Population>(true);
                __Game_Citizens_TravelPurpose_RO_ComponentTypeHandle = state.GetComponentTypeHandle<TravelPurpose>(true);
                __Game_Citizens_CurrentBuilding_RO_ComponentLookup = state.GetComponentLookup<CurrentBuilding>(true);
                __Game_Common_Target_RO_ComponentLookup = state.GetComponentLookup<Target>(true);
                __Game_Buildings_School_RO_ComponentLookup = state.GetComponentLookup<Game.Buildings.School>(true);
            }

            [ReadOnly]
            public EntityTypeHandle __Unity_Entities_Entity_TypeHandle;

            [ReadOnly]
            public ComponentTypeHandle<Citizen> __Game_Citizens_Citizen_RO_ComponentTypeHandle;

            [ReadOnly]
            public ComponentTypeHandle<CurrentBuilding> __Game_Citizens_CurrentBuilding_RO_ComponentTypeHandle;

            [ReadOnly]
            public ComponentTypeHandle<Game.Citizens.Student> __Game_Citizens_Student_RO_ComponentTypeHandle;

            public BufferTypeHandle<TripNeeded> __Game_Citizens_TripNeeded_RW_BufferTypeHandle;

            [ReadOnly]
            public ComponentLookup<TravelPurpose> __Game_Citizens_TravelPurpose_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Building> __Game_Buildings_Building_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<CarKeeper> __Game_Citizens_CarKeeper_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> __Game_Buildings_PropertyRenter_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Game.Objects.OutsideConnection> __Game_Objects_OutsideConnection_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<AttendingMeeting> __Game_Citizens_AttendingMeeting_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Population> __Game_City_Population_RO_ComponentLookup;

            [ReadOnly]
            public ComponentTypeHandle<TravelPurpose> __Game_Citizens_TravelPurpose_RO_ComponentTypeHandle;

            [ReadOnly]
            public ComponentLookup<CurrentBuilding> __Game_Citizens_CurrentBuilding_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Target> __Game_Common_Target_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Game.Buildings.School> __Game_Buildings_School_RO_ComponentLookup;
        }
    }
}
