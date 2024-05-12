using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Events;
using Game.Pathfind;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
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
using Game.Economy;

namespace TrafficUnlocker.Systems
{
    public partial class MyCitizenBehaviorSystem : GameSystemBase
    {
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        public override int GetUpdateOffset(SystemUpdatePhase phase)
        {
            return 11;
        }

        public static float2 GetSleepTime(Entity entity, Citizen citizen, ref EconomyParameterData economyParameters, ref ComponentLookup<Worker> workers, ref ComponentLookup<Game.Citizens.Student> students)
        {
            CitizenAge age = citizen.GetAge();
            float2 @float = new float2(0.875f, 0.175f);
            float num = @float.y - @float.x;
            Unity.Mathematics.Random pseudoRandom = citizen.GetPseudoRandom(CitizenPseudoRandom.SleepOffset);
            @float += pseudoRandom.NextFloat(0f, 0.2f);
            if (age == CitizenAge.Elderly)
            {
                @float -= 0.05f;
            }
            if (age == CitizenAge.Child)
            {
                @float -= 0.1f;
            }
            if (age == CitizenAge.Teen)
            {
                @float += 0.05f;
            }
            @float = math.frac(@float);
            float2 float2;
            if (workers.HasComponent(entity))
            {
                float2 = WorkerSystem.GetTimeToWork(citizen, workers[entity], ref economyParameters, true);
            }
            else
            {
                if (!students.HasComponent(entity))
                {
                    return @float;
                }
                float2 = StudentSystem.GetTimeToStudy(citizen, students[entity], ref economyParameters);
            }
            if (float2.x < float2.y)
            {
                if (@float.x > @float.y && float2.y > @float.x)
                {
                    @float += float2.y - @float.x;
                }
                else if (@float.y > float2.x)
                {
                    @float += 1f - (@float.y - float2.x);
                }
            }
            else
            {
                @float = new float2(float2.y, float2.y + num);
            }
            @float = math.frac(@float);
            return @float;
        }

        public static bool IsSleepTime(Entity entity, Citizen citizen, ref EconomyParameterData economyParameters, float normalizedTime, ref ComponentLookup<Worker> workers, ref ComponentLookup<Game.Citizens.Student> students)
        {
            float2 sleepTime = MyCitizenBehaviorSystem.GetSleepTime(entity, citizen, ref economyParameters, ref workers, ref students);
            if (sleepTime.y < sleepTime.x)
            {
                return normalizedTime > sleepTime.x || normalizedTime < sleepTime.y;
            }
            return normalizedTime > sleepTime.x && normalizedTime < sleepTime.y;
        }

        public NativeQueue<Entity>.ParallelWriter GetCarReserverQueue(out JobHandle deps)
        {
            deps = this.m_CarReserveWriters;
            return this.m_ParallelCarReserverQueue;
        }

        public void AddCarReserveWriter(JobHandle writer)
        {
            this.m_CarReserveWriters = JobHandle.CombineDependencies(this.m_CarReserveWriters, writer);
        }

        //[Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            this.m_SimulationSystem = base.World.GetOrCreateSystemManaged<SimulationSystem>();
            this.m_TimeSystem = base.World.GetOrCreateSystemManaged<TimeSystem>();
            this.m_EndFrameBarrier = base.World.GetOrCreateSystemManaged<EndFrameBarrier>();
            this.m_CityStatisticsSystem = base.World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            this.m_CarReserverQueue = new NativeQueue<Entity>(Allocator.Persistent);
            this.m_ParallelCarReserverQueue = this.m_CarReserverQueue.AsParallelWriter();
            this.m_EconomyParameterQuery = base.GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<EconomyParameterData>()
            });
            this.m_LeisureParameterQuery = base.GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<LeisureParametersData>()
            });
            this.m_PopulationQuery = base.GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<Population>()
            });
            this.m_CitizenQuery = base.GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadWrite<Citizen>(),
                ComponentType.Exclude<TravelPurpose>(),
                ComponentType.Exclude<ResourceBuyer>(),
                ComponentType.ReadOnly<CurrentBuilding>(),
                ComponentType.ReadOnly<HouseholdMember>(),
                ComponentType.ReadOnly<UpdateFrame>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>()
            });
            this.m_OutsideConnectionQuery = base.GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<Game.Objects.OutsideConnection>(),
                ComponentType.Exclude<Game.Objects.ElectricityOutsideConnection>(),
                ComponentType.Exclude<Game.Objects.WaterPipeOutsideConnection>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>()
            });
            this.m_TimeDataQuery = base.GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<TimeData>()
            });
            this.m_HouseholdArchetype = base.World.EntityManager.CreateArchetype(new ComponentType[]
            {
                ComponentType.ReadWrite<Household>(),
                ComponentType.ReadWrite<HouseholdNeed>(),
                ComponentType.ReadWrite<HouseholdCitizen>(),
                ComponentType.ReadWrite<TaxPayer>(),
                ComponentType.ReadWrite<Game.Economy.Resources>(),
                ComponentType.ReadWrite<UpdateFrame>(),
                ComponentType.ReadWrite<Created>()
            });
            base.RequireForUpdate(this.m_CitizenQuery);
            base.RequireForUpdate(this.m_EconomyParameterQuery);
            base.RequireForUpdate(this.m_LeisureParameterQuery);
            base.RequireForUpdate(this.m_TimeDataQuery);
            base.RequireForUpdate(this.m_PopulationQuery);
        }

        //[Preserve]
        protected override void OnDestroy()
        {
            this.m_CarReserverQueue.Dispose();
            base.OnDestroy();
        }

        //[Preserve]
        protected override void OnUpdate()
        {
            uint updateFrameWithInterval = SimulationUtils.GetUpdateFrameWithInterval(this.m_SimulationSystem.frameIndex, (uint)this.GetUpdateInterval(SystemUpdatePhase.GameSimulation), 16);
            NativeQueue<Entity> mailSenderQueue = new NativeQueue<Entity>(Allocator.TempJob);
            NativeQueue<Entity> sleepQueue = new NativeQueue<Entity>(Allocator.TempJob);
            this.__TypeHandle.__Game_Citizens_CommuterHousehold_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Vehicles_OwnedVehicle_RO_BufferLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Prefabs_OutsideConnectionData_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_City_Population_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Buildings_Student_RO_BufferLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Prefabs_PrefabRef_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Prefabs_HaveCoordinatedMeetingData_RO_BufferLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_AttendingMeeting_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_CoordinatedMeeting_RW_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_CoordinatedMeetingAttendee_RO_BufferLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Events_InDanger_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Objects_OutsideConnection_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_TouristHousehold_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_Student_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_Worker_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Agents_MovingAway_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Vehicles_PersonalCar_RW_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_CarKeeper_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Objects_Transform_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Buildings_PropertyRenter_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_Household_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_HouseholdNeed_RW_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_Leisure_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_TripNeeded_RW_BufferTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_HealthProblem_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Simulation_UpdateFrame_SharedComponentTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_HouseholdMember_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Unity_Entities_Entity_TypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_Citizen_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
            MyCitizenBehaviorSystem.CitizenAITickJob citizenAITickJob = default(MyCitizenBehaviorSystem.CitizenAITickJob);
            citizenAITickJob.m_CitizenType = this.__TypeHandle.__Game_Citizens_Citizen_RW_ComponentTypeHandle;
            citizenAITickJob.m_CurrentBuildingType = this.__TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentTypeHandle;
            citizenAITickJob.m_EntityType = this.__TypeHandle.__Unity_Entities_Entity_TypeHandle;
            citizenAITickJob.m_HouseholdMemberType = this.__TypeHandle.__Game_Citizens_HouseholdMember_RO_ComponentTypeHandle;
            citizenAITickJob.m_UpdateFrameType = this.__TypeHandle.__Game_Simulation_UpdateFrame_SharedComponentTypeHandle;
            citizenAITickJob.m_HealthProblemType = this.__TypeHandle.__Game_Citizens_HealthProblem_RO_ComponentTypeHandle;
            citizenAITickJob.m_TripType = this.__TypeHandle.__Game_Citizens_TripNeeded_RW_BufferTypeHandle;
            citizenAITickJob.m_LeisureType = this.__TypeHandle.__Game_Citizens_Leisure_RO_ComponentTypeHandle;
            citizenAITickJob.m_HouseholdNeeds = this.__TypeHandle.__Game_Citizens_HouseholdNeed_RW_ComponentLookup;
            citizenAITickJob.m_Households = this.__TypeHandle.__Game_Citizens_Household_RO_ComponentLookup;
            citizenAITickJob.m_Properties = this.__TypeHandle.__Game_Buildings_PropertyRenter_RO_ComponentLookup;
            citizenAITickJob.m_Transforms = this.__TypeHandle.__Game_Objects_Transform_RO_ComponentLookup;
            citizenAITickJob.m_CarKeepers = this.__TypeHandle.__Game_Citizens_CarKeeper_RO_ComponentLookup;
            citizenAITickJob.m_PersonalCars = this.__TypeHandle.__Game_Vehicles_PersonalCar_RW_ComponentLookup;
            citizenAITickJob.m_MovingAway = this.__TypeHandle.__Game_Agents_MovingAway_RO_ComponentLookup;
            citizenAITickJob.m_Workers = this.__TypeHandle.__Game_Citizens_Worker_RO_ComponentLookup;
            citizenAITickJob.m_Students = this.__TypeHandle.__Game_Citizens_Student_RO_ComponentLookup;
            citizenAITickJob.m_TouristHouseholds = this.__TypeHandle.__Game_Citizens_TouristHousehold_RO_ComponentLookup;
            citizenAITickJob.m_OutsideConnections = this.__TypeHandle.__Game_Objects_OutsideConnection_RO_ComponentLookup;
            citizenAITickJob.m_InDangerData = this.__TypeHandle.__Game_Events_InDanger_RO_ComponentLookup;
            citizenAITickJob.m_Attendees = this.__TypeHandle.__Game_Citizens_CoordinatedMeetingAttendee_RO_BufferLookup;
            citizenAITickJob.m_Meetings = this.__TypeHandle.__Game_Citizens_CoordinatedMeeting_RW_ComponentLookup;
            citizenAITickJob.m_AttendingMeetings = this.__TypeHandle.__Game_Citizens_AttendingMeeting_RO_ComponentLookup;
            citizenAITickJob.m_MeetingDatas = this.__TypeHandle.__Game_Prefabs_HaveCoordinatedMeetingData_RO_BufferLookup;
            citizenAITickJob.m_Prefabs = this.__TypeHandle.__Game_Prefabs_PrefabRef_RO_ComponentLookup;
            citizenAITickJob.m_BuildingStudents = this.__TypeHandle.__Game_Buildings_Student_RO_BufferLookup;
            citizenAITickJob.m_PopulationData = this.__TypeHandle.__Game_City_Population_RO_ComponentLookup;
            citizenAITickJob.m_OutsideConnectionDatas = this.__TypeHandle.__Game_Prefabs_OutsideConnectionData_RO_ComponentLookup;
            citizenAITickJob.m_OwnedVehicles = this.__TypeHandle.__Game_Vehicles_OwnedVehicle_RO_BufferLookup;
            citizenAITickJob.m_CommuterHouseholds = this.__TypeHandle.__Game_Citizens_CommuterHousehold_RO_ComponentLookup;
            citizenAITickJob.m_HouseholdArchetype = this.m_HouseholdArchetype;
            JobHandle job;
            citizenAITickJob.m_OutsideConnectionEntities = this.m_OutsideConnectionQuery.ToEntityListAsync(Allocator.TempJob, out job);
            citizenAITickJob.m_EconomyParameters = this.m_EconomyParameterQuery.GetSingleton<EconomyParameterData>();
            citizenAITickJob.m_LeisureParameters = this.m_LeisureParameterQuery.GetSingleton<LeisureParametersData>();
            citizenAITickJob.m_CommandBuffer = this.m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter();
            citizenAITickJob.m_UpdateFrameIndex = updateFrameWithInterval;
            citizenAITickJob.m_SimulationFrame = this.m_SimulationSystem.frameIndex;
            citizenAITickJob.m_NormalizedTime = this.m_TimeSystem.normalizedTime;
            citizenAITickJob.m_TimeData = this.m_TimeDataQuery.GetSingleton<TimeData>();
            citizenAITickJob.m_PopulationEntity = this.m_PopulationQuery.GetSingletonEntity();
            citizenAITickJob.m_CarReserverQueue = this.m_ParallelCarReserverQueue;
            citizenAITickJob.m_MailSenderQueue = mailSenderQueue.AsParallelWriter();
            citizenAITickJob.m_SleepQueue = sleepQueue.AsParallelWriter();
            citizenAITickJob.m_RandomSeed = RandomSeed.Next();
            MyCitizenBehaviorSystem.CitizenAITickJob jobData = citizenAITickJob;
            JobHandle jobHandle = jobData.ScheduleParallel(this.m_CitizenQuery, JobHandle.CombineDependencies(this.m_CarReserveWriters, JobHandle.CombineDependencies(base.Dependency, job)));
            jobData.m_OutsideConnectionEntities.Dispose(jobHandle);
            this.m_EndFrameBarrier.AddJobHandleForProducer(jobHandle);
            this.AddCarReserveWriter(jobHandle);
            this.__TypeHandle.__Game_Citizens_Citizen_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Vehicles_PersonalCar_RW_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Vehicles_OwnedVehicle_RO_BufferLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_HouseholdMember_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_CarKeeper_RW_ComponentLookup.Update(ref base.CheckedStateRef);
            MyCitizenBehaviorSystem.CitizenReserveHouseholdCarJob jobData2 = default(MyCitizenBehaviorSystem.CitizenReserveHouseholdCarJob);
            jobData2.m_CarKeepers = this.__TypeHandle.__Game_Citizens_CarKeeper_RW_ComponentLookup;
            jobData2.m_HouseholdMembers = this.__TypeHandle.__Game_Citizens_HouseholdMember_RO_ComponentLookup;
            jobData2.m_OwnedVehicles = this.__TypeHandle.__Game_Vehicles_OwnedVehicle_RO_BufferLookup;
            jobData2.m_PersonalCars = this.__TypeHandle.__Game_Vehicles_PersonalCar_RW_ComponentLookup;
            jobData2.m_Citizens = this.__TypeHandle.__Game_Citizens_Citizen_RO_ComponentLookup;
            jobData2.m_CommandBuffer = this.m_EndFrameBarrier.CreateCommandBuffer();
            jobData2.m_ReserverQueue = this.m_CarReserverQueue;
            JobHandle jobHandle2 = jobData2.Schedule(JobHandle.CombineDependencies(jobHandle, this.m_CarReserveWriters));
            this.m_EndFrameBarrier.AddJobHandleForProducer(jobHandle2);
            this.AddCarReserveWriter(jobHandle2);
            this.__TypeHandle.__Game_Buildings_MailProducer_RW_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_MailSender_RW_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Prefabs_ServiceObjectData_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Prefabs_MailAccumulationData_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Prefabs_SpawnableBuildingData_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Prefabs_PrefabRef_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            MyCitizenBehaviorSystem.CitizenTryCollectMailJob jobData3 = default(MyCitizenBehaviorSystem.CitizenTryCollectMailJob);
            jobData3.m_CurrentBuildingData = this.__TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentLookup;
            jobData3.m_PrefabRefData = this.__TypeHandle.__Game_Prefabs_PrefabRef_RO_ComponentLookup;
            jobData3.m_SpawnableBuildingData = this.__TypeHandle.__Game_Prefabs_SpawnableBuildingData_RO_ComponentLookup;
            jobData3.m_MailAccumulationData = this.__TypeHandle.__Game_Prefabs_MailAccumulationData_RO_ComponentLookup;
            jobData3.m_ServiceObjectData = this.__TypeHandle.__Game_Prefabs_ServiceObjectData_RO_ComponentLookup;
            jobData3.m_MailSenderData = this.__TypeHandle.__Game_Citizens_MailSender_RW_ComponentLookup;
            jobData3.m_MailProducerData = this.__TypeHandle.__Game_Buildings_MailProducer_RW_ComponentLookup;
            jobData3.m_MailSenderQueue = mailSenderQueue;
            jobData3.m_CommandBuffer = this.m_EndFrameBarrier.CreateCommandBuffer();
            JobHandle jobHandle3 = jobData3.Schedule(jobHandle);
            this.m_EndFrameBarrier.AddJobHandleForProducer(jobHandle3);
            mailSenderQueue.Dispose(jobHandle3);
            this.__TypeHandle.__Game_Buildings_CitizenPresence_RW_ComponentLookup.Update(ref base.CheckedStateRef);
            this.__TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            MyCitizenBehaviorSystem.CitizeSleepJob jobData4 = default(MyCitizenBehaviorSystem.CitizeSleepJob);
            jobData4.m_CurrentBuildingData = this.__TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentLookup;
            jobData4.m_CitizenPresenceData = this.__TypeHandle.__Game_Buildings_CitizenPresence_RW_ComponentLookup;
            jobData4.m_SleepQueue = sleepQueue;
            JobHandle jobHandle4 = jobData4.Schedule(jobHandle);
            sleepQueue.Dispose(jobHandle4);
            base.Dependency = JobHandle.CombineDependencies(jobHandle2, jobHandle3, jobHandle4);
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
        public MyCitizenBehaviorSystem()
        {
        }

        public static readonly float kMaxPathfindCost = 17000f;

        private JobHandle m_CarReserveWriters;

        private EntityQuery m_CitizenQuery;

        private EntityQuery m_OutsideConnectionQuery;

        private EntityQuery m_EconomyParameterQuery;

        private EntityQuery m_LeisureParameterQuery;

        private EntityQuery m_TimeDataQuery;

        private EntityQuery m_PopulationQuery;

        private SimulationSystem m_SimulationSystem;

        private TimeSystem m_TimeSystem;

        private EndFrameBarrier m_EndFrameBarrier;

        private CityStatisticsSystem m_CityStatisticsSystem;

        private EntityArchetype m_HouseholdArchetype;

        private NativeQueue<Entity> m_CarReserverQueue;

        private NativeQueue<Entity>.ParallelWriter m_ParallelCarReserverQueue;

        private MyCitizenBehaviorSystem.TypeHandle __TypeHandle;

        //[BurstCompile]
        private struct CitizenReserveHouseholdCarJob : IJob
        {
            public void Execute()
            {
                Entity entity;
                while (this.m_ReserverQueue.TryDequeue(out entity))
                {
                    if (this.m_HouseholdMembers.HasComponent(entity))
                    {
                        Entity household = this.m_HouseholdMembers[entity].m_Household;
                        Entity @null = Entity.Null;
                        if (this.m_Citizens[entity].GetAge() != CitizenAge.Child && HouseholdBehaviorSystem.GetFreeCar(household, this.m_OwnedVehicles, this.m_PersonalCars, ref @null) && !this.m_CarKeepers.IsComponentEnabled(entity))
                        {
                            this.m_CarKeepers.SetComponentEnabled(entity, true);
                            this.m_CarKeepers[entity] = new CarKeeper
                            {
                                m_Car = @null
                            };
                            Game.Vehicles.PersonalCar value = this.m_PersonalCars[@null];
                            value.m_Keeper = entity;
                            this.m_PersonalCars[@null] = value;
                        }
                    }
                }
            }

            public ComponentLookup<CarKeeper> m_CarKeepers;

            public ComponentLookup<Game.Vehicles.PersonalCar> m_PersonalCars;

            [ReadOnly]
            public ComponentLookup<HouseholdMember> m_HouseholdMembers;

            [ReadOnly]
            public BufferLookup<OwnedVehicle> m_OwnedVehicles;

            [ReadOnly]
            public ComponentLookup<Citizen> m_Citizens;

            public NativeQueue<Entity> m_ReserverQueue;

            public EntityCommandBuffer m_CommandBuffer;
        }

        //[BurstCompile]
        private struct CitizenTryCollectMailJob : IJob
        {
            public void Execute()
            {
                Entity entity;
                while (this.m_MailSenderQueue.TryDequeue(out entity))
                {
                    CurrentBuilding currentBuilding;
                    MailProducer mailProducer;
                    if (this.m_CurrentBuildingData.TryGetComponent(entity, out currentBuilding) && this.m_MailProducerData.TryGetComponent(currentBuilding.m_CurrentBuilding, out mailProducer) && mailProducer.m_SendingMail >= 15 && !this.RequireCollect(this.m_PrefabRefData[currentBuilding.m_CurrentBuilding].m_Prefab))
                    {
                        bool flag = this.m_MailSenderData.IsComponentEnabled(entity);
                        MailSender mailSender = flag ? this.m_MailSenderData[entity] : default(MailSender);
                        int num = math.min((int)mailProducer.m_SendingMail, (int)(100 - mailSender.m_Amount));
                        if (num > 0)
                        {
                            mailSender.m_Amount = (ushort)((int)mailSender.m_Amount + num);
                            mailProducer.m_SendingMail = (ushort)((int)mailProducer.m_SendingMail - num);
                            this.m_MailProducerData[currentBuilding.m_CurrentBuilding] = mailProducer;
                            if (!flag)
                            {
                                this.m_MailSenderData.SetComponentEnabled(entity, true);
                            }
                            this.m_MailSenderData[entity] = mailSender;
                        }
                    }
                }
            }

            private bool RequireCollect(Entity prefab)
            {
                if (this.m_SpawnableBuildingData.HasComponent(prefab))
                {
                    SpawnableBuildingData spawnableBuildingData = this.m_SpawnableBuildingData[prefab];
                    if (this.m_MailAccumulationData.HasComponent(spawnableBuildingData.m_ZonePrefab))
                    {
                        return this.m_MailAccumulationData[spawnableBuildingData.m_ZonePrefab].m_RequireCollect;
                    }
                }
                else if (this.m_ServiceObjectData.HasComponent(prefab))
                {
                    ServiceObjectData serviceObjectData = this.m_ServiceObjectData[prefab];
                    if (this.m_MailAccumulationData.HasComponent(serviceObjectData.m_Service))
                    {
                        return this.m_MailAccumulationData[serviceObjectData.m_Service].m_RequireCollect;
                    }
                }
                return false;
            }

            [ReadOnly]
            public ComponentLookup<CurrentBuilding> m_CurrentBuildingData;

            [ReadOnly]
            public ComponentLookup<PrefabRef> m_PrefabRefData;

            [ReadOnly]
            public ComponentLookup<SpawnableBuildingData> m_SpawnableBuildingData;

            [ReadOnly]
            public ComponentLookup<MailAccumulationData> m_MailAccumulationData;

            [ReadOnly]
            public ComponentLookup<ServiceObjectData> m_ServiceObjectData;

            public ComponentLookup<MailSender> m_MailSenderData;

            public ComponentLookup<MailProducer> m_MailProducerData;

            public NativeQueue<Entity> m_MailSenderQueue;

            public EntityCommandBuffer m_CommandBuffer;
        }

        //[BurstCompile]
        private struct CitizeSleepJob : IJob
        {
            public void Execute()
            {
                Entity entity;
                while (this.m_SleepQueue.TryDequeue(out entity))
                {
                    if (this.m_CurrentBuildingData.HasComponent(entity))
                    {
                        CurrentBuilding currentBuilding = this.m_CurrentBuildingData[entity];
                        if (this.m_CitizenPresenceData.HasComponent(currentBuilding.m_CurrentBuilding))
                        {
                            CitizenPresence citizenPresence = this.m_CitizenPresenceData[currentBuilding.m_CurrentBuilding];
                            citizenPresence.m_Delta = (sbyte)math.max(-127, (int)(citizenPresence.m_Delta - 1));
                            this.m_CitizenPresenceData[currentBuilding.m_CurrentBuilding] = citizenPresence;
                        }
                    }
                }
            }

            [ReadOnly]
            public ComponentLookup<CurrentBuilding> m_CurrentBuildingData;

            public ComponentLookup<CitizenPresence> m_CitizenPresenceData;

            public NativeQueue<Entity> m_SleepQueue;
        }

        //[BurstCompile]
        private struct CitizenAITickJob : IJobChunk
        {
            private bool CheckSleep(int index, Entity entity, ref Citizen citizen, Entity currentBuilding, Entity household, Entity home, DynamicBuffer<TripNeeded> trips, ref EconomyParameterData economyParameters, ref Unity.Mathematics.Random random)
            {
                if (MyCitizenBehaviorSystem.IsSleepTime(entity, citizen, ref economyParameters, this.m_NormalizedTime, ref this.m_Workers, ref this.m_Students))
                {
                    if (home != Entity.Null && currentBuilding == home)
                    {
                        this.m_CommandBuffer.AddComponent<TravelPurpose>(index, entity, new TravelPurpose
                        {
                            m_Purpose = Purpose.Sleeping
                        });
                        this.m_SleepQueue.Enqueue(entity);
                        this.ReleaseCar(index, entity);
                    }
                    else
                    {
                        this.GoHome(entity, home, trips, currentBuilding);
                    }
                    return true;
                }
                return false;
            }

            private bool CheckLeisure(ref Citizen citizenData, ref Unity.Mathematics.Random random)
            {
                int num = (int)(128 - citizenData.m_LeisureCounter);
                return random.NextInt(this.m_LeisureParameters.m_LeisureRandomFactor) < num;
            }

            private void GoHome(Entity entity, Entity target, DynamicBuffer<TripNeeded> trips, Entity currentBuilding)
            {
                if (target == Entity.Null)
                {
                    return;
                }
                if (currentBuilding == target)
                {
                    return;
                }
                if (!this.m_CarKeepers.IsComponentEnabled(entity))
                {
                    this.m_CarReserverQueue.Enqueue(entity);
                }
                this.m_MailSenderQueue.Enqueue(entity);
                TripNeeded elem = new TripNeeded
                {
                    m_TargetAgent = target,
                    m_Purpose = Purpose.GoingHome
                };
                trips.Add(elem);
            }

            private void GoToOutsideConnection(Entity entity, Entity household, Entity currentBuilding, ref Citizen citizen, DynamicBuffer<TripNeeded> trips, Purpose purpose, ref Unity.Mathematics.Random random)
            {
                if (purpose == Purpose.MovingAway)
                {
                    for (int i = 0; i < trips.Length; i++)
                    {
                        if (trips[i].m_Purpose == Purpose.MovingAway)
                        {
                            return;
                        }
                    }
                }
                if (!this.m_OutsideConnections.HasComponent(currentBuilding))
                {
                    if (!this.m_CarKeepers.IsComponentEnabled(entity))
                    {
                        this.m_CarReserverQueue.Enqueue(entity);
                    }
                    this.m_MailSenderQueue.Enqueue(entity);
                    Entity entity2;
                    if (this.m_OwnedVehicles.HasBuffer(household) && this.m_OwnedVehicles[household].Length > 0 && purpose == Purpose.MovingAway)
                    {
                        BuildingUtils.GetRandomOutsideConnectionByTransferType(ref this.m_OutsideConnectionEntities, ref this.m_OutsideConnectionDatas, ref this.m_Prefabs, random, OutsideConnectionTransferType.Road, out entity2);
                    }
                    else
                    {
                        OutsideConnectionTransferType outsideConnectionTransferType = OutsideConnectionTransferType.Train | OutsideConnectionTransferType.Air | OutsideConnectionTransferType.Ship;
                        if (this.m_OwnedVehicles.HasBuffer(household) && this.m_OwnedVehicles[household].Length > 0)
                        {
                            outsideConnectionTransferType |= OutsideConnectionTransferType.Road;
                        }
                        BuildingUtils.GetRandomOutsideConnectionByTransferType(ref this.m_OutsideConnectionEntities, ref this.m_OutsideConnectionDatas, ref this.m_Prefabs, random, outsideConnectionTransferType, out entity2);
                    }
                    if (entity2 == Entity.Null)
                    {
                        int index = random.NextInt(this.m_OutsideConnectionEntities.Length);
                        entity2 = this.m_OutsideConnectionEntities[index];
                    }
                    trips.Add(new TripNeeded
                    {
                        m_TargetAgent = entity2,
                        m_Purpose = purpose
                    });
                    return;
                }
                if (purpose == Purpose.MovingAway)
                {
                    citizen.m_State |= CitizenFlags.MovingAway;
                }
            }

            private void GoShopping(int chunkIndex, Entity citizen, Entity household, HouseholdNeed need, float3 position)
            {
                if (!this.m_CarKeepers.IsComponentEnabled(citizen))
                {
                    this.m_CarReserverQueue.Enqueue(citizen);
                }
                this.m_MailSenderQueue.Enqueue(citizen);
                this.m_CommandBuffer.AddComponent<ResourceBuyer>(chunkIndex, citizen, new ResourceBuyer
                {
                    m_Payer = household,
                    m_Flags = SetupTargetFlags.Commercial,
                    m_Location = position,
                    m_ResourceNeeded = need.m_Resource,
                    m_AmountNeeded = need.m_Amount
                });
            }

            private float GetTimeLeftUntilInterval(float2 interval)
            {
                if (this.m_NormalizedTime >= interval.x)
                {
                    return 1f - this.m_NormalizedTime + interval.x;
                }
                return interval.x - this.m_NormalizedTime;
            }

            private bool DoLeisure(int chunkIndex, Entity entity, ref Citizen citizen, Entity household, float3 position, int population, ref Unity.Mathematics.Random random, ref EconomyParameterData economyParameters)
            {
                int num = math.min(Mod.m_Setting.LeisureProbability, Mathf.RoundToInt(200f / math.max(1f, math.sqrt(Mod.m_Setting.GetRealTrafficReduction() * (float)population))));
                if (random.NextInt(100) > num)
                {
                    citizen.m_LeisureCounter = byte.MaxValue;
                    return true;
                }
                float2 sleepTime = MyCitizenBehaviorSystem.GetSleepTime(entity, citizen, ref economyParameters, ref this.m_Workers, ref this.m_Students);
                float num2 = this.GetTimeLeftUntilInterval(sleepTime);
                if (this.m_Workers.HasComponent(entity))
                {
                    Worker worker = this.m_Workers[entity];
                    float2 timeToWork = WorkerSystem.GetTimeToWork(citizen, worker, ref economyParameters, true);
                    num2 = math.min(num2, this.GetTimeLeftUntilInterval(timeToWork));
                }
                else if (this.m_Students.HasComponent(entity))
                {
                    Game.Citizens.Student student = this.m_Students[entity];
                    float2 timeToStudy = StudentSystem.GetTimeToStudy(citizen, student, ref economyParameters);
                    num2 = math.min(num2, this.GetTimeLeftUntilInterval(timeToStudy));
                }
                uint num3 = (uint)(num2 * 262144f);
                Leisure component = new Leisure
                {
                    m_LastPossibleFrame = this.m_SimulationFrame + num3
                };
                this.m_CommandBuffer.AddComponent<Leisure>(chunkIndex, entity, component);
                return false;
            }

            private void ReleaseCar(int chunkIndex, Entity citizen)
            {
                if (this.m_CarKeepers.IsComponentEnabled(citizen))
                {
                    Entity car = this.m_CarKeepers[citizen].m_Car;
                    if (this.m_PersonalCars.HasComponent(car))
                    {
                        Game.Vehicles.PersonalCar value = this.m_PersonalCars[car];
                        value.m_Keeper = Entity.Null;
                        this.m_PersonalCars[car] = value;
                    }
                    this.m_CommandBuffer.SetComponentEnabled<CarKeeper>(chunkIndex, citizen, false);
                }
            }

            private bool AttendMeeting(int chunkIndex, Entity entity, ref Citizen citizen, Entity household, Entity currentBuilding, DynamicBuffer<TripNeeded> trips, ref Unity.Mathematics.Random random)
            {
                Entity meeting = this.m_AttendingMeetings[entity].m_Meeting;
                if (this.m_Attendees.HasBuffer(meeting) && this.m_Meetings.HasComponent(meeting))
                {
                    CoordinatedMeeting coordinatedMeeting = this.m_Meetings[meeting];
                    if (this.m_Prefabs.HasComponent(meeting) && coordinatedMeeting.m_Status != MeetingStatus.Done)
                    {
                        HaveCoordinatedMeetingData haveCoordinatedMeetingData = this.m_MeetingDatas[this.m_Prefabs[meeting].m_Prefab][coordinatedMeeting.m_Phase];
                        DynamicBuffer<CoordinatedMeetingAttendee> dynamicBuffer = this.m_Attendees[meeting];
                        if (coordinatedMeeting.m_Status == MeetingStatus.Waiting && coordinatedMeeting.m_Target == Entity.Null)
                        {
                            if (dynamicBuffer.Length > 0 && dynamicBuffer[0].m_Attendee == entity)
                            {
                                if (haveCoordinatedMeetingData.m_Purpose.m_Purpose == Purpose.Shopping)
                                {
                                    float3 position = this.m_Transforms[currentBuilding].m_Position;
                                    this.GoShopping(chunkIndex, entity, household, new HouseholdNeed
                                    {
                                        m_Resource = haveCoordinatedMeetingData.m_Purpose.m_Resource,
                                        m_Amount = haveCoordinatedMeetingData.m_Purpose.m_Data
                                    }, position);
                                    return true;
                                }
                                if (haveCoordinatedMeetingData.m_Purpose.m_Purpose == Purpose.Traveling)
                                {
                                    Citizen citizen2 = default(Citizen);
                                    this.GoToOutsideConnection(entity, household, currentBuilding, ref citizen2, trips, haveCoordinatedMeetingData.m_Purpose.m_Purpose, ref random);
                                }
                                else
                                {
                                    if (haveCoordinatedMeetingData.m_Purpose.m_Purpose != Purpose.GoingHome)
                                    {
                                        trips.Add(new TripNeeded
                                        {
                                            m_Purpose = haveCoordinatedMeetingData.m_Purpose.m_Purpose,
                                            m_Resource = haveCoordinatedMeetingData.m_Purpose.m_Resource,
                                            m_Data = haveCoordinatedMeetingData.m_Purpose.m_Data,
                                            m_TargetAgent = default(Entity)
                                        });
                                        return true;
                                    }
                                    if (this.m_Properties.HasComponent(household))
                                    {
                                        coordinatedMeeting.m_Target = this.m_Properties[household].m_Property;
                                        this.m_Meetings[meeting] = coordinatedMeeting;
                                        this.GoHome(entity, this.m_Properties[household].m_Property, trips, currentBuilding);
                                    }
                                }
                            }
                        }
                        else if (coordinatedMeeting.m_Status == MeetingStatus.Waiting || coordinatedMeeting.m_Status == MeetingStatus.Traveling)
                        {
                            for (int i = 0; i < dynamicBuffer.Length; i++)
                            {
                                if (dynamicBuffer[i].m_Attendee == entity)
                                {
                                    if (coordinatedMeeting.m_Target != Entity.Null && currentBuilding != coordinatedMeeting.m_Target && (!this.m_Properties.HasComponent(coordinatedMeeting.m_Target) || this.m_Properties[coordinatedMeeting.m_Target].m_Property != currentBuilding))
                                    {
                                        trips.Add(new TripNeeded
                                        {
                                            m_Purpose = haveCoordinatedMeetingData.m_Purpose.m_Purpose,
                                            m_Resource = haveCoordinatedMeetingData.m_Purpose.m_Resource,
                                            m_Data = haveCoordinatedMeetingData.m_Purpose.m_Data,
                                            m_TargetAgent = coordinatedMeeting.m_Target
                                        });
                                    }
                                    return true;
                                }
                            }
                            this.m_CommandBuffer.RemoveComponent<AttendingMeeting>(chunkIndex, entity);
                            return false;
                        }
                    }
                    return coordinatedMeeting.m_Status != MeetingStatus.Done;
                }
                this.m_CommandBuffer.RemoveComponent<AttendingMeeting>(chunkIndex, entity);
                return false;
            }

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (chunk.GetSharedComponent<UpdateFrame>(this.m_UpdateFrameType).m_Index != this.m_UpdateFrameIndex)
                {
                    return;
                }
                Unity.Mathematics.Random random = this.m_RandomSeed.GetRandom(unfilteredChunkIndex);
                NativeArray<Entity> nativeArray = chunk.GetNativeArray(this.m_EntityType);
                NativeArray<Citizen> nativeArray2 = chunk.GetNativeArray<Citizen>(ref this.m_CitizenType);
                NativeArray<HouseholdMember> nativeArray3 = chunk.GetNativeArray<HouseholdMember>(ref this.m_HouseholdMemberType);
                NativeArray<CurrentBuilding> nativeArray4 = chunk.GetNativeArray<CurrentBuilding>(ref this.m_CurrentBuildingType);
                BufferAccessor<TripNeeded> bufferAccessor = chunk.GetBufferAccessor<TripNeeded>(ref this.m_TripType);
                bool flag = chunk.Has<HealthProblem>(ref this.m_HealthProblemType);
                int population = this.m_PopulationData[this.m_PopulationEntity].m_Population;
                for (int i = 0; i < nativeArray.Length; i++)
                {
                    Entity entity = nativeArray3[i].m_Household;
                    Entity entity2 = nativeArray[i];
                    bool flag2 = this.m_TouristHouseholds.HasComponent(entity);
                    DynamicBuffer<TripNeeded> trips = bufferAccessor[i];
                    if (entity == Entity.Null)
                    {
                        entity = this.m_CommandBuffer.CreateEntity(unfilteredChunkIndex, this.m_HouseholdArchetype);
                        this.m_CommandBuffer.SetComponent<HouseholdMember>(unfilteredChunkIndex, entity2, new HouseholdMember
                        {
                            m_Household = entity
                        });
                        this.m_CommandBuffer.SetBuffer<HouseholdCitizen>(unfilteredChunkIndex, entity).Add(new HouseholdCitizen
                        {
                            m_Citizen = entity2
                        });
                    }
                    else if (!this.m_Households.HasComponent(entity))
                    {
                        this.m_CommandBuffer.AddComponent<Deleted>(unfilteredChunkIndex, entity2, default(Deleted));
                    }
                    else
                    {
                        Entity currentBuilding = nativeArray4[i].m_CurrentBuilding;
                        if (this.m_Transforms.HasComponent(currentBuilding) && (!this.m_InDangerData.HasComponent(currentBuilding) || (this.m_InDangerData[currentBuilding].m_Flags & DangerFlags.StayIndoors) == (DangerFlags)0U))
                        {
                            Citizen citizen = nativeArray2[i];
                            bool flag3 = (citizen.m_State & CitizenFlags.Commuter) > CitizenFlags.None;
                            CitizenAge age = citizen.GetAge();
                            if (flag3 && (age == CitizenAge.Elderly || age == CitizenAge.Child))
                            {
                                this.m_CommandBuffer.AddComponent<Deleted>(unfilteredChunkIndex, entity2, default(Deleted));
                            }
                            if ((citizen.m_State & CitizenFlags.MovingAway) != CitizenFlags.None)
                            {
                                this.m_CommandBuffer.AddComponent<Deleted>(unfilteredChunkIndex, entity2, default(Deleted));
                            }
                            else if (this.m_MovingAway.HasComponent(entity))
                            {
                                this.GoToOutsideConnection(entity2, entity, currentBuilding, ref citizen, trips, Purpose.MovingAway, ref random);
                                if (chunk.Has<Leisure>(ref this.m_LeisureType))
                                {
                                    this.m_CommandBuffer.RemoveComponent<Leisure>(unfilteredChunkIndex, entity2);
                                }
                                if (this.m_Workers.HasComponent(entity2))
                                {
                                    this.m_CommandBuffer.RemoveComponent<Worker>(unfilteredChunkIndex, entity2);
                                }
                                if (this.m_Students.HasComponent(entity2))
                                {
                                    if (this.m_BuildingStudents.HasBuffer(this.m_Students[entity2].m_School))
                                    {
                                        this.m_CommandBuffer.AddComponent<StudentsRemoved>(unfilteredChunkIndex, this.m_Students[entity2].m_School);
                                    }
                                    this.m_CommandBuffer.RemoveComponent<Game.Citizens.Student>(unfilteredChunkIndex, entity2);
                                }
                                nativeArray2[i] = citizen;
                            }
                            else
                            {
                                Entity entity3 = Entity.Null;
                                if (this.m_Properties.HasComponent(entity))
                                {
                                    entity3 = this.m_Properties[entity].m_Property;
                                }
                                else if (flag2)
                                {
                                    Entity hotel = this.m_TouristHouseholds[entity].m_Hotel;
                                    if (this.m_Properties.HasComponent(hotel))
                                    {
                                        entity3 = this.m_Properties[hotel].m_Property;
                                    }
                                }
                                else if (flag3)
                                {
                                    if (this.m_OutsideConnections.HasComponent(currentBuilding))
                                    {
                                        entity3 = currentBuilding;
                                    }
                                    else
                                    {
                                        CommuterHousehold commuterHousehold;
                                        if (this.m_CommuterHouseholds.TryGetComponent(entity, out commuterHousehold))
                                        {
                                            entity3 = commuterHousehold.m_OriginalFrom;
                                        }
                                        if (entity3 == Entity.Null)
                                        {
                                            entity3 = this.m_OutsideConnectionEntities[random.NextInt(this.m_OutsideConnectionEntities.Length)];
                                        }
                                    }
                                }
                                if (flag)
                                {
                                    if (chunk.Has<Leisure>(ref this.m_LeisureType))
                                    {
                                        this.m_CommandBuffer.RemoveComponent<Leisure>(unfilteredChunkIndex, entity2);
                                    }
                                }
                                else if (!this.m_AttendingMeetings.HasComponent(entity2) || !this.AttendMeeting(unfilteredChunkIndex, entity2, ref citizen, entity, currentBuilding, trips, ref random))
                                {
                                    if ((this.m_Workers.HasComponent(entity2) && !WorkerSystem.IsTodayOffDay(citizen, ref this.m_EconomyParameters, this.m_SimulationFrame, this.m_TimeData, population) && WorkerSystem.IsTimeToWork(citizen, this.m_Workers[entity2], ref this.m_EconomyParameters, this.m_NormalizedTime)) || (this.m_Students.HasComponent(entity2) && StudentSystem.IsTimeToStudy(citizen, this.m_Students[entity2], ref this.m_EconomyParameters, this.m_NormalizedTime, this.m_SimulationFrame, this.m_TimeData, population)))
                                    {
                                        if (chunk.Has<Leisure>(ref this.m_LeisureType))
                                        {
                                            this.m_CommandBuffer.RemoveComponent<Leisure>(unfilteredChunkIndex, entity2);
                                        }
                                    }
                                    else if (this.CheckSleep(i, entity2, ref citizen, currentBuilding, entity, entity3, trips, ref this.m_EconomyParameters, ref random))
                                    {
                                        if (chunk.Has<Leisure>(ref this.m_LeisureType))
                                        {
                                            this.m_CommandBuffer.RemoveComponent<Leisure>(unfilteredChunkIndex, entity2);
                                        }
                                    }
                                    else
                                    {
                                        if (age == CitizenAge.Adult || age == CitizenAge.Elderly)
                                        {
                                            HouseholdNeed householdNeed = this.m_HouseholdNeeds[entity];
                                            if (householdNeed.m_Resource != Resource.NoResource && this.m_Transforms.HasComponent(currentBuilding))
                                            {
                                                this.GoShopping(unfilteredChunkIndex, entity2, entity, householdNeed, this.m_Transforms[currentBuilding].m_Position);
                                                householdNeed.m_Resource = Resource.NoResource;
                                                this.m_HouseholdNeeds[entity] = householdNeed;
                                                if (chunk.Has<Leisure>(ref this.m_LeisureType))
                                                {
                                                    this.m_CommandBuffer.RemoveComponent<Leisure>(unfilteredChunkIndex, entity2);
                                                    goto IL_62F;
                                                }
                                                goto IL_62F;
                                            }
                                        }
                                        bool flag4 = !chunk.Has<Leisure>(ref this.m_LeisureType) && !this.m_OutsideConnections.HasComponent(currentBuilding) && this.CheckLeisure(ref citizen, ref random);
                                        nativeArray2[i] = citizen;
                                        if (flag4)
                                        {
                                            if (this.DoLeisure(unfilteredChunkIndex, entity2, ref citizen, entity, this.m_Transforms[currentBuilding].m_Position, population, ref random, ref this.m_EconomyParameters))
                                            {
                                                nativeArray2[i] = citizen;
                                            }
                                        }
                                        else if (!chunk.Has<Leisure>(ref this.m_LeisureType))
                                        {
                                            if (currentBuilding != entity3)
                                            {
                                                this.GoHome(entity2, entity3, trips, currentBuilding);
                                            }
                                            else
                                            {
                                                this.ReleaseCar(unfilteredChunkIndex, entity2);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                IL_62F:;
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
            public ComponentTypeHandle<HouseholdMember> m_HouseholdMemberType;

            [ReadOnly]
            public ComponentTypeHandle<CurrentBuilding> m_CurrentBuildingType;

            [ReadOnly]
            public SharedComponentTypeHandle<UpdateFrame> m_UpdateFrameType;

            [ReadOnly]
            public ComponentTypeHandle<HealthProblem> m_HealthProblemType;

            public BufferTypeHandle<TripNeeded> m_TripType;

            [ReadOnly]
            public ComponentTypeHandle<Leisure> m_LeisureType;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<HouseholdNeed> m_HouseholdNeeds;

            [ReadOnly]
            public ComponentLookup<Household> m_Households;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> m_Properties;

            [ReadOnly]
            public ComponentLookup<Game.Objects.Transform> m_Transforms;

            [ReadOnly]
            public ComponentLookup<CarKeeper> m_CarKeepers;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<Game.Vehicles.PersonalCar> m_PersonalCars;

            [ReadOnly]
            public ComponentLookup<MovingAway> m_MovingAway;

            [ReadOnly]
            public ComponentLookup<Worker> m_Workers;

            [ReadOnly]
            public ComponentLookup<Game.Citizens.Student> m_Students;

            [ReadOnly]
            public ComponentLookup<TouristHousehold> m_TouristHouseholds;

            [ReadOnly]
            public ComponentLookup<Game.Objects.OutsideConnection> m_OutsideConnections;

            [ReadOnly]
            public ComponentLookup<OutsideConnectionData> m_OutsideConnectionDatas;

            [ReadOnly]
            public ComponentLookup<InDanger> m_InDangerData;

            [ReadOnly]
            public ComponentLookup<AttendingMeeting> m_AttendingMeetings;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<CoordinatedMeeting> m_Meetings;

            [ReadOnly]
            public BufferLookup<CoordinatedMeetingAttendee> m_Attendees;

            [ReadOnly]
            public BufferLookup<HaveCoordinatedMeetingData> m_MeetingDatas;

            [ReadOnly]
            public ComponentLookup<PrefabRef> m_Prefabs;

            [ReadOnly]
            public BufferLookup<Game.Buildings.Student> m_BuildingStudents;

            [ReadOnly]
            public ComponentLookup<Population> m_PopulationData;

            [ReadOnly]
            public BufferLookup<OwnedVehicle> m_OwnedVehicles;

            [ReadOnly]
            public ComponentLookup<CommuterHousehold> m_CommuterHouseholds;

            [ReadOnly]
            public EntityArchetype m_HouseholdArchetype;

            [ReadOnly]
            public NativeList<Entity> m_OutsideConnectionEntities;

            [ReadOnly]
            public EconomyParameterData m_EconomyParameters;

            [ReadOnly]
            public LeisureParametersData m_LeisureParameters;

            public uint m_UpdateFrameIndex;

            public float m_NormalizedTime;

            public uint m_SimulationFrame;

            public EntityCommandBuffer.ParallelWriter m_CommandBuffer;

            public NativeQueue<Entity>.ParallelWriter m_CarReserverQueue;

            public NativeQueue<Entity>.ParallelWriter m_MailSenderQueue;

            public NativeQueue<Entity>.ParallelWriter m_SleepQueue;

            public TimeData m_TimeData;

            public Entity m_PopulationEntity;

            public RandomSeed m_RandomSeed;
        }

        private struct TypeHandle
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void __AssignHandles(ref SystemState state)
            {
                this.__Game_Citizens_Citizen_RW_ComponentTypeHandle = state.GetComponentTypeHandle<Citizen>(false);
                this.__Game_Citizens_CurrentBuilding_RO_ComponentTypeHandle = state.GetComponentTypeHandle<CurrentBuilding>(true);
                this.__Unity_Entities_Entity_TypeHandle = state.GetEntityTypeHandle();
                this.__Game_Citizens_HouseholdMember_RO_ComponentTypeHandle = state.GetComponentTypeHandle<HouseholdMember>(true);
                this.__Game_Simulation_UpdateFrame_SharedComponentTypeHandle = state.GetSharedComponentTypeHandle<UpdateFrame>();
                this.__Game_Citizens_HealthProblem_RO_ComponentTypeHandle = state.GetComponentTypeHandle<HealthProblem>(true);
                this.__Game_Citizens_TripNeeded_RW_BufferTypeHandle = state.GetBufferTypeHandle<TripNeeded>(false);
                this.__Game_Citizens_Leisure_RO_ComponentTypeHandle = state.GetComponentTypeHandle<Leisure>(true);
                this.__Game_Citizens_HouseholdNeed_RW_ComponentLookup = state.GetComponentLookup<HouseholdNeed>(false);
                this.__Game_Citizens_Household_RO_ComponentLookup = state.GetComponentLookup<Household>(true);
                this.__Game_Buildings_PropertyRenter_RO_ComponentLookup = state.GetComponentLookup<PropertyRenter>(true);
                this.__Game_Objects_Transform_RO_ComponentLookup = state.GetComponentLookup<Game.Objects.Transform>(true);
                this.__Game_Citizens_CarKeeper_RO_ComponentLookup = state.GetComponentLookup<CarKeeper>(true);
                this.__Game_Vehicles_PersonalCar_RW_ComponentLookup = state.GetComponentLookup<Game.Vehicles.PersonalCar>(false);
                this.__Game_Agents_MovingAway_RO_ComponentLookup = state.GetComponentLookup<MovingAway>(true);
                this.__Game_Citizens_Worker_RO_ComponentLookup = state.GetComponentLookup<Worker>(true);
                this.__Game_Citizens_Student_RO_ComponentLookup = state.GetComponentLookup<Game.Citizens.Student>(true);
                this.__Game_Citizens_TouristHousehold_RO_ComponentLookup = state.GetComponentLookup<TouristHousehold>(true);
                this.__Game_Objects_OutsideConnection_RO_ComponentLookup = state.GetComponentLookup<Game.Objects.OutsideConnection>(true);
                this.__Game_Events_InDanger_RO_ComponentLookup = state.GetComponentLookup<InDanger>(true);
                this.__Game_Citizens_CoordinatedMeetingAttendee_RO_BufferLookup = state.GetBufferLookup<CoordinatedMeetingAttendee>(true);
                this.__Game_Citizens_CoordinatedMeeting_RW_ComponentLookup = state.GetComponentLookup<CoordinatedMeeting>(false);
                this.__Game_Citizens_AttendingMeeting_RO_ComponentLookup = state.GetComponentLookup<AttendingMeeting>(true);
                this.__Game_Prefabs_HaveCoordinatedMeetingData_RO_BufferLookup = state.GetBufferLookup<HaveCoordinatedMeetingData>(true);
                this.__Game_Prefabs_PrefabRef_RO_ComponentLookup = state.GetComponentLookup<PrefabRef>(true);
                this.__Game_Buildings_Student_RO_BufferLookup = state.GetBufferLookup<Game.Buildings.Student>(true);
                this.__Game_City_Population_RO_ComponentLookup = state.GetComponentLookup<Population>(true);
                this.__Game_Prefabs_OutsideConnectionData_RO_ComponentLookup = state.GetComponentLookup<OutsideConnectionData>(true);
                this.__Game_Vehicles_OwnedVehicle_RO_BufferLookup = state.GetBufferLookup<OwnedVehicle>(true);
                this.__Game_Citizens_CommuterHousehold_RO_ComponentLookup = state.GetComponentLookup<CommuterHousehold>(true);
                this.__Game_Citizens_CarKeeper_RW_ComponentLookup = state.GetComponentLookup<CarKeeper>(false);
                this.__Game_Citizens_HouseholdMember_RO_ComponentLookup = state.GetComponentLookup<HouseholdMember>(true);
                this.__Game_Citizens_Citizen_RO_ComponentLookup = state.GetComponentLookup<Citizen>(true);
                this.__Game_Citizens_CurrentBuilding_RO_ComponentLookup = state.GetComponentLookup<CurrentBuilding>(true);
                this.__Game_Prefabs_SpawnableBuildingData_RO_ComponentLookup = state.GetComponentLookup<SpawnableBuildingData>(true);
                this.__Game_Prefabs_MailAccumulationData_RO_ComponentLookup = state.GetComponentLookup<MailAccumulationData>(true);
                this.__Game_Prefabs_ServiceObjectData_RO_ComponentLookup = state.GetComponentLookup<ServiceObjectData>(true);
                this.__Game_Citizens_MailSender_RW_ComponentLookup = state.GetComponentLookup<MailSender>(false);
                this.__Game_Buildings_MailProducer_RW_ComponentLookup = state.GetComponentLookup<MailProducer>(false);
                this.__Game_Buildings_CitizenPresence_RW_ComponentLookup = state.GetComponentLookup<CitizenPresence>(false);
            }

            public ComponentTypeHandle<Citizen> __Game_Citizens_Citizen_RW_ComponentTypeHandle;

            [ReadOnly]
            public ComponentTypeHandle<CurrentBuilding> __Game_Citizens_CurrentBuilding_RO_ComponentTypeHandle;

            [ReadOnly]
            public EntityTypeHandle __Unity_Entities_Entity_TypeHandle;

            [ReadOnly]
            public ComponentTypeHandle<HouseholdMember> __Game_Citizens_HouseholdMember_RO_ComponentTypeHandle;

            public SharedComponentTypeHandle<UpdateFrame> __Game_Simulation_UpdateFrame_SharedComponentTypeHandle;

            [ReadOnly]
            public ComponentTypeHandle<HealthProblem> __Game_Citizens_HealthProblem_RO_ComponentTypeHandle;

            public BufferTypeHandle<TripNeeded> __Game_Citizens_TripNeeded_RW_BufferTypeHandle;

            [ReadOnly]
            public ComponentTypeHandle<Leisure> __Game_Citizens_Leisure_RO_ComponentTypeHandle;

            public ComponentLookup<HouseholdNeed> __Game_Citizens_HouseholdNeed_RW_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Household> __Game_Citizens_Household_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> __Game_Buildings_PropertyRenter_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Game.Objects.Transform> __Game_Objects_Transform_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<CarKeeper> __Game_Citizens_CarKeeper_RO_ComponentLookup;

            public ComponentLookup<Game.Vehicles.PersonalCar> __Game_Vehicles_PersonalCar_RW_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<MovingAway> __Game_Agents_MovingAway_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Worker> __Game_Citizens_Worker_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Game.Citizens.Student> __Game_Citizens_Student_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<TouristHousehold> __Game_Citizens_TouristHousehold_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Game.Objects.OutsideConnection> __Game_Objects_OutsideConnection_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<InDanger> __Game_Events_InDanger_RO_ComponentLookup;

            [ReadOnly]
            public BufferLookup<CoordinatedMeetingAttendee> __Game_Citizens_CoordinatedMeetingAttendee_RO_BufferLookup;

            public ComponentLookup<CoordinatedMeeting> __Game_Citizens_CoordinatedMeeting_RW_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<AttendingMeeting> __Game_Citizens_AttendingMeeting_RO_ComponentLookup;

            [ReadOnly]
            public BufferLookup<HaveCoordinatedMeetingData> __Game_Prefabs_HaveCoordinatedMeetingData_RO_BufferLookup;

            [ReadOnly]
            public ComponentLookup<PrefabRef> __Game_Prefabs_PrefabRef_RO_ComponentLookup;

            [ReadOnly]
            public BufferLookup<Game.Buildings.Student> __Game_Buildings_Student_RO_BufferLookup;

            [ReadOnly]
            public ComponentLookup<Population> __Game_City_Population_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<OutsideConnectionData> __Game_Prefabs_OutsideConnectionData_RO_ComponentLookup;

            [ReadOnly]
            public BufferLookup<OwnedVehicle> __Game_Vehicles_OwnedVehicle_RO_BufferLookup;

            [ReadOnly]
            public ComponentLookup<CommuterHousehold> __Game_Citizens_CommuterHousehold_RO_ComponentLookup;

            public ComponentLookup<CarKeeper> __Game_Citizens_CarKeeper_RW_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<HouseholdMember> __Game_Citizens_HouseholdMember_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Citizen> __Game_Citizens_Citizen_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<CurrentBuilding> __Game_Citizens_CurrentBuilding_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<SpawnableBuildingData> __Game_Prefabs_SpawnableBuildingData_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<MailAccumulationData> __Game_Prefabs_MailAccumulationData_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<ServiceObjectData> __Game_Prefabs_ServiceObjectData_RO_ComponentLookup;

            public ComponentLookup<MailSender> __Game_Citizens_MailSender_RW_ComponentLookup;

            public ComponentLookup<MailProducer> __Game_Buildings_MailProducer_RW_ComponentLookup;

            public ComponentLookup<CitizenPresence> __Game_Buildings_CitizenPresence_RW_ComponentLookup;
        }
    }
}
