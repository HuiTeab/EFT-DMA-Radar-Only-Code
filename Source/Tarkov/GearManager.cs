using System;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace eft_dma_radar
{
    public class GearManager
    {
        private static readonly List<string> _skipSlots = new()
        {
            "Scabbard", "SecuredContainer", "Dogtag", "Compass", "Eyewear", "ArmBand"
        };
        /// <summary>
        /// List of equipped items in PMC Inventory Slots.
        /// </summary>
        public ReadOnlyDictionary<string, GearItem> Gear { get; }

        public GearManager(ulong playerBase, bool isPMC)
        {

            Debug.WriteLine("GearManager: Initializing...");

            //foreach (var item in TarkovMarketManager.AllItems)
            //{
            //    //Debug.WriteLine($"GearManager: TarkovMarketManager.AllItems: {item}");
            //}

            var inventorycontroller = Memory.ReadPtr(playerBase + Offsets.Player.InventoryController);
            //Debug.WriteLine($"GearManager: InventoryController: 0x{inventorycontroller:X}");
            var inventory = Memory.ReadPtr(inventorycontroller + Offsets.InventoryController.Inventory);
            //Debug.WriteLine($"GearManager: Inventory: 0x{inventory:X}");
            var equipment = Memory.ReadPtr(inventory + Offsets.Inventory.Equipment);
            //Debug.WriteLine($"GearManager: Equipment: 0x{equipment:X}");
            var slots = Memory.ReadPtr(equipment + Offsets.Equipment.Slots);
            //Debug.WriteLine($"GearManager: Slots: 0x{slots:X}");
            var size = Memory.ReadValue<int>(slots + Offsets.UnityList.Count);
            //Debug.WriteLine($"GearManager: Slots Size: {size}");
            var slotDict = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

            for (int slotID = 0; slotID < size; slotID++)
            {
                var slotPtr = Memory.ReadPtr(slots + Offsets.UnityListBase.Start + (uint)slotID * 0x8);
                var namePtr = Memory.ReadPtr(slotPtr + Offsets.Slot.Name);
                var name = Memory.ReadUnityString(namePtr);
                if (_skipSlots.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                slotDict.TryAdd(name, slotPtr);
            }
            var gearDict = new Dictionary<string, GearItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var slotName in slotDict.Keys)
            {
                //Debug.WriteLine($"GearManager: Slot: {slotName}");
                try
                {
                    if (slotDict.TryGetValue(slotName, out var slot))
                    {
                        var containedItem = Memory.ReadPtr(slot + Offsets.Slot.ContainedItem);
                        Debug.WriteLine($"GearManager: Slot: {slotName} 0x{slot:X} ContainedItem: 0x{containedItem:X}");
                        var inventorytemplate = Memory.ReadPtr(containedItem + Offsets.LootItemBase.ItemTemplate);
                        Debug.WriteLine($"GearManager: InventoryTemplate: 0x{inventorytemplate:X}");
                        var idPtr = Memory.ReadPtr(inventorytemplate + Offsets.ItemTemplate.BsgId);
                        Debug.WriteLine($"GearManager: ID: 0x{idPtr:X}");
                        var id = Memory.ReadUnityString(idPtr);
                        Debug.WriteLine($"GearManager: ID: {id}");
                        //Print all tarkovmanager items

                        if (TarkovMarketManager.AllItems.TryGetValue(id, out var entry))
                        {
                            string longName = entry.Item.name; // Contains 'full' item name
                            Debug.WriteLine($"GearManager: LongName: {longName}");
                            string shortName = entry.Item.shortName; // Contains 'full' item name
                            Debug.WriteLine($"GearManager: ShortName: {shortName}");
                            string extraSlotInfo = null; // Contains additional slot information (ammo type,etc.)
                            if (isPMC) // Only recurse further for PMCs (we don't care about P Scavs)
                            {
                                if (slotName == "FirstPrimaryWeapon" || slotName == "SecondPrimaryWeapon") // Only interested in weapons
                                {
                                    try
                                    {
                                        var result = new PlayerWeaponInfo();
                                        //RecurseSlotsForThermalsAmmo(containedItem, ref result); // Check weapon ammo type, and if it contains a thermal scope
                                        extraSlotInfo = result.ToString();
                                    }
                                    catch { }
                                }
                            }
                            if (extraSlotInfo is not null)
                            {
                                longName += $" ({extraSlotInfo})";
                                shortName += $" ({extraSlotInfo})";
                            }
                            var gear = new GearItem()
                            {
                                Long = longName,
                                Short = shortName
                            };
                            gearDict.TryAdd(slotName, gear);
                        } else {
                            Debug.WriteLine($"GearManager: ID: {id} not found in TarkovMarketManager.AllItems");
                        }
                    }
                }
                catch { } // Skip over empty slots
            }
            Gear = new(gearDict); // update readonly ref
            //foreach (var item in Gear)
            //{
                //Debug.WriteLine($"GearManager: Gear: {item}");
            //}
        }

        /// <summary>
        /// Checks a 'Primary' weapon for Ammo Type, and Thermal Scope.
        /// </summary>
        private void RecurseSlotsForThermalsAmmo(ulong lootItemBase, ref PlayerWeaponInfo result)
        {
            const string reapIR = "5a1eaa87fcdbcb001865f75e";
            const string flir = "5d1b5e94d7ad1a2b865a96b0";
            Debug.WriteLine($"GearManager Scope: Starting...");
            try
            {
                var parentSlots = Memory.ReadPtr(lootItemBase + Offsets.LootItemBase.Slots);
                var size = Memory.ReadValue<int>(parentSlots + Offsets.UnityList.Count);
                var slotDict = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

                for (int slotID = 0; slotID < size; slotID++)
                {
                    var slotPtr = Memory.ReadPtr(parentSlots + Offsets.UnityListBase.Start + (uint)slotID * 0x8);
                    var namePtr = Memory.ReadPtr(slotPtr + Offsets.Slot.Name);
                    var name = Memory.ReadUnityString(namePtr);
                    if (_skipSlots.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    slotDict.TryAdd(name, slotPtr);
                }
                foreach (var slotName in slotDict.Keys)
                {
                    try
                    {
                        if (slotDict.TryGetValue(slotName, out var slot))
                        {
                            var containedItem = Memory.ReadPtr(slot + Offsets.Slot.ContainedItem);
                            if (slotName == "mod_magazine") // Magazine slot - Check for ammo!
                            {
                                var cartridge = Memory.ReadPtr(containedItem + Offsets.LootItemBase.Cartridges);
                                var cartridgeStack = Memory.ReadPtr(cartridge + Offsets.StackSlot.Items);
                                var cartridgeStackList = Memory.ReadPtr(cartridgeStack + Offsets.UnityList.Base);
                                var firstRoundItem = Memory.ReadPtr(cartridgeStackList + Offsets.UnityListBase.Start + 0); // Get first round in magazine
                                var firstRoundItemTemplate = Memory.ReadPtr(firstRoundItem + Offsets.LootItemBase.ItemTemplate);
                                var firstRoundIdPtr = Memory.ReadPtr(firstRoundItemTemplate + Offsets.ItemTemplate.BsgId);
                                var firstRoundId = Memory.ReadUnityString(firstRoundIdPtr);
                                if (TarkovMarketManager.AllItems.TryGetValue(firstRoundId, out var firstRound)) // Lookup ammo type
                                {
                                    result.AmmoType = firstRound.Item.shortName;
                                }
                            }
                            else // Not a magazine, keep recursing for a scope
                            {
                                var inventorytemplate = Memory.ReadPtr(containedItem + Offsets.LootItemBase.ItemTemplate);
                                Debug.WriteLine($"GearManager Scope: InventoryTemplate: 0x{inventorytemplate:X}");
                                var idPtr = Memory.ReadPtr(inventorytemplate + Offsets.ItemTemplate.BsgId);
                                Debug.WriteLine($"GearManager Scope: ID: 0x{idPtr:X}");
                                var id = Memory.ReadUnityString(idPtr);
                                Debug.WriteLine($"GearManager Scope: ID: {id}");
                                if (id.Equals(reapIR, StringComparison.OrdinalIgnoreCase) ||
                                    id.Equals(flir, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (TarkovMarketManager.AllItems.TryGetValue(id, out var entry))
                                    {
                                        result.ThermalScope = entry.Item.shortName;
                                    }
                                }
                                RecurseSlotsForThermalsAmmo(containedItem, ref result);
                            }
                        }
                    }
                    catch { } // Skip over empty slots
                }
            }
            catch
            {
            }
        }
    }
}
