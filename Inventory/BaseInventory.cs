using System.Collections.Generic;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine;
using TMPro;

public enum InventoryType
{
    ClientInventory = 0,
    NetworkInventory = 1,
}
public class BaseInventory : NetworkBehaviour
{
    [Header("Item Database")]
    public ItemDatabase database;

    [Header("UI Variables")]
    public GameObject inventoryHUD = null;                                                      // The parent of the UI, on players it's the InventoryHUD, on containers it should instantiate an UI, under PlayerHUD.
    [HideInInspector] protected GameObject[] uiSlots = new GameObject[MAX_INVENTORY_SIZE];      // The UI slots, these are instantiated automatically.

    [Header("Inventory Variables")]
    public List<InventorySlot> inventory = new List<InventorySlot>();                           // List of InventorySlots
    protected const int MAX_INVENTORY_SIZE = 20;                                                // Needs 1 more, that has currentInventorySize or something, so there is a max, and then current size of what-ever inventory/container is open.
    public int currentInventorySize = 12;                                                       // The amount of slots this player / container has, can be increased on the run.
    public InventoryType inventoryType = InventoryType.ClientInventory;                         // If this is a networked inventory (chests, containers etc) or client inventory (player inventory)

    [Header("Drag & Drop")]
    public GameObject DraggableObject = null;

    /*
     * 
     *  To load inventory slots back in where player left game from, save inventory slots and the current number in loop, and create a new function that adds an item to a desired slot.
     *  This desired slot function should only be called on logging in though.
     * 
     * 
     * TO-DO
     * 
     *  When PlayerInventory & Networked Inventories are finished, then everything in here, can be moved to PlayerInventory & keep this BaseInventory clean, it will save some unused variables such as PlayerInventory is not used in the networked part etc etc., and just override everything.
     *  
     */

    #region Basic Inventory Functions
    public virtual bool IsInventoryFull()
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            if (inventory[i].baseItem == null) return false;
        }
        return true;
    }


    [ServerRpc]
    public virtual void MoveSlotServerRpc(int fromSlot, int toSlot)
    {
        inventory[toSlot].baseItem = inventory[fromSlot].baseItem;
        inventory[toSlot].baseItem.itemID = inventory[fromSlot].baseItem.itemID;
        inventory[toSlot].itemQuantity = inventory[fromSlot].itemQuantity;

        inventory[fromSlot].baseItem = null;
        inventory[fromSlot].itemQuantity = 0;
    }

    [ServerRpc]
    public virtual void DropItemServerRpc(int inventorySlot)
    {
        GameObject entityGroundItem = Instantiate(Resources.Load("Items/GroundItem") as GameObject);
        entityGroundItem.transform.position = transform.position + transform.forward * 0.7f;
        entityGroundItem.GetComponent<NetworkObject>().Spawn();
        entityGroundItem.GetComponent<GroundItem>().networkedItemId.Value = (int)inventory[inventorySlot].baseItem.itemID;

        if (entityGroundItem.TryGetComponent(out GroundItem groundItem))
        {
            groundItem.itemShouldDestroy = true;
            groundItem.itemDestroyTime = 15 * 60;   // 15 minutes
            groundItem.networkedItemId.Value = (int)inventory[inventorySlot].baseItem.itemID;
            groundItem.quantity = inventory[inventorySlot].itemQuantity;
        }
    }

    [ClientRpc]
    public virtual void ClearInventoryClientRpc(ClientRpcParams clientRpcParams = default)
    {
        ClearInventory();
    }

    public int GetUsedInventorySlots()
    {
        int usedSlots = 0;
        for (int i = 0; i < inventory.Count; i++)
        {
            if (inventory[i].baseItem != null) usedSlots++;
        }
        return usedSlots;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsLocalPlayer || IsServer)
            SetupInventorySlots();
    }

    /// <summary> Update, only for testing purposes</summary>
    private void Update()
    {
        if (inventoryHUD == null) return;
        if (!inventoryHUD.activeSelf) return;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            RemoveItem((int)ItemID.Apple, 13);
        }
    }

    #endregion

    #region Add Items
    /// <summary> Adds an item to the players inventory, based on itemID</summary>
    public virtual void AddItem(int itemId, int quantity)
    {
        BaseItem _Item = ReturnBaseItemFromID(itemId);
        if (_Item == null) return;                      // Something went wrong, perhaps debug here.
        if (IsInventoryFull()) return;                  // Could create function here that drops the item on the floor.

        int loopFrom = LookForItemReturnFreeSlot(itemId);   // Check if container has the item, and if there is more space in it, will return the slotId, then start looping from that slot

        for (int i = 0; i < inventory.Count; i++)
        {
            if (i < loopFrom) continue; // We skip till we hit the loopFrom (LookForItemReturnFreeSlot returns 0 if nothing was found, so it will just act normal incase)
            if (inventory[i].baseItem == null)
            {
                // Free slot.
                inventory[i].baseItem = _Item;

                // MaxQuantity check even though this should not be possible, but incase we change item quantity stack in a update.
                if ((inventory[i].itemQuantity + quantity) > inventory[i].baseItem.maxQuantity)
                {
                    int calculatedLeftOver = (quantity + inventory[i].itemQuantity) - inventory[i].baseItem.maxQuantity;
                    inventory[i].itemQuantity = inventory[i].baseItem.maxQuantity;
                    quantity = calculatedLeftOver;
                    RefreshSlotUI(i);
                    continue;
                }

                inventory[i].itemQuantity = quantity;
                RefreshSlotUI(i);
                break;
            }

            if ((ItemID)inventory[i].baseItem.itemID == (ItemID)itemId)            // Got match
            {
                // Non-stackable types such as weaponry, should just break the loop & add it directly as they cannot stack.
                //if((ItemType)inventory[i].baseItem.itemID == ItemType.Weapon) break; // Not really needed, never gonna add more than 1 weapon (quantity) at a time anyway

                // Check max stack size & do logic
                if ((inventory[i].itemQuantity + quantity) > inventory[i].baseItem.maxQuantity)
                {
                    int calculatedLeftOver = (quantity + inventory[i].itemQuantity) - inventory[i].baseItem.maxQuantity;
                    inventory[i].itemQuantity = inventory[i].baseItem.maxQuantity;
                    RefreshSlotUI(i);
                    quantity = calculatedLeftOver;
                    continue;
                }
                else inventory[i].itemQuantity += quantity;

                RefreshSlotUI(i);
                break;
            }
        }

        print($"(INV) Added {ReturnItemNameFromID(itemId)} (ID: {itemId}) with amount ({quantity}) to {gameObject.name}");
    }
    #endregion

    #region Remove Items
    /// <summary> Removes an item from the player based on ItemID & amount.</summary>
    public virtual void RemoveItem(int itemId, int quantity)
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            if (inventory[i].baseItem == null) continue;
            if ((ItemID)inventory[i].baseItem.itemID != (ItemID)itemId) continue;     // No match.
            if (quantity > inventory[i].itemQuantity)
            {
                // Calculate leftovers, and check if there are more of that item in his inventory, then remove that as well with the leftover quantity
                int leftOvers = (quantity - inventory[i].itemQuantity);
                inventory[i].baseItem = null;
                inventory[i].itemQuantity = 0;
                quantity = leftOvers;
                RefreshSlotUI(i);
                continue;
            }

            inventory[i].itemQuantity -= quantity;

            if(inventory[i].itemQuantity <= 0)
            {
                print($"(INV) Removed {ReturnItemNameFromID(itemId)} (ID: {itemId}) completely with amount ({quantity}) from {gameObject.name}");
                inventory[i].baseItem = null;
                inventory[i].itemQuantity = 0;
            }
            else print($"(INV) Removed {ReturnItemNameFromID(itemId)} (ID: {itemId}) amount ({quantity}) from {gameObject.name}");

            RefreshSlotUI(i);
            break;
        }
    }

    /// <summary> Removes an item in the desired inventory slot from the player</summary>
    public virtual void RemoveItemFromSlot(int slot, int quantity)
    {
        if (!inventory[slot].baseItem) return;
        if (quantity > inventory[slot].itemQuantity)  // Tried to remove more than he has, perhaps a bug ?
        {
            print("Tried to remove more than he had from slot, perhaps a bug somewhere? Setting Quantity to how much he had");
            quantity = inventory[slot].itemQuantity;
        }

        inventory[slot].itemQuantity -= quantity;

        if (inventory[slot].itemQuantity <= 0)
        {
            print($"(INV) Removed Slot {slot} completely with amount ({quantity}) from {gameObject.name}");
            inventory[slot].baseItem = null;
            inventory[slot].itemQuantity = 0;
        }
        else print($"(INV) Removed Slot {slot} amount ({quantity}) from {gameObject.name}");

        RefreshSlotUI(slot);
    }
    #endregion

    #region (Inventory) Return TotalQuantity / (Database) Return Item Id  / (Database) Return Base Item
    /// <summary> Pass in the itemID, and it will return the total quantity he has of that itemID, forexample if he has 3 and a half stacks of bananas that has maxQuantity set to 100, it would return 350</summary>
    public int LookForItemReturnFreeSlot(int itemID)
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            if (inventory[i].baseItem == null) continue;
            if ((ItemID)inventory[i].baseItem.itemID == (ItemID)itemID)
            {
                if (inventory[i].itemQuantity >= inventory[i].baseItem.maxQuantity)
                    continue;
                else
                    return i;
            }
        }

        return -1;
    }

    /// <summary> Pass in the itemID, and it will return the total quantity he has of that itemID, forexample if he has 3 and a half stacks of bananas that has maxQuantity set to 100, it would return 350</summary>
    public int ReturnTotalQuantity(int itemID)
    {
        int totalQuantity = 0;
        for (int i = 0; i < inventory.Count; i++)
        {
            if ((ItemID)inventory[i].baseItem.itemID == (ItemID)itemID) totalQuantity += inventory[i].itemQuantity;
        }

        return totalQuantity;
    }

    /// <summary> Pass in ItemID & get the Max Quantity this itemId has, is typically used for containers</summary>
    public int ReturnStackQuantity(int itemID)
    {
        int maxQuantity = 0;
        for (int i = 0; i < database.items.Length; i++)
        {
            if ((ItemID)database.items[i].baseItem.itemID == (ItemID)itemID) return database.items[i].baseItem.maxQuantity;
        }

        return maxQuantity;
    }

    /// <summary> This will return the itemID based on the BaseItem that is passed through parameter, searches through database</summary>
    public int ReturnItemId(BaseItem item)
    {
        for (int i = 0; i < database.items.Length; i++)
        {
            if (database.items[i].baseItem && database.items[i].baseItem.name == item.name)
                return (int)database.items[i].baseItem.itemID;
        }

        return -1;
    }

    /// <summary> This will return the sprite based on the itemID that is passed through parameter, searches through database</summary>
    public Sprite ReturnItemSprite(int itemID)
    {
        for (int i = 0; i < database.items.Length; i++)
        {
            if ((ItemID)database.items[i].baseItem.itemID == (ItemID)itemID) return database.items[i].baseItem.itemIcon;
        }

        return null;
    }

    /// <summary> This will convert an ItemID to BaseItem, if it returns null it's not a registered item in the "item database"</summary>
    public BaseItem ReturnBaseItemFromID(int itemID)
    {
        for (int i = 0; i < database.items.Length; i++)
        {
            if ((ItemID)database.items[i].baseItem.itemID == (ItemID)itemID) return database.items[i].baseItem;
        }

        return null;
    }

    /// <summary> Returns item name based on ID, this checks from the item database, so can be called for any item, not just what the player has on him</summary>
    public string ReturnItemNameFromID(int itemID)
    {
        for (int i = 0; i < database.items.Length; i++)
        {
            if (database.items[i].baseItem && (ItemID)database.items[i].baseItem.itemID == (ItemID)itemID)
            {
                return database.items[i].baseItem.itemName;
            }
        }

        return "";
    }

    /// <summary> Clears the players inventory entirely.</summary>
    public void ClearInventory()
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            inventory[i].itemQuantity = 0;
            inventory[i].baseItem = null;
            RefreshInventoryUI();
        }
    }
    #endregion

    #region UI
    /// <summary> Refreshes the desired slot in the UI, is called automatically from Remove & Add</summary>
    protected virtual void RefreshSlotUI(int inventorySlot)
    {
        if (!IsOwner) return;

        if (inventory[inventorySlot].baseItem == null)
        {
            uiSlots[inventorySlot].transform.GetChild(0).GetChild(0).gameObject.GetComponent<TextMeshProUGUI>().text = $" ";

            Image tmpImage = uiSlots[inventorySlot].transform.GetChild(0).GetComponent<Image>();
            tmpImage.color = new Color(tmpImage.color.r, tmpImage.color.g, tmpImage.color.b, 0f);

        }
        else
        {
            if (inventory[inventorySlot].baseItem.maxQuantity != 1) // It's a non-stackable object, no need to show amount.
                uiSlots[inventorySlot].transform.GetChild(0).GetChild(0).gameObject.GetComponent<TextMeshProUGUI>().text = $"{inventory[inventorySlot].itemQuantity}";
            else uiSlots[inventorySlot].transform.GetChild(0).GetChild(0).gameObject.GetComponent<TextMeshProUGUI>().text = $" ";

            uiSlots[inventorySlot].transform.GetChild(0).gameObject.GetComponent<Image>().sprite = inventory[inventorySlot].baseItem.itemIcon;

            Image tmpImage = uiSlots[inventorySlot].transform.GetChild(0).GetComponent<Image>();
            tmpImage.color = new Color(tmpImage.color.r, tmpImage.color.g, tmpImage.color.b, 255f);
        }
    }

    /// <summary> This refreshes the whole inventory, should perhaps only be called when player is dead and lost all his items, or so.</summary>
    public virtual void RefreshInventoryUI()
    {
        if (IsServer && !IsHost) return;
        //if (!inventoryHUD.activeSelf) return; // No need to refresh, if UI is closed.

        for (int i = 0; i < currentInventorySize; i++)
        {
            RefreshSlotUI(i);
        }
    }

    /// <summary> Instantiates the slots that are needed by the inventory, is overriden in PlayerInventory & ContainerInventory</summary>
    protected virtual void SetupInventorySlots()
    {
        if (IsServer && !IsHost) return;

        for (int i = 0; i < currentInventorySize; i++)
        {
            if (uiSlots[i] == null)
            {
                uiSlots[i] = Instantiate(Resources.Load("InventorySlot") as GameObject, inventoryHUD.transform);
                RefreshSlotUI(i);
                /*uiSlots[i].transform.GetChild(0).GetComponent<DraggableItem>().currentInventory = this.GetComponent<BaseInventory>();
                uiSlots[i].transform.GetChild(0).GetComponent<DraggableItem>().inventorySlot = i;*/
            }
        }
    }

    // Add function here to create more uiSlots incase inventory size has increased during runtime.

    #endregion

    #region Event Triggers
    public virtual void SetDragObject(GameObject dragObject)
    {
        print(dragObject.name);
    }

    public virtual void OnClickedInventorySlot(int inventorySlot)
    {
        print($"OnClickedInventorySlot called in baseInventory for slot {inventorySlot} ");
    }

    public virtual void OnDroppedInventorySlot(int inventorySlot)
    {
        print($"OnDroppedInventorySlot called in baseInventory for slot {inventorySlot} ");
    }
    #endregion

    #region Swap Slots (UI DraggableItem.cs Call)
    public virtual void SwapSlot(int fromSlot, int toSlot, GameObject fromInventory = null, GameObject toInventory = null)
    {
        // Dragging from same inventory.
        if(fromInventory == toInventory)
        {
            if (inventory[toSlot].baseItem == null)
            {
                inventory[toSlot].baseItem = inventory[fromSlot].baseItem;
                inventory[toSlot].baseItem.itemID = inventory[fromSlot].baseItem.itemID;
                inventory[toSlot].itemQuantity = inventory[fromSlot].itemQuantity;
                RefreshSlotUI(toSlot);

                inventory[fromSlot].baseItem = null;
                inventory[fromSlot].itemQuantity = 0;
                RefreshSlotUI(fromSlot);

                if(!IsHost) MoveSlotServerRpc(fromSlot, toSlot);
            }
            else if (inventory[toSlot].baseItem == inventory[fromSlot].baseItem)
            {
                // Moving same kind of item ontop of each other.
                if ((inventory[toSlot].itemQuantity + inventory[fromSlot].itemQuantity) > inventory[toSlot].baseItem.maxQuantity)
                {
                    if (inventory[toSlot].itemQuantity == inventory[toSlot].baseItem.maxQuantity && inventory[fromSlot].itemQuantity == inventory[fromSlot].baseItem.maxQuantity) return;  // Max stack ontop of max stack

                    int calculatedLeftOver = (inventory[toSlot].itemQuantity + inventory[fromSlot].itemQuantity) - inventory[fromSlot].baseItem.maxQuantity;
                    inventory[toSlot].itemQuantity = inventory[fromSlot].baseItem.maxQuantity;
                    inventory[fromSlot].itemQuantity = calculatedLeftOver;

                    RefreshSlotUI(fromSlot);
                    RefreshSlotUI(toSlot);
                }
                else
                {
                    inventory[toSlot].itemQuantity = (inventory[fromSlot].itemQuantity + inventory[toSlot].itemQuantity);

                    inventory[fromSlot].baseItem = null;
                    inventory[fromSlot].itemQuantity = 0;
                    RefreshSlotUI(fromSlot);
                    RefreshSlotUI(toSlot);
                }
            }
        }
        else if(fromInventory != toInventory)
        {
            // Dragging from two different inventories, probably from a container to player inventory.
            print("BASEINVENTORY SWAP SLOT WAS CALLED WITH NOT MATCHING INVENTORIES");

        }
    }
    #endregion

    public virtual void DestroyFromServer()
    {
        // Overridden in ContainerInventory
    }
}