using UnityEngine;
using Unity.Netcode;
using SensorToolkit;

public enum BotState
{
    Idle,
    Wander,

    Follow,

    Attack,
    Flee,

    ReturnHome,
}

public class BaseBot : NetworkBehaviour
{
    /* 
     *          RE-WORK OF OLD AI SYSTEM
     * 
     *          Keep this BaseBot as clean as possible
     *          Add stuff in the derived scripts
     *          Make it as dynamic as possible so it wont be spaghetti code
     * 
     */

    [Header("Bot Components")]
    [SerializeField] public BotDatabase botDatabase = null;                         // Database (This has to be added manually!)
    [SerializeField] private BotAnimator botAnimator = null;                        // Animator
    [SerializeField] private BotHealthController botHealthController = null;        // Health Controller
    [SerializeField] public RaySensor botRaySensor = null;                          // Ray Sensor
    [SerializeField] public RangeSensor botRangeSensor = null;                      // Range Sensor
    [SerializeField] public BotCombat botCombat = null;                             // Combat
    [SerializeField] public BotMovement botAgent = null;                            // Runtime NavMesh building https://forum.unity.com/threads/runtime-navmesh-baking.1368801/

    [Space] [Header("Bot Networked Variables")]
    /// <summary> Contains the ID of the bot from the bot database, to easily grab model & what is needed</summary>
    [SerializeField] private NetworkVariable<byte> botId = new NetworkVariable<byte>(255);
    /// <summary> Contains the velocity of the bot, to use with the walk/run animation</summary>
    [SerializeField] private NetworkVariable<float> botVelocity = new NetworkVariable<float>(0);
    /// <summary> Contains the ID of the current botAnimation that it's using</summary>
    [SerializeField] private NetworkVariable<byte> botAnimId = new NetworkVariable<byte>(255);
    /// <summary> Contains the ID of the current weapon the bot is using</summary>
    [SerializeField] private NetworkVariable<byte> botWeaponId = new NetworkVariable<byte>(255);

    private static int ANIMATOR_PARAM_WALK_SPEED = Animator.StringToHash("WalkSpeed");

    [Space]
    [Header("Bot Brain")]
    [SerializeField] public GameObject botTarget = null;
    [SerializeField] private bool brainActive = false;
    [SerializeField, Range(0f, 5f)] public float brainReactionTime = 0.5f;
    private float brainTimer = 0f;

    [Space]
    [Header("Bot Equipment")]
    public BaseWeapon[] botWeaponry;
    public BotState CurrentBotState { get; set; }

    #region Standard functions (Awake, Update etc)
    public virtual void FixedUpdate()
    {
        if (!IsServer) return;
        if (!brainActive) return;
        if (!botDatabase.Bot[botId.Value].usesBrain) return;

        // Velocity, prob updating too fast, invoke or add timer
        if(botDatabase.Bot[botId.Value].usesMovement)
        {
            if (botVelocity.Value != botAgent.botMesh.velocity.magnitude)
            {
                botVelocity.Value = botAgent.botMesh.velocity.magnitude;
            }
        }

        // Combat
        if(botDatabase.Bot[botId.Value].usesCombat)
        {
            if (botCombat.botIsInCombat && botAgent.isBotFocused)
            {
                if (botDatabase.Bot[botId.Value].usesRaySensor && botRaySensor.GetDetected().Count > 0 && !botRaySensor.IsObstructed)
                    botCombat.AttackCheck();
            }
        }

        // Brain timer
        BrainTimer();
    }

    public virtual void BrainTimer()
    {
        brainTimer += Time.deltaTime;
        if (brainTimer >= brainReactionTime)
        {
            BotBrain();
            if (botDatabase.Bot[botId.Value].usesSensor) BotSensor();
        }
    }

    public override void OnNetworkDespawn()
    {
        botId.OnValueChanged        -= OnBotIdChange;
        botVelocity.OnValueChanged  -= OnBotVelocityChange;
        botAnimId.OnValueChanged    -= OnBotAnimationChange;
        botWeaponId.OnValueChanged  -= OnBotWeaponChange;
    }

    private void OnBotAnimationChange(byte prevAnim, byte newAnim)
    {
        switch(newAnim)
        {
            case (byte)AIanimationTriggerTypes.Dead:
            botAnimator.SetTrigger("Dead");
            break;
        }
    }

    private void OnBotVelocityChange(float prevVelocity, float newVelocity)
    {
        botAnimator.animator.SetFloat(ANIMATOR_PARAM_WALK_SPEED, botVelocity.Value);
    }

    private void OnBotIdChange(byte prevBotId, byte newBotId)
    {
        if (newBotId == 255) return;
        SetupBot(newBotId);
    }

    private void OnBotWeaponChange(byte prevWeapon, byte newWeapon)
    {
        if (newWeapon == 255) return;

        // Make auto detection for bows & melee (Bows go in left hand, melee in right ++)
        // Just testing if this will fix the RPC bug 
        GameObject leftHand = SearchGameObject(gameObject, "+ L Hand");

        if (leftHand != null)
        {
            foreach (BaseWeapon weapon in botWeaponry)
            {
                weapon.transform.SetParent(leftHand.transform, false);
                weapon.gameObject.SetActive(true);
                // hide all others
                if (IsServer)
                {
                    // Does not support dual wield or secondary atm. this is just temporary needs an upgrade
                    botCombat.botMainWeapon = weapon;
                }
            }
        }
        else print("Did not find + L Hand");
    }
    #endregion

    #region Bot Spawning / Setup
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        botId.OnValueChanged        += OnBotIdChange;
        botVelocity.OnValueChanged  += OnBotVelocityChange;
        botAnimId.OnValueChanged    += OnBotAnimationChange;
        botWeaponId.OnValueChanged  += OnBotWeaponChange;

        if (!botDatabase) print($"WARNING: Bot {gameObject.name} has no database attached (BROKEN)");
        if (IsServer && botId.Value == 255) botId.Value = (byte)Random.Range(0, botDatabase.Bot.Length - 1);
    }

    public virtual void SetupBot(byte botId)
    {
        GameObject tmpBot = Instantiate(botDatabase.Bot[botId].BotModel, transform);

        if (!botHealthController)
        {
            TryGetComponent<BotHealthController>(out botHealthController);
        }

        if (!botAnimator && botDatabase.Bot[botId].usesAnimations)
        {
            botAnimator = gameObject.AddComponent(typeof(BotAnimator)) as BotAnimator;
        }

        botAnimator.animator.runtimeAnimatorController = botDatabase.Bot[botId].BotController;
        botAnimator.animator.GetComponent<Animator>().avatar = botDatabase.Bot[botId].BotAvatar;
        botAnimator.animator.GetComponent<Animator>().Rebind();

        if (IsServer)
        {
            if (botDatabase.Bot[botId].usesSensor && botRangeSensor == null && botDatabase.Bot[botId].usesRangeSensor)
            {
                botRangeSensor = gameObject.AddComponent(typeof(RangeSensor)) as RangeSensor;
                if (botRangeSensor.SensorRange == 0) print($"{gameObject.name} ID:{botId} - Has no SensorRange set");

                botRangeSensor.SensorUpdateMode = RangeSensor.UpdateMode.Manual;
                botRangeSensor.SensorRange = botDatabase.Bot[botId].sensorRange;
                botRangeSensor.DetectsOnLayers = LayerMask.GetMask("Player");
                botRangeSensor.BlocksLineOfSight = LayerMask.GetMask("Grounded") + LayerMask.GetMask("Tree");
                botRangeSensor.RequiresLineOfSight = true;
            }

            if (botDatabase.Bot[botId].usesSensor && botRaySensor == null && botDatabase.Bot[botId].usesRaySensor)
            {
                botRaySensor = gameObject.AddComponent(typeof(RaySensor)) as RaySensor;
                if (botRaySensor.Length == 0) print($"{gameObject.name} ID:{botId} - Has no RaySensor Length set");

                botRaySensor.SensorUpdateMode = RaySensor.UpdateMode.Manual;
                botRaySensor.Length = botDatabase.Bot[botId].rayRange;
                botRaySensor.DetectsOnLayers = LayerMask.GetMask("Player");
                botRaySensor.ObstructedByLayers = LayerMask.GetMask("Grounded") + LayerMask.GetMask("Tree");
            }

            if (!botAgent && botDatabase.Bot[botId].usesMovement)
            {
                botAgent = gameObject.AddComponent(typeof(BotMovement)) as BotMovement;
            }

            if (!botCombat && botDatabase.Bot[botId].usesCombat)
            {
                botCombat = gameObject.AddComponent(typeof(BotCombat)) as BotCombat;
                botWeaponId.Value = 0;
                if (botDatabase.Bot[botId].botAttackType == BotAttackTypes.Ranged)
                    botAgent.isRanged = true;
            }

            CurrentBotState = BotState.Idle;
        }

        gameObject.name = botDatabase.Bot[botId].name;
        brainActive = true;
    }

    protected GameObject SearchGameObject(GameObject root, string gameObjectName)
    {
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform item in children)
        {
            if (item.name == gameObjectName)
            {
                return item.gameObject;
            }
        }

        return null;
    }
    #endregion

    #region Bot Brain Functions (Brain & Senses)
    public virtual void OnBotDeath()
    {
        if (!IsServer) return;

        botVelocity.Value = 0;
        brainActive = false;

        if(botCombat)
        {
            botCombat.botTarget = null;
            botCombat.botIsInCombat = false;
        }

        if(botAgent)
        {
            botAgent.botTarget = null;
            botAgent.isBotFocused = false;
            botAgent.botMesh.isStopped = true;
        }

        if(botRangeSensor)
        {
            botRangeSensor.enabled = false;
        }

        if (botRaySensor)
        {
            botRaySensor.enabled = false;
        }

        LootDropCheck();
    }

    public virtual void LootDropCheck()
    {
        if (TryGetComponent(out LootTable loot))
        {
            if (loot.lootTable.Length < 1) return;  // No loot was added.
            if (loot.lootToDrop == 0) return;       // This mobs randomness made it so it had no items to drop.

            // Spawn the backpack directly so we can fill it up, if the mob had no items to drop, this would never happen.
            GameObject entityBackpack = Instantiate(Resources.Load("Containers/container.Backpack") as GameObject);
            entityBackpack.transform.position = transform.position;
            var containerInventory = entityBackpack.GetComponent<ContainerInventory>();
            entityBackpack.GetComponent<NetworkObject>().Spawn();

            // Loot has been found in the loottable.
            byte itemsAdded = 0;
            for (int i = 0; i < loot.lootToDrop; i++)
            {
                int itemIdToAdd = loot.GetDropChanceLootFromTable();    // Return a random loot from loottable that has dropchance values (0-100)

                if (itemIdToAdd != -1)
                {
                    if (containerInventory.containerInv.Count == 0)
                    {
                        containerInventory.containerInv.Add(new BufferedContainerData((byte)loot.lootTable[itemIdToAdd].item.itemID, (byte)Random.Range(1, loot.lootTable[itemIdToAdd].itemQuantityMaxAmount)));
                        itemsAdded++;
                    }
                    else containerInventory.AddItem((int)loot.lootTable[itemIdToAdd].item.itemID, Random.Range(1, loot.lootTable[itemIdToAdd].itemQuantityMaxAmount));
                }
            }

            containerInventory.containerSize.Value = itemsAdded;    // Sets the container inventory size after adding all the items
            containerInventory.containerShouldDestroy = true;

            if (itemsAdded == 0) // Just incase there was some randomness mistake or something.
            {
                print($"AIHealthController.cs: Randomness mistake on {gameObject.name}, was set to drop items, but didn't return any through GetDropChanceLootFromTable");
                Destroy(entityBackpack);
                return;
            }
        }
    }

    public virtual void BotBrain()
    {
        //  Calculate everything that should be done after the reaction time.
        //  Override in other bot scripts and run Base, as we reset timer here.
        brainTimer = 0f;
    }

    public virtual void BotSensor()
    {
        //  Calculate Sensor stuff here, this is only run if sensor is enabled (from timer function)
        //  Override in other bot scripts and run Base, as we run Pulse from here, then just grab what is needed (nearest etc etc)
        //botSensor.Pulse();
    }

    public virtual void BotAggro(GameObject targetGameObject)
    {
        if (!IsServer) return;
        if (botTarget == targetGameObject) return;

        botTarget = targetGameObject;
    }

    public virtual void BotResetAggro()
    {
        if (!IsServer) return;
        if (botTarget == null) return;  // Cause we reset when it has no "zoned-in" players

        botTarget = null;
        CurrentBotState = BotState.Idle;

        if (botAgent)
        {
            botAgent.botTarget = null;
        }

        if (botCombat)
        {
            botCombat.botTarget = null;
            botCombat.botIsInCombat = false;
        }
    }

    public virtual void ToggleBrain()
    {
        if (brainActive) brainActive = false;
        else brainActive = true;
    }

    #endregion

    #region Animations
    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    public void BotAnimationTriggerClientRpc(byte animationTriggerType)
    {
        botAnimator.AnimationTrigger(animationTriggerType);
    }
    #endregion
}