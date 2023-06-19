using UnityEngine;

/// <summary> LootTable slot</summary>
[System.Serializable]
public class LootTableSlot
{
    public string name = "NoName";
    public BaseItem item = null;
    public int itemQuantityMaxAmount = 1;       // A random amount ranging from 1-itemQuantityMaxAmount is dropped if its higher than 1.
    public int dropChanceStart = 0;
    public int dropChanceEnd = 0;
}

public class LootTable : MonoBehaviour
{
    [Header("Loot Table for this object")]
    public LootTableSlot[] lootTable = default;
    public int lootToDrop = 0;

    private void Start()
    {
        if(lootToDrop == 0)
            lootToDrop = Random.Range(0, 3);
    }

    public int GetDropChanceLootFromTable()
    {
        if (lootTable == null) Debug.Log($"LootTable(ERROR): {gameObject.name} wanted a random loot from LootTable, but no loot has been added, add some loot or remove script"); // No loot added to droptable.
        int luckOfTheDraw = Random.Range(0, 100);
        for (int i = 0; i < lootTable.Length; i++)
        {
            if (lootTable[i].item == null) continue;
            if (luckOfTheDraw >= lootTable[i].dropChanceStart && luckOfTheDraw <= lootTable[i].dropChanceEnd)
            {
                return i;
            }
        }

        return -1;
    }

    public BaseItem GetRandomLootFromTable()
    {
        BaseItem randomLoot = lootTable[Random.Range(0, lootTable.Length)].item;
        return randomLoot;
    }
}