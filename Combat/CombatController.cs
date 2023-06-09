using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine;
using UnityStandardAssets.CrossPlatformInput;
/// <summary> A weapon "slot" class, used for the player's weapon inventory</summary>
[System.Serializable]
public class WeaponSlot
{
    public string name = "No name";
    public BaseWeapon baseWeapon = null;
}

/// <summary> The base weapon controller to handle switching & adding new weapons</summary>
public class CombatController : NetworkBehaviour
{
    #region Variables, top of script
    /// <summary> The buffered data controller, used for switching to correct weapon on late joiners</summary>
    [Header("Components"), SerializeField]
    private BufferedPlayerDataController bufferedPlayerDataController = default;
    /// <summary> The weapon inventory. ALL BASEWEAPONS MUST GO IN HERE!</summary>
    [Header("Variables")]
    public List<GameObject> equippedWeapons = new List<GameObject>();       // Maybe change to baseweapon for quicker access to shitness from server side

    public List<WeaponSlot> weaponInventory = new List<WeaponSlot>();

    /// <summary> The current weapon in main-hand</summary>
    private WeaponSlot currentWeapon = new WeaponSlot();

    /// <summary> The current weapon in off-hand</summary>
    private WeaponSlot currentOffHandWeapon = new WeaponSlot();

    /// <summary> The index of the current weapon in the "weaponInventory"</summary>
    public int currentWeaponIndex = 0;
    private int _currentWeaponIndex = 0;

    /// <summary> The index of the current off hand weapon in the "weaponInventory"</summary>
    public int currentOffHandWeaponIndex = 0;
    private int _currentOffHandWeaponIndex = 0;
    #endregion

    #region Unity Events ( Awake, Update )
    private void Awake()
    {
        //Subscribe to buffered data event
        bufferedPlayerDataController.OnBufferedPlayerDataReceived += OnBufferedDataReceived;
    }

    // Update is called once per frame
    void Update()
    {
        if (!IsOwner) return;

        if(CrossPlatformInputManager.GetButtonDown("Fire1") && currentWeapon.baseWeapon != null)
        {
            if (!currentWeapon.baseWeapon.CanAttack()) return;
            AttackTriggerServerRpc();
        }

        if (CrossPlatformInputManager.GetButtonDown("Fire2") && currentOffHandWeapon.baseWeapon != null)
        {
            if (!currentOffHandWeapon.baseWeapon.CanAttack()) return;
            AttackTriggerServerRpc();
        }
    }
    #endregion

    #region Attack Triggers
    [ServerRpc]
    public void AttackTriggerServerRpc()
    {
        // Does not support dual wield
        if (currentWeapon.baseWeapon.CanAttack())
        {
            currentWeapon.baseWeapon.AttackTrigger();
        }
    }
    #endregion

    #region BufferedData, OnNetworkDespawn
    private void OnBufferedDataReceived(BufferedPlayerData buffered)
    {
        if(buffered.currentWeaponType != (byte)WeaponTypes.NONE)
            CreateWeapon(buffered.currentWeaponType, buffered.currentWeaponSkinId);

        /*if ((int)buffered.currentWeaponIndex > 0)
            AttachWeapon((int)buffered.currentWeaponIndex);

        if ((int)buffered.currentOffHandWeaponIndex > 0)
            AttachWeapon((int)buffered.currentOffHandWeaponIndex);*/
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
    }

    public override void OnNetworkDespawn()
    {
        //always unsubscribe to events
        bufferedPlayerDataController.OnBufferedPlayerDataReceived -= OnBufferedDataReceived;
    }
    #endregion

    #region Remove Weapon
    public void DisarmPlayer()
    {
        if (!IsOwner) return;

        if (currentWeaponIndex > 0)
            TryRemoveWeaponServerRpc(currentWeaponIndex);

        if (currentOffHandWeaponIndex > 0)
            AttachWeapon(currentOffHandWeaponIndex);
    }

    /// <summary> Removes a weapon, this can be called from the owner or a client, it'll network itself</summary>
    /// <param name="baseWeapon"></param>
    public void RemoveWeapon(BaseWeapon baseWeapon)
    {
        if (!baseWeapon)
            return;

        for (int i = 0; i < weaponInventory.Count; i++)
        {
            //hack hack, don't compare using names, compare using something else
            if (weaponInventory[i].baseWeapon && weaponInventory[i].baseWeapon.name == baseWeapon.name)
            {
                //both server & client can call this, so don't use "InvokeServerRPC" if it's the server self calling it
                TryRemoveWeaponServerRpc(i);
                return;
            }
        }
        Debug.LogError($"BaseWeapon {baseWeapon.name} is not found on the player's weapon inventory - aborting!");
    }

    /// <summary> Removes a weapon by index, this can be called from the owner or a client, it'll network itself</summary>
    /// <param name="baseWeapon"></param>
    public void RemoveWeaponByName(string name)
    {
        for (int i = 0; i < weaponInventory.Count; i++)
        {
            //hack hack, don't compare using names, compare using something else
            if (weaponInventory[i].baseWeapon && weaponInventory[i].baseWeapon.name.Contains(name))
            {
                //both server & client can call this, so don't use "InvokeServerRPC" if it's the server self calling it
                TryRemoveWeaponServerRpc(i);
                return;
            }
        }
    }

    /// <summary> Server RPC to try and remove a weapon, based on index. </summary>
    /// <param name="weaponIndex"></param>
    /// <param name="switchTo"></param>
    [ServerRpc(RequireOwnership = false)]
    private void TryRemoveWeaponServerRpc(int weaponIndex)
    {
        // Run it on the server as well, so server knows what weapon he has access to
        if (!IsHost)
        {
            UnAttachWeapon(weaponIndex);
        }

        //store in the buffered data, the removal of the weapon
        if (IsServer)
        {
            var newBufferedData = bufferedPlayerDataController.bufferedPlayerData;
            if(newBufferedData.currentWeaponIndex == weaponIndex)
            {
                newBufferedData.currentWeaponIndex = 0;
            }
            if(newBufferedData.currentOffHandWeaponIndex == weaponIndex)
            {
                newBufferedData.currentOffHandWeaponIndex = 0;
            }
            bufferedPlayerDataController.bufferedPlayerData = newBufferedData;
            bufferedPlayerDataController.BufferedPlayerData.Value = newBufferedData;
        }

        RemoveWeaponClientRpc(weaponIndex);
    }

    /// <summary> The client rpc that removes the weapon on all clients</summary>
    /// <param name="weaponIndex"></param>
    /// <param name="switchTo"></param>
    [ClientRpc]
    private void RemoveWeaponClientRpc(int weaponIndex)
    {
        if (currentWeaponIndex == weaponIndex)
        {
            currentWeaponIndex = 0;
        }

        if (currentOffHandWeaponIndex == weaponIndex)
        {
            currentOffHandWeaponIndex = 0;
        }

        UnAttachWeapon(weaponIndex);
    }

    private void UnAttachWeapon(int weaponIndex)
    {
        //weaponInventory[weaponIndex].baseWeapon.OnWeaponUnequipped(this);
    }
    #endregion

    #region Add Weapon
    [ServerRpc]
    public void CreateWeaponServerRpc(int itemId, byte weaponSkin)
    {
        print($"CreateWeaponServerRpc {itemId} {weaponSkin}");
        CreateWeapon(itemId, weaponSkin);
    }

    [ClientRpc]
    public void CreateWeaponClientRpc(int itemId, byte weaponSkin)
    {
        CreateWeapon(itemId, weaponSkin);
    }

    private void CreateWeapon(int itemId, byte weaponSkin)
    {
        print($"CreateWeapon {itemId} {weaponSkin}");
        GameObject tmpWeapon;
        var itemDatabase = NetworkManager.Singleton.gameObject.GetComponent<GlobalDatabaseManager>().globalItems;
        var selectedItem = itemDatabase.items[itemId].baseItem.GetComponent<BaseWeaponItem>();

        switch ((byte)selectedItem.weaponType)
        {
            case (byte)WeaponTypes.MELEE:
                tmpWeapon = new GameObject("MeleeWeapon");
                tmpWeapon.transform.parent = transform;
                tmpWeapon.AddComponent<BaseMeleeWeapon>();

                // Copy values from the item
                var weaponInfo = tmpWeapon.GetComponent<BaseMeleeWeapon>();
                weaponInfo.SplashDamage = selectedItem.shouldSplashDamage;
                weaponInfo.sphereCastRadius = selectedItem.sphereCastRadius;
                weaponInfo.sphereCastDistance = selectedItem.sphereCastDistance;
                weaponInfo.sphereCastHeightOffset = selectedItem.sphereCastHeightOffset;
                weaponInfo.hitRegistrationLayerMask = selectedItem.hitRegistrationLayerMask;

                equippedWeapons.Add(tmpWeapon);
                print($"CreateWeapon WeaponTypes.MELEE {itemId} {weaponSkin}");
                break;

            case (byte)WeaponTypes.RANGED:
                tmpWeapon = new GameObject("RangedWeapon");
                tmpWeapon.transform.parent = transform;
                tmpWeapon.AddComponent<BaseProjectileWeapon>();
                equippedWeapons.Add(tmpWeapon);
                print($"CreateWeapon WeaponTypes.RANGED {itemId} {weaponSkin}");
                break;

            case (byte)WeaponTypes.SHIELD:
                tmpWeapon = new GameObject("Shield");
                tmpWeapon.transform.parent = transform;
                tmpWeapon.AddComponent<BaseShieldWeapon>();
                equippedWeapons.Add(tmpWeapon);
                print($"CreateWeapon WeaponTypes.SHIELD {itemId} {weaponSkin}");
                break;
        }

        // Spawn the model/skin here.

        if (IsServer)
        {
            // BufferedData
            var newBufferedData = bufferedPlayerDataController.bufferedPlayerData;
            newBufferedData.currentWeaponType = (byte)selectedItem.weaponType;
            newBufferedData.currentWeaponSkinId = weaponSkin;
            bufferedPlayerDataController.bufferedPlayerData = newBufferedData;
            bufferedPlayerDataController.BufferedPlayerData.Value = newBufferedData;
        }
    }

    /// <summary> Adds a weapon, this can be called from the owner or a client, it'll network itself</summary>
    /// <param name="baseWeapon"></param>
    public void AddWeapon(BaseWeapon baseWeapon)
    {
        if (!baseWeapon)
            return;

        for (int i = 0; i < weaponInventory.Count; i++)
        {
            //hack hack, don't compare using names, compare using something else
            if (weaponInventory[i].baseWeapon && weaponInventory[i].baseWeapon.name == baseWeapon.name)
            {
                //both server & client can call this, so don't use "InvokeServerRPC" if it's the server self calling it
                TryAddWeaponServerRpc(i);
                return;
            }
        }
        Debug.LogError($"BaseWeapon {baseWeapon.name} is not found on the player's weapon inventory - aborting!");
    }

    /// <summary> Adds a weapon by name</summary>
    public void AddWeaponByName(string name)
    {
        for (int i = 0; i < weaponInventory.Count; i++)
        {
            //hack hack, don't compare using names, compare using something else
            if (weaponInventory[i].baseWeapon && weaponInventory[i].baseWeapon.name.Contains(name))
            {
                //both server & client can call this, so don't use "InvokeServerRPC" if it's the server self calling it
                TryAddWeaponServerRpc(i);
                return;
            }
        }
        Debug.LogError($"AddWeaponByName: Index {name} not found on the player's weapon inventory - aborting!");
    }

    /// <summary> Server RPC to try and add a weapon, based on index. This should not be invoked manually, use "public void AddWeapon(BaseWeapon baseWeapon, bool switchTo = true)"</summary>
    /// <param name="weaponIndex"></param>
    [ServerRpc(RequireOwnership = false)]
    private void TryAddWeaponServerRpc(int weaponIndex)
    {
        // Store in the buffered data
        if (IsServer)
        {
            var newBufferedData = bufferedPlayerDataController.bufferedPlayerData;
            newBufferedData.currentWeaponIndex = (byte)currentWeaponIndex;
            newBufferedData.currentOffHandWeaponIndex = (byte)currentOffHandWeaponIndex;
            bufferedPlayerDataController.bufferedPlayerData = newBufferedData;
            bufferedPlayerDataController.BufferedPlayerData.Value = newBufferedData;
        }

        // We only run it on the server if we are not a host
        if (!IsHost)
        {
            AttachWeapon(weaponIndex);
        }

        AddWeaponClientRpc(weaponIndex);
    }

    /// <summary> The client rpc that adds the weapon on all clients, this should not be invoked manually, use "public void AddWeapon(BaseWeapon baseWeapon, bool switchTo = true)"</summary>
    /// <param name="weaponIndex"></param>
    [ClientRpc]
    private void AddWeaponClientRpc(int weaponIndex)
    {
        AttachWeapon(weaponIndex);
    }

    /// <summary> Switches to a certain weapon - NOT NETWORKED - call this from a networked place</summary>
    /// <param name="weaponIndexInList"></param>
    private void AttachWeapon(int weaponIndexInList)
    {
        if (weaponIndexInList == currentWeaponIndex) // Switching to the same weapon
            return;

        if (currentWeaponIndex == 0 && currentOffHandWeaponIndex == 0)
        {
            if (IsServer)
            {
                var newBufferedData = bufferedPlayerDataController.bufferedPlayerData;
                newBufferedData.currentWeaponIndex = (byte)weaponIndexInList;
                bufferedPlayerDataController.bufferedPlayerData = newBufferedData;
                bufferedPlayerDataController.BufferedPlayerData.Value = newBufferedData;
            }

            currentWeapon = weaponInventory[weaponIndexInList];
            currentWeaponIndex = weaponIndexInList;

            if (weaponInventory[weaponIndexInList] != null)
            {
                if (currentWeapon.baseWeapon)
                {
                    currentWeapon.baseWeapon.attackButton = "Fire1";

                    if (currentWeapon.baseWeapon.gameObject.name.Contains("(Bow)") || currentWeapon.baseWeapon.gameObject.name.Contains("(Shield)"))
                    {
                        currentWeapon.baseWeapon.OnWeaponEquipped(this, "+ L Hand");
                    }
                    else
                    {
                        currentWeapon.baseWeapon.OnWeaponEquipped(this, "+ R Hand");
                    }

                    if (currentWeapon.baseWeapon.gameObject.name.Contains("(1H)"))
                    {
                        currentWeapon.baseWeapon.weaponAnimationTriggerType = AnimationTriggerRPCType.AttackRightHand;
                    }
                }
            }
        }

        else if (currentWeaponIndex != 0 && currentOffHandWeaponIndex == 0)
        {
            if (IsServer)
            {
                var newBufferedData = bufferedPlayerDataController.bufferedPlayerData;
                newBufferedData.currentOffHandWeaponIndex = (byte)weaponIndexInList;
                bufferedPlayerDataController.bufferedPlayerData = newBufferedData;
                bufferedPlayerDataController.BufferedPlayerData.Value = newBufferedData;
            }

            currentOffHandWeapon = weaponInventory[weaponIndexInList];
            currentOffHandWeaponIndex = weaponIndexInList;

            if (weaponInventory[weaponIndexInList] != null)
            {
                if (currentOffHandWeapon.baseWeapon)
                {
                    currentOffHandWeapon.baseWeapon.OnWeaponEquipped(this, "+ L Hand");
                    currentOffHandWeapon.baseWeapon.attackButton = "Fire2";

                    if (currentOffHandWeapon.baseWeapon.gameObject.name.Contains("(1H)"))
                    {
                        currentOffHandWeapon.baseWeapon.weaponAnimationTriggerType = AnimationTriggerRPCType.AttackLeftHand;
                    }
                }
            }
        }
        else print($"ERROR: Something weird happened, player tried to equip a weapon, but already have both hands full, what happened?");
    }
    #endregion

    #region Cycle Loadout
    /// <summary> ServerRpc -> tries to cycle through weapons based on an index that's the weaponInventory index</summary>
    /// <param name="weaponIndexInList"></param>
    [ServerRpc]
    private void TryCycleWeaponsServerRpc(int weaponIndexInList)
    {
        if(!IsHost)
        {
            CycleLoadout();
        }
        CycleWeaponsClientRpc(weaponIndexInList);
    }

    /// <summary> Client RPC to cycle though weapon based on an index that's the weaponInventory index</summary>
    /// <param name="weaponIndexInList"></param>
    [ClientRpc]
    private void CycleWeaponsClientRpc(int weaponIndexInList)
    {
        CycleLoadout();
    }

    private void CycleLoadout()
    {
        bool didHeHaveAnything = false;
        if (currentWeaponIndex != 0) // Player has something in hand.
        {
            _currentWeaponIndex = currentWeaponIndex;
            currentWeaponIndex = 0;
            //currentWeapon.baseWeapon.OnWeaponUnequipped(this);
            didHeHaveAnything = true;
        }

        if (currentOffHandWeaponIndex != 0) // Player has something in off-hand.
        {
            _currentOffHandWeaponIndex = currentOffHandWeaponIndex;
            currentOffHandWeaponIndex = 0;
            //currentOffHandWeapon.baseWeapon.OnWeaponUnequipped(this);
            didHeHaveAnything = true;
        }

        if (didHeHaveAnything)
        {
            AttachWeapon(0); // Unarmed.

            if (IsServer)
            {
                var newBufferedData = bufferedPlayerDataController.bufferedPlayerData;
                newBufferedData.currentWeaponIndex = (byte)currentWeaponIndex;
                newBufferedData.currentOffHandWeaponIndex = (byte)currentOffHandWeaponIndex;
                bufferedPlayerDataController.bufferedPlayerData = newBufferedData;
                bufferedPlayerDataController.BufferedPlayerData.Value = newBufferedData;
            }
        }
        else
        {
            // Player was unarmed, he wants to switch back to his weapons.
            // We can create cool stuff, like putting the weapons on his back, or onto his hip or some shit in the future, so people know, okay this guy is actually armed & ready.
            if (_currentWeaponIndex != 0)
                AttachWeapon(_currentWeaponIndex);

            if (_currentOffHandWeaponIndex != 0)
                AttachWeapon(_currentOffHandWeaponIndex);
        }
    }
    #endregion
}
