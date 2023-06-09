using UnityEngine;
using UnityEngine.AI;
using SensorToolkit;
using Unity.Netcode;
using System.Collections.Generic;

public class BaseAttackAI : BaseAI
{
    #region Variables, top of script
    [Header("Attack AI Components")]
    /// <summary> Sensor Toolkit, needed by the Hostile AI.</summary>
    public Sensor sensorDetector = default;
    /// <summary> Current state of the AI (AIState Enum for values)</summary>
    public AIState CurrentAIState { get; private set; }
    /// <summary> Animator, to procc animations.</summary>
    public AnimatorAI AIAnimator = default;
    /// <summary> NavMesh.</summary>
    public NavMeshAgent agent = default;
    /// <summary> ScriptableObject of the AI database.</summary>
    public aiDatabase aiDatabase = default;
    /// <summary> This contains health & healing functions & variables, is networked.</summary>
    public AIHealthController aiHealth = default;

    [Header("Variables - AI Settings")]
    /// <summary> The target of the Hostile AI</summary>
    public GameObject currentTarget = null;
    /// <summary> A saved ulong of the networked target clientId</summary>
    private ulong savedClientId;
    /// <summary> How fast is this NPC's brain, procc brain thinking every X second :D</summary>
    [SerializeField] private float brainTimer = 1f;
    /// <summary> A stored value of the brainTimer, to set it back to what it was supposed to be, after wandering</summary>
    private float _prevBrainTimer = 1f;
    /// <summary> Can the AI leave the spawn area?</summary>
    [SerializeField] private bool canLeaveSpawnArea = false;
    /// <summary> Neutral or Hostile?</summary>
    [SerializeField] private bool isHostile = false;

    [Header("Variables - Animation/Idle Settings")]
    /// <summary> Minimum idle time</summary>
    [SerializeField] private float minIdleTime = 3f;
    /// <summary> Maximum idle time</summary>
    [SerializeField] private float maxIdleTime = 8f;
    /// <summary> How many random Idle animations this AI has to be able to trigger, this is loaded through AI database</summary>
    private int maxExtraIdleAnims = 0;
    private static int ANIMATOR_PARAM_WALK_SPEED = Animator.StringToHash("WalkSpeed");
    private static int AUTO_ATTACK = 0;
    [Header("Variables - Range Settings")]
    /// <summary> Max range dif before AI will stop following</summary>
    [SerializeField] private float maxRangeFromTarget = 20f;
    /// <summary> combatRange distance, this is the distance where the AI is close to it's target and can attack or behave as if it has reached it's target</summary>
    [SerializeField] private float combatRange = 1.8f;
    /// <summary> Time since last brain operation </summary>
    private float savedBrainTimer = 0f;
    /// <summary> Spawn location of this AI, so it heads back home once out of combat.</summary>
    private Vector3 spawnPos;
    /// <summary> Store the normal speed of the AI</summary>
    private float _prevWalkSpeed;

    [Header("Variables - Combat")]
    /// <summary> The layer mask for which layers should be hit</summary>
    [SerializeField] protected LayerMask hitRegistrationLayerMask = default;
    /// <summary> Saves how long the AI has been in combat, to not instantly cast spells etc. etc.</summary>
    private float savedCombatTimer = 0f;
    private float globalCoolDown = 1f;
    [SerializeField] private float maxFleeTime = 10f;
    /// <summary> Is the AI currently casting a spell? To avoid casting other spells ontop.</summary>
    public int isCastingSpell = 0;
    /// <summary> Stops the movement of the AI, some spells apply this</summary>
    public bool stopMovement = false;

    [Header("Sensor Detector Settings")]
    private float PulseTimer = 2.5f;
    private float savedPulseTimer = 0f;

    [Header("Abillity Settings")]
    public List<AbillitySlot> aiAbillities = new List<AbillitySlot>();

    [Header("Variables - Owner Settings")]
    /// <summary> Is the AI owned by a player? null if not</summary>
    [SerializeField] private GameObject currentOwner = null;
    /// <summary> Is the AI owned by a player? null if not</summary>
    public ulong currentOwnerId;

    [Header("Network Settings")]
    /// <summary> Networked byte variable that contains the current animation of the AI, to apply it "instantly"/sync to players who just zone in</summary>
    [SerializeField] public NetworkVariable<byte> currentAnimation = new NetworkVariable<byte>(255);
    /// <summary> A byte that contains the current model of the AI, to set it up automatically the run</summary>
    [SerializeField] private NetworkVariable<byte> currentModel = new NetworkVariable<byte>(255);
    /// <summary> Float value that holds the current velocity of the AI, to sync with the blendmesh tree in animator</summary>
    public NetworkVariable<float> currentVelocity = new NetworkVariable<float>(0);
    /// <summary> The minimum amount the position of a AI should move since last update, for it to update over the network</summary>
    [SerializeField] private float networkPositionThreshold = 0.05f;
    /// <summary> The minimum amount the rotation of a AI should move since last update, for it to update over the network</summary>
    [SerializeField] private float networkRotationThreshold = 0.05f;
    /// <summary> Networked AI position, this is the variable that updates the AI position to other clients</summary>
    private NetworkVariable<Vector3> AIPosition = new NetworkVariable<Vector3>();
    private Vector3 oldAIPosition = new Vector3(0f, 0f, 0f);
    /// <summary> Networked AI rotation, this is the variable that updates the AI rotation to other clients</summary>
    private NetworkVariable<Quaternion> AIRotation = new NetworkVariable<Quaternion>();
    private Quaternion oldAIRotation;
    /// <summary> Smoothen out the lerping on pos & rot</summary>
    private readonly float smoothSyncValue = 10f;

    public enum AIState
    {
        Idle,
        Following,
        Attacking,
        HeadHome,
        Wander,
        Flee,
    }

    #endregion

    #region Networked OnSpawn / OnDespawn & Subscribing to AI movement
    // Copy this region into new AI script if movement animations should be 100% in sync, it will use more bandwidth, but is worth it on smooth AI's
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        OnSpawnedAIClient();
        OnSpawnedAIServer();

        currentVelocity.OnValueChanged += UpdateWalkAnimation;
        currentAnimation.OnValueChanged += UpdateAnimation;
        currentModel.OnValueChanged += SetupAI;

        SetupAI(0, currentModel.Value);
    }

    public override void OnSpawnedAIServer() 
    {
        base.OnSpawnedAIServer();
        if (!IsServer) return;
        currentModel.Value = (byte)Random.Range(0 , aiDatabase.AI.Length-1);
        combatRange = aiDatabase.AI[currentModel.Value].autoAttackRange;
        for (int i = 0; i < aiDatabase.AI[currentModel.Value].aiAbillityPrefabs.Length; i++)
        {
            if (aiDatabase.AI[currentModel.Value].aiAbillityPrefabs == null) continue;
            GameObject tmpAbillity = Instantiate(aiDatabase.AI[currentModel.Value].aiAbillityPrefabs[i], gameObject.transform);
            aiAbillities.Add(new AbillitySlot(aiDatabase.AI[currentModel.Value].aiAbillityPrefabs[i].name, tmpAbillity.GetComponent<BaseAbillity>()));
            if (i == 0) { tmpAbillity.GetComponent<BaseAbillity>().abillityCooldownTime = aiDatabase.AI[currentModel.Value].autoAttackTimer; }
            //Debug.Log($"Added Abillity {tmpAbillity.name}");
        }

        InvokeRepeating("UpdateVelocity", 0.5f, 0.2f);
        agent.updateRotation = true;            // Update Rotation
        oldAIPosition = transform.position;     // Old Pos
        spawnPos = transform.position;          // Spawn Position
        _prevBrainTimer = brainTimer;           // We store this here, to set it back to normal, after NPC exit's wandering state.
        _prevWalkSpeed = agent.speed;           // Store speed
        currentOwnerId = OwnerClientId;         // Set server as owner
        OnStateChange(AIState.Wander);          // Starts by wandering, so they don't stand still at spawn pos (looks more alive)
    }

    private void UpdateVelocity()
    {
        if (IsServer)
        {
            if (currentVelocity.Value != agent.velocity.magnitude)
            {
                currentVelocity.Value = agent.velocity.magnitude;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        currentAnimation.OnValueChanged -= UpdateAnimation;
        currentVelocity.OnValueChanged -= UpdateWalkAnimation;
        currentModel.OnValueChanged -= SetupAI;
    }

    public override void OnSpawnedAIClient()
    {
        if (!IsClient) return;

        if(!IsHost && Vector3.Distance(transform.position, AIPosition.Value) > 10)
        {
            // Warp it when a client "zones in for this AI, if it was far away from last time he zoned in on it.
            transform.position = AIPosition.Value;
            transform.rotation = AIRotation.Value;
        }
    }
    #endregion

    #region Sync of animation / model & attack RPC
    private void SetupAI(byte previousValue, byte newValue)
    {
        //print($"SetupAI: {previousValue}/{newValue}");
        if (newValue == 255) return;

        GameObject tmpAI = Instantiate(aiDatabase.AI[newValue].AiModel, transform);
        AIAnimator.aiAnimator.runtimeAnimatorController = aiDatabase.AI[newValue].AiController;
        AIAnimator.aiAnimator.GetComponent<Animator>().avatar = aiDatabase.AI[newValue].AiAvatar;
        AIAnimator.aiAnimator.GetComponent<Animator>().Rebind();

        if (!IsServer) return;
        maxExtraIdleAnims = aiDatabase.AI[newValue].randomIdleAnims;
        isHostile = aiDatabase.AI[newValue].isHostile;

        gameObject.name = $"(AI) {tmpAI.name}";

        //GameObject nameTag = Instantiate(Resources.Load("Nametag"), transform) as GameObject;
        //nameTag.transform.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>().text = aiDatabase.AI[newValue].name;
        //nameTag.transform.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>().color = Color.yellow;
    }

    private void UpdateWalkAnimation(float previousValue, float newValue)
    {
        AIAnimator.aiAnimator.SetFloat(ANIMATOR_PARAM_WALK_SPEED, currentVelocity.Value);
    }

    private void UpdateAnimation(byte previousValue, byte newValue)
    {
        /*switch(previousValue)
        {
            case (byte)AIanimationTriggerTypes.Jump:
                AIAnimator.SetBool("Jump", false);
            break;
        }*/

        switch (newValue)
        {
            /*case (byte)AIanimationTriggerTypes.Jump:
                AIAnimator.SetBool("Jump", true);
            break;*/

            // Dead is a trigger, no need to add it in previousValue as it's dead
            case (byte)AIanimationTriggerTypes.Dead:
                AIAnimator.SetTrigger("Dead");
            break;
        }
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    public void DoAnimationTriggerClientRpc(byte animationTriggerType)
    {
        AIAnimator.AnimationTrigger(animationTriggerType);
    }
    #endregion

    #region AIO
    public override void FreezeBrain()
    {
        if (!IsServer) return;
        base.FreezeBrain();
        if (currentTarget) ResetTarget();
        if (sensorDetector && isHostile) sensorDetector.enabled = false;
    }

    public override void UnfreezeBrain()
    {
        if (!IsServer) return;
        base.UnfreezeBrain();
        if (sensorDetector && isHostile) sensorDetector.enabled = true;
    }
    #endregion

    #region SetDestination, OnDeath, DestroyObject, Brain, Wander, Idle, WanderOrIdle, SetSpeedBasedOnDistance
    private bool SetDestination(Vector3 destination)
    {
        if (stopMovement) return false;
        if (destination != transform.position)   // Sometimes we just set their current pos to their current position incase their target fled away or so, we dont wanna trigger calcs for that
        {
            if (agent.SetDestination(destination))
            {
                #if UNITY_EDITOR
                //Debug.DrawLine(transform.position, destination, Color.white, 3f);
                #endif
                agent.speed = SetSpeedBasedOnDistance();
                return true;
            }
            else
            {
                Debug.DrawLine(transform.position, destination, Color.red, 3f);
                return false;
            }
        }
        else return false;
    }

    public override void OnDeath()
    {
        //transform.DOScale(new Vector3(0f, 0f, 0f), 5f).OnComplete(DestroyObject);
        if (!IsServer) return;
        if(sensorDetector) sensorDetector.enabled = false;
        GetComponent<BoxCollider>().enabled = false;
        GetComponent<NavMeshAgent>().enabled = false;
        currentTarget = null;
        brainFrozen = true;
    }

    private void Brain()
    {
        if (!IsServer) return;
        if(currentTarget == null && CurrentAIState == AIState.Following || CurrentAIState == AIState.Attacking && currentTarget == null)
        {
            ResetTarget();
            return;
        }

        float distanceFromPlayer = Vector3.Distance(transform.position, currentTarget.transform.position);
        if (distanceFromPlayer > maxRangeFromTarget)    // Player is out of range, reset target.
        {
            ResetTarget();
            return;
        }


        // Current Target is still in range of interest, execute logic.
        //print($"{gameObject.name}: Distance to target is {distanceFromPlayer}");
        if (distanceFromPlayer < combatRange)
        {
            //print($"{gameObject.name}: Distance to target is in attack range");
            if (IsTargetDead()) return;

            if (agent.isOnOffMeshLink) agent.destination = transform.position;
            OnStateChange(AIState.Attacking);
            CombatBrain(true);
        }
        else
        {
            if(CurrentAIState == AIState.Attacking) CurrentAIState = AIState.Following; // Hard change it, no need to statechange

            CombatBrain(false);

            if (currentOwner == null) SetDestination(currentTarget.transform.position);
        }
    }

    private float SetSpeedBasedOnDistance()
    {
        float newSpeed;
        float calculatedDistance;

        switch(CurrentAIState)
        {
            case AIState.Following:
                calculatedDistance = Vector3.Distance(transform.position, currentTarget.transform.position);
                if (calculatedDistance > maxRangeFromTarget)
                newSpeed = (_prevWalkSpeed + 3f);
                else if (calculatedDistance > 8)
                newSpeed = (_prevWalkSpeed + 1.9f);
                else newSpeed = (_prevWalkSpeed + 1.2f);
            break;

            case AIState.Attacking:
                calculatedDistance = Vector3.Distance(transform.position, currentTarget.transform.position);
                if (calculatedDistance > maxRangeFromTarget)
                newSpeed = (_prevWalkSpeed + 3f);
                else if (calculatedDistance > 8)
                newSpeed = (_prevWalkSpeed + 1.9f);
                else newSpeed = (_prevWalkSpeed + 1.2f);
            break;

            default:
                calculatedDistance = Vector3.Distance(transform.position, agent.destination);
                if (calculatedDistance > maxRangeFromTarget)
                newSpeed = 3f;
                else if (calculatedDistance > 8)
                newSpeed = 1.9f;
                else newSpeed = 1.2f;
            break;
        }

        return newSpeed;
    }

    private void WanderOrIdle(bool forceWander = false)
    {
        savedBrainTimer = 0f;
        if (forceWander)
        {
            Wander();
            return;
        }

        // Here I'll add some randomness, once in a while let it chill, then move on, etc. etc.
        float idleOrWander = Random.value;
        if (idleOrWander < 0.5f)
            OnStateChange(AIState.Wander);
        else
            OnStateChange(AIState.Idle);
    }

    /// <summary> Flee logic</summary>
    private void Flee()
    {
        // Needs a makeover so it heads in the direction it is fleeing, instead of random.
        if(!SetDestination(GenerateRandomDestination(transform.position, 50f)))
        {
            // Try again if first fails
            SetDestination(GenerateRandomDestination(transform.position, 50f));
        }
    }

    private Vector3 GenerateRandomDestination(Vector3 fromPos, float maxRangeFromPos = 25f)
    {
        Vector3 randomFleeLocation;
        randomFleeLocation = fromPos + Random.insideUnitSphere * (maxRangeFromPos);
        randomFleeLocation = new Vector3(randomFleeLocation.x, 0f, randomFleeLocation.z);

        return randomFleeLocation;
    }

    /// <summary> Wander logic</summary>
    private void Wander()
    {
        Vector3 randomWanderLocation;
        float rangeFromDestination = 10f;

        if (canLeaveSpawnArea)
        {
            rangeFromDestination = maxRangeFromTarget;
            randomWanderLocation = transform.position + Random.insideUnitSphere * maxRangeFromTarget;
        }
        else if (currentOwner != null)
        {
            rangeFromDestination = 10f;
            randomWanderLocation = currentOwner.transform.position + Random.insideUnitSphere * 10;
        }
        else
        {
            rangeFromDestination = maxRangeFromTarget;
            randomWanderLocation = spawnPos + Random.insideUnitSphere * maxRangeFromTarget;
        }

        randomWanderLocation = new Vector3(randomWanderLocation.x, 0f, randomWanderLocation.z);
        if (!SetDestination(GenerateRandomDestination(randomWanderLocation, rangeFromDestination)))
        {
            // Try again if first fails
            SetDestination(GenerateRandomDestination(randomWanderLocation, rangeFromDestination));
        }
    }

    /// <summary> Random Idle actions, could trigger random animation in here.</summary>
    private void Idle()
    {
        brainTimer += 1f;   // Add a extra second on their brain time

        if (maxExtraIdleAnims == 0) return;
        int randomIdleAnim = Random.Range(0, maxExtraIdleAnims + 2);    // We plus 4 on top, to be able to hit one of those 2, so nothing happens, to make it more random.
        switch(randomIdleAnim)
        {
            // Only default value, no other animations
            case 0:
                DoAnimationTriggerClientRpc((byte)AIanimationTriggerTypes.RandomIdle0);
            break;

            case 1:
                // This animation is an extra animation some AI's have, forexample jumping in place when idle, or happy or some shit, it will automatically go back to Idle after running it once.
                DoAnimationTriggerClientRpc((byte)AIanimationTriggerTypes.RandomIdle1);
            break;
        }
    }
    #endregion

    #region State related
    /// <summary> Transitioning to other states</summary>
    public void OnStateChange(AIState newState)
    {
        if (!IsServer) return;
        AIState _currentState = CurrentAIState;

        OnStateExit(_currentState, newState);       // OnStateExit event
        CurrentAIState = newState;                  // Set new state.
        OnStateEnter(newState, _currentState);      // OnStateEnter event
    }

    public void OnStateEnter(AIState newState, AIState oldState)
    {
        if (!IsServer) return;

        switch (newState)
        {
            case AIState.Idle:
            {
                agent.updateRotation = true;
                currentTarget = null;
                savedCombatTimer = 0f;
                savedBrainTimer = 0f;
                agent.stoppingDistance = 1.2f;
                brainTimer = Random.Range(minIdleTime, maxIdleTime);
                Idle();
                break;
            }

            case AIState.Following:
            {
                agent.updateRotation = false;
                brainTimer = 0.5f;
                break;
            }

            case AIState.Attacking:
            {
                agent.updateRotation = false;
                brainTimer = 0.5f;
                break;
            }

            case AIState.HeadHome:
            {
                currentTarget = null;
                agent.updateRotation = true;
                savedCombatTimer = 0f;
                savedBrainTimer = 0f;
                SetDestination(spawnPos);
                break;
            }

            case AIState.Wander:
            {
                agent.updateRotation = true;
                savedBrainTimer = 0f;
                savedCombatTimer = 0f;
                brainTimer = (_prevBrainTimer + 2f);
                Wander(); // Forces wander by param set to true.
                break;
            }

            case AIState.Flee:
            {
                agent.updateRotation = true;
                savedBrainTimer = 0f;
                savedCombatTimer = 0f;
                brainTimer = 0.5f;
                Flee(); // Forces wander by param set to true.
                break;
            }
        }
    }

    public void OnStateExit(AIState oldState, AIState newState)
    {
        switch (oldState)
        {
            case AIState.Idle:
            {
                // Exited from Idle state
                brainTimer = _prevBrainTimer;
                break;
            }

            case AIState.Following:
            {
                // Exited from Following state, stop destination.
                agent.speed = _prevWalkSpeed;
                brainTimer = _prevBrainTimer;
                break;
            }

            case AIState.Attacking:
            {
                // Exited from Attacking state
                agent.speed = _prevWalkSpeed;
                brainTimer = _prevBrainTimer;
                break;
            }

            case AIState.Wander:
            {
                // Exited from Attacking state
                brainTimer = _prevBrainTimer;       // Set brain timer back to what it is supposed to be.
                break;
            }

            case AIState.Flee:
            {
                savedCombatTimer = 0f;
                break;
            }

        }
    }
    #endregion

    #region Unity Events ( Awake, Update etc )
    public override void Awake()
    {
        base.Awake();
        if (!IsServer) return;

        if(!sensorDetector)
        {
            if (TryGetComponent(out Sensor _sensorDetector))
            {
                sensorDetector = _sensorDetector;
            }
        }
    }

    public override void FixedUpdate()
    {
        if(IsClient && !IsHost && !IsServer)
        {
            UpdateAITransform();
        }

        if (IsServer && brainFrozen) return;
        UpdateAITransform();
    }

    public override void Update()
    {
        if (!IsServer) return;
        if (brainFrozen) return;

        savedBrainTimer += Time.deltaTime;

        if (globalCoolDown > 0)
        {
            globalCoolDown -= Time.deltaTime;
        }

        if(currentOwner != null)
        {
            if (Input.GetMouseButtonDown(0))
            {
                RaycastHit hit;

                if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit, 100))
                {
                    SetDestination(hit.point);
                }
            }
        }

        if (currentTarget != null)
        {
            savedCombatTimer += Time.deltaTime;

            if(savedCombatTimer > 30f)  // AI has been chasing player for 30 seconds, is AI stuck, or is player above the AI where it cant reach him?
            {
                if (currentTarget.transform.position.y > 1.9 && agent.remainingDistance < combatRange + 5f)
                    ResetTarget();
            }

            if (savedBrainTimer < brainTimer) return;

            if(CurrentAIState == AIState.Flee)
            {
                // AI is fleeing from it's target.
                Flee();

                if(savedCombatTimer > maxFleeTime)
                {
                    // AI has fled for 10 seconds, perhaps some of its healing/defensive spells are ready now, go back and fight
                    OnStateChange(AIState.Following);
                }
            }

            if(CurrentAIState != AIState.Following && CurrentAIState != AIState.Attacking && CurrentAIState != AIState.Flee)
            {
                // If AI is not in one of the states -> following or attacking or fleeing but has a target, set it to follow something went wrong perhaps.
                OnStateChange(AIState.Following);
            }

            Brain();
            savedBrainTimer = 0f;
        }
        else
        {
            DoSensorPulseCheck();
            if (savedBrainTimer < 0.5f) return;

            if (CurrentAIState == AIState.Wander)
            {
                //float calcDistance = (transform.position - agent.destination).sqrMagnitude;
                if (agent.velocity == Vector3.zero || agent.hasPath == false)
                {
                    OnStateChange(AIState.Idle);
                    return;
                }

                if (savedBrainTimer > 15f) // AI bugged into a wall or something, never reached destination, send him home.
                {
                    OnStateChange(AIState.HeadHome);
                }
            }

            else if (CurrentAIState == AIState.Idle)
            {
                if (savedBrainTimer < brainTimer) return;

                // NPC is idle, let him idle more or go wander (random)
                WanderOrIdle(); 
            }

            else if (CurrentAIState == AIState.HeadHome)
            {
                // It's near home, let him wander around, else we just wait for him to come home in safety in upcoming brain ticks
                float calcDistance = (transform.position - spawnPos).sqrMagnitude;
                if (calcDistance < combatRange)
                {
                    OnStateChange(AIState.Wander);
                    savedBrainTimer = 0f;
                }
                else if (savedBrainTimer > 15f) // AI bugged into a wall or something, never reached destination, send him on wander mode, and hopefully he sorts himself out.
                {
                    OnStateChange(AIState.Wander);
                    savedBrainTimer = 0f;
                }
            }

            else if(CurrentAIState == AIState.Following || CurrentAIState == AIState.Attacking)
            {
                // Anti target disconnection bug (endless attack, follow fix)
                if (currentTarget == null)
                {
                    ResetTarget();
                }
            }

        }
    }
    #endregion

    #region Targeting
    public override void ResetTarget()
    {
        if (!IsServer) return;
        base.ResetTarget();

        currentTarget = null;
        savedCombatTimer = 0f;
        OnStateChange(AIState.Idle);
        AttackNearestTarget();
    }
    public override void TryTauntAI(GameObject tauntRequester)
    {
        if (!IsServer) return;
        base.TryTauntAI(tauntRequester);

        if(currentTarget == null)
        {
            currentTarget = tauntRequester;
            if (tauntRequester.TryGetComponent(out NetworkObject taunterNetwork))
            {
                savedClientId = taunterNetwork.OwnerClientId;                                       // Grab PlayerId of target & save it.
            }

            // If AI is currently moving somewhere, stop it immediately.
            if(agent.hasPath)
            {
                SetDestination(transform.position);
            }

            OnStateChange(AIState.Following);                                                       // Calculates what to do, based on distance etc.
            //print($"{gameObject.name}: Taunted target {currentTarget.name}");                     // Print.
        }
    }
    public override void UpdateTarget()
    {
        if (!IsServer) return;
        base.UpdateTarget();
        if (!isHostile || !sensorDetector) return;

        AttackNearestTarget();
    }
    private bool IsTargetDead()
    {
        if (currentTarget == null) return true;
        if (currentTarget.TryGetComponent(out BaseHealthController healthController))
        {
            if (!healthController.IsAlive)
            {
                ResetTarget();   // Our target died, reset target.
                return true;
            }
        }

        return false;
    }
    #endregion

    #region AI Transform Sync
    /// <summary> Updates position & rotation between server & client, both ways.</summary>
    private void UpdateAITransform()
    {
        if(IsServer)
        {
            if (currentTarget != null)
            {
                if(CurrentAIState == AIState.Following || CurrentAIState == AIState.Attacking)
                    LookAtTarget(currentTarget.transform);
            }

            if (Vector3.Distance(transform.position, oldAIPosition) > networkPositionThreshold)
            {
                AIPosition.Value = new Vector3(transform.position.x, transform.position.y, transform.position.z);
                oldAIPosition = transform.position;
            }

            if (Quaternion.Angle(transform.rotation, oldAIRotation) > networkRotationThreshold)
            {
                AIRotation.Value = transform.rotation;
                oldAIRotation = transform.rotation;
            }
        }

        if(!IsServer)
        {
            transform.position = Vector3.Lerp(transform.position, AIPosition.Value, smoothSyncValue * Time.fixedDeltaTime);
            transform.rotation = Quaternion.Lerp(transform.rotation, AIRotation.Value, smoothSyncValue * Time.fixedDeltaTime);
        }
    }
    #endregion

    #region Owner Stuff
    private void OnOwnerSet(GameObject ownerObject)
    {
        sensorDetector.IgnoreList.Add(currentOwner);
        currentOwnerId = currentOwner.GetComponent<NetworkedPlayer>().OwnerClientId;
    }
    #endregion

    #region Sensor Stuff
    private void DoSensorPulseCheck()
    {
        if (!isHostile) return;

        savedPulseTimer += Time.deltaTime;
        if (savedPulseTimer >= PulseTimer)
        {
            sensorDetector.Pulse();
            // After pulse, attack nearest.
            AttackNearestTarget();
            savedPulseTimer = 0f;
        }
    }

    private void AttackNearestTarget()
    {
        foreach (GameObject detectedTarget in sensorDetector.DetectedObjectsOrderedByDistance)
        {
            if (detectedTarget.TryGetComponent(out BaseHealthController healthController))
            {
                if (!healthController.IsAlive) continue;                                            // If target is alive, check if there are more detectedTargets, don't need to waste time on dead players
                currentTarget = detectedTarget;                                                     // Attach target on server
                savedClientId = healthController.OwnerClientId;                                     // Grab PlayerId of target & save it.

                Brain();                                                                            // Calculates what to do, based on distance etc.
                //print($"{gameObject.name}: Found target {currentTarget.name}");                   // Print.
                break;
            }
        }

    }
    #endregion

    #region CombatBrain / Abillity Functions
    private void CombatBrain(bool isCloseToTarget = false)
    {
        if (isCastingSpell > 0) return;         // AI is currently casting a spell
        if (aiAbillities.Count == 0) return;    // An AI that has not been setup properly, no auto attack, nothing.

        if (isCloseToTarget)
        {
            // Always fire auto attacks first, as we want to trigger it everytime it's available.
            if (!aiAbillities[AUTO_ATTACK].baseAbillity.abillityIsOnCooldown)
            {
                // Auto attack is not on cooldown, hit the target as we are close.
                aiAbillities[AUTO_ATTACK].baseAbillity.DoAbillity();
                isCastingSpell = AUTO_ATTACK;
            }
            else
            {
                // LOW HEALTH.
                if (aiHealth.Health < 55)
                {
                    if(aiHealth.Health < 15)
                    {
                        OnStateChange(AIState.Flee);
                    }
                    // We are low on health, perhaps use defensive, if we have it ready?
                    // If target is not using a defensive spell, then use defensive spell self.
                    if (ReturnCurrentTargetSpellType(currentTarget) != (int)AbillityType.Defensive)
                        UseReadyAbillity(AbillityType.Defensive, AbillityRangeType.Close);
                }
                else
                {
                    // We check if target is NOT using a defensive abillity, as it's stupid to cast a offensive ontop of a defensive, rather wait.
                    if (ReturnCurrentTargetSpellType(currentTarget) != (int)AbillityType.Defensive)
                        UseReadyAbillity(AbillityType.Offensive, AbillityRangeType.Close);
                }
            }
        }
        else
        {
            // Not close to target, perhaps we got some speed buff or ranged spells?
            // savedCombatTimer could be used to not instantly fire all spells etc.
            UseReadyAbillity(AbillityType.Offensive, AbillityRangeType.Ranged);
        }
    }

    private bool UseReadyAbillity(AbillityType abillityType, AbillityRangeType abillityRangeType)
    {
        if (globalCoolDown > 0) return false;
        int OffensiveSpell = ReturnSpellType(abillityType, abillityRangeType);
        if (OffensiveSpell != -1)
        {
            aiAbillities[OffensiveSpell].baseAbillity.DoAbillity();
            isCastingSpell = OffensiveSpell;
            globalCoolDown = 2.4f;  // For automated battle AI's we apply a higher global cool down, so they don't blow all their CD's at once.
            return true;
        }

        return false;
    }

    public int ReturnCurrentTargetSpellType(GameObject target)
    {
        if (target.TryGetComponent(out BaseAttackAI targetBrain))
        {
            return (int)targetBrain.aiAbillities[targetBrain.isCastingSpell].baseAbillity.abillityType;
        }

        return (int)AbillityType.None;
    }

    private int ReturnSpellType(AbillityType abillityType, AbillityRangeType abillityRange)
    {
        for (int i = 1; i < aiAbillities.Count; i++)    // Start 1, as 0 is always auto attack.
        {
            if (aiAbillities[i].baseAbillity.abillityType == abillityType && aiAbillities[i].baseAbillity.abillityRangeType == abillityRange)
            {
                if (!aiAbillities[i].baseAbillity.abillityIsOnCooldown)
                    return i;
            }
        }

        return (int)AbillityType.None;
    }
    #endregion
}
