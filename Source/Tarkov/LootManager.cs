﻿using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace eft_dma_radar {
    public class LootManager {
        private readonly Config _config;
        /// <summary>
        /// Filtered loot ready for display by GUI.
        /// </summary>
        public ReadOnlyCollection < LootItem > Filter {
            get;
            private set;
        }
        /// <summary>
        /// All tracked loot/corpses in Local Game World.
        /// </summary>
        private ReadOnlyCollection < LootItem > Loot {
            get;
        }

        /// <summary>
        /// InHideout from Game.cs
        /// </summary>
        /// 
        private bool IsAtHideout
        {
            get => Memory.InHideout;
        }
        #region Constructor
        /// <summary>
        /// Initializes a new instance of the <see cref="LootManager"/> class.
        /// </summary>
        /// <param name="localGameWorld"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        ///
        public LootManager(ulong localGameWorld) {
            // If in hideout, don't parse loot
            if (IsAtHideout) {
                Program.Log("In Hideout, skipping loot parsing");
                return;
            }
            _config = Program.Config;
            var lootlistPtr = Memory.ReadPtr(localGameWorld + Offsets.LocalGameWorld.LootList);
            var lootListEntity = Memory.ReadPtr(lootlistPtr + Offsets.UnityList.Base);
            var countLootListObjects = Memory.ReadValue < int > (lootListEntity + Offsets.UnityList.Count);
            if (countLootListObjects < 0 || countLootListObjects > 4096) throw new ArgumentOutOfRangeException("countLootListObjects"); // Loot list sanity check
            var loot = new List < LootItem > (countLootListObjects);
            var lootCorpse = new List < Corpse > (countLootListObjects);

            var map = new ScatterReadMap();
            var round1 = map.AddRound();
            var round2 = map.AddRound();
            var round3 = map.AddRound();
            var round4 = map.AddRound();
            var round5 = map.AddRound();
            var round6 = map.AddRound();
            var round7 = map.AddRound();
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start(); // Start timing
            Program.Log("Parsing loot...");
            for (int i = 0; i < countLootListObjects; i++) {
                var lootObjectsEntity = round1.AddEntry(i, 0, lootListEntity + Offsets.UnityListBase.Start + (ulong)(0x8 * i), typeof (ulong));
                var unknownPtr = round2.AddEntry(i, 1, lootObjectsEntity, typeof (ulong), null, Offsets.LootListItem.LootUnknownPtr);
                var interactiveClass = round3.AddEntry(i, 2, unknownPtr, typeof (ulong), null, Offsets.LootUnknownPtr.LootInteractiveClass);
                var baseObject = round4.AddEntry(i, 3, interactiveClass, typeof (ulong), null, Offsets.LootInteractiveClass.LootBaseObject);
                var gameObject = round5.AddEntry(i, 4, baseObject, typeof (ulong), null, Offsets.LootBaseObject.GameObject);
                var pGameObjectName = round6.AddEntry(i, 5, gameObject, typeof (ulong), null, Offsets.GameObject.ObjectName);
                var name = round7.AddEntry(i, 6, pGameObjectName, typeof (string), 64);
            }
            map.Execute(countLootListObjects); // execute scatter read
            // Process results in parallel
            Parallel.For(0, countLootListObjects, i => {
                try {
                    if (map.Results[i][2].Result is null ||
                        map.Results[i][4].Result is null ||
                        map.Results[i][6].Result is null) return;

                    var interactiveClass = (ulong) map.Results[i][2].Result;
                    var gameObject = (ulong) map.Results[i][4].Result;
                    var name = (string) map.Results[i][6].Result;
                    var classNamePtr = Memory.ReadPtrChain(interactiveClass, Offsets.UnityClass.Name);
                    var classNameString = Memory.ReadString(classNamePtr, 64);

                    // Create cache entry
                    

                    if (name.Contains("script", StringComparison.OrdinalIgnoreCase)) {
                        //skip these. They are not loot.
                    } 
                    // Check if the object is a container
                    else if (_containers.Contains(name)) {
                        //ContainerEntity->ItemOwner->RootItem->Grids[]->ContainedItems->Grid Dictionary (unity::c_Dictionary<unity::c_item*, c_LocationInGrid*>)
                        var friendlyName = ContainerMappings.NameMap.TryGetValue(name, out var label) ? label : "Unknown Container";
                        //Get Position
                        var objectClass = Memory.ReadPtr(gameObject + Offsets.GameObject.ObjectClass);
                        var transformInternal = Memory.ReadPtrChain(objectClass, Offsets.LootGameObjectClass.To_TransformInternal);
                        var pos = new Transform(transformInternal).GetPosition();
                        //Debug.WriteLine($"Loot: {name} at {pos}");

                        //the WORST method to figure out if an item is a container...but no better solution now
                        //bool isContainer = _containers.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase));
                        //Check classname
                        //If the item is a Static Container like weapon boxes, barrels, caches, safes, airdrops etc
                        //Grid Logic for static containers so that we can see what's inside
                        try {
                            if (name.Contains("container_crete_04_COLLIDER(1)", StringComparison.OrdinalIgnoreCase)) {
                                loot.Add(new LootItem {
                                    Position = pos,
                                        Label = "!!Airdrop",
                                        Important = true,
                                        AlwaysShow = true
                                });
                                return;
                            }
                            var itemOwner = Memory.ReadPtr(interactiveClass + Offsets.LootInteractiveClass.ContainerItemOwner);
                            var itemBase = Memory.ReadPtr(itemOwner + 0xC0); //Offsets.ContainerItemOwner.LootItemBase);
                            var grids = Memory.ReadPtr(itemBase + Offsets.LootItemBase.Grids);
                            GetItemsInGrid(grids, "ignore", pos, loot, true, friendlyName);
                        } catch {}
                    }
                    // Loose Loot
                    else if (classNameString == "ObservedLootItem") {
                        var item = Memory.ReadPtr(interactiveClass + 0xB0); //EFT.InventoryLogic.Item
                        var itemTemplate = Memory.ReadPtr(item + Offsets.LootItemBase.ItemTemplate); //EFT.InventoryLogic.ItemTemplate
                        bool questItem = Memory.ReadValue < bool > (itemTemplate + Offsets.ItemTemplate.IsQuestItem);
                        if (questItem) {
                            //These are quest items
                            //Not to be confused with found in raid items
                        }
                        else {
                            var objectClass = Memory.ReadPtr(gameObject + Offsets.GameObject.ObjectClass);
                            var transformInternal = Memory.ReadPtrChain(objectClass, Offsets.LootGameObjectClass.To_TransformInternal);
                            var pos = new Transform(transformInternal).GetPosition();
                            var BSGIdPtr = Memory.ReadPtr(itemTemplate + Offsets.ItemTemplate.BsgId);
                            var id = Memory.ReadUnityString(BSGIdPtr);
                            if (id == null) return;
                            try {
                                    var grids = Memory.ReadPtr(item + Offsets.LootItemBase.Grids);
                                    var count = new MemArray(grids).Count;
                                    GetItemsInGrid(grids, id, pos, loot, false , "Loose Loot");
                                } catch {
                                    //The loot item we found does not have any grids so it's basically like a keycard or a ledx etc. Therefore add it to our loot dictionary.
                                    if (TarkovMarketManager.AllItems.TryGetValue(id, out
                                            var entry)) {
                                        loot.Add(new LootItem {
                                            Label = entry.Label,
                                                AlwaysShow = entry.AlwaysShow,
                                                Important = entry.Important,
                                                Position = pos,
                                                Item = entry.Item
                                        });
                                    }
                                }
                        
                        }

                    }
                    else if (classNameString == "ObservedCorpse") {
                        if (interactiveClass == 0x0) return;
                        var itemOwner = Memory.ReadPtr(interactiveClass + 0x40); //[40] ItemOwner : -.GClass24D0
                        var rootItem = Memory.ReadPtr(itemOwner + 0xC0); //[C0] item_0xC0 : EFT.InventoryLogic.Item
                        var slots = Memory.ReadPtr(rootItem + 0x78);
                        var slotsArray = new MemArray(slots);
                        var objectClass = Memory.ReadPtr(gameObject + Offsets.GameObject.ObjectClass);
                        var transformInternal = Memory.ReadPtrChain(objectClass, Offsets.LootGameObjectClass.To_TransformInternal);
                        var pos = new Transform(transformInternal).GetPosition();
                        foreach(var slot in slotsArray.Data) {
                            try {
                                var containedItem = Memory.ReadPtr(slot + 0x40);
                                if (containedItem == 0x0){
                                    continue;
                                }
                                var itemTemplate = Memory.ReadPtr(containedItem + Offsets.LootItemBase.ItemTemplate); //EFT.InventoryLogic.ItemTemplate
                                var BSGIdPtr = Memory.ReadPtr(itemTemplate + Offsets.ItemTemplate.BsgId);
                                var id = Memory.ReadUnityString(BSGIdPtr);
                                var corpseItemNamePtr = Memory.ReadPtr(itemTemplate + 0x58);
                                var corpseItemName = Memory.ReadUnityString(corpseItemNamePtr);
                                var grids = Memory.ReadPtr(containedItem + Offsets.LootItemBase.Grids);
                                if (grids == 0x0){
                                    //The loot item we found does not have any grids so it's weapon slot?
                                    if (TarkovMarketManager.AllItems.TryGetValue(id, out
                                            var entry)) {
                                        loot.Add(new LootItem {
                                            Label = entry.Label,
                                                AlwaysShow = entry.AlwaysShow,
                                                Important = entry.Important,
                                                Position = pos,
                                                Item = entry.Item,
                                                Container = true,
                                                ContainerName = "Corpse"
                                        });
                                    }
                                };
                                GetItemsInGrid(grids, id, pos, loot, true, "Corpse");
                            } catch {
                                continue;
                            }

                        }

                    }

                } catch {
                    // Handle exceptions
                }
            });
            stopwatch.Stop(); // Stop timing
            TimeSpan ts = stopwatch.Elapsed;
            string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
            Debug.WriteLine("RunTime " + elapsedTime);
            Loot = new(loot); // update readonly ref
            Program.Log("Loot parsing completed");
        }
        #endregion

        #region Methods
        private static readonly HashSet < string > _containers = new HashSet < string > {
            "scontainer_Blue_Barrel_Base_Cap",
            "scontainer_bag_sport_lootable",
            "container_supplyBox_opened_Cap",
            "scontainer_wood_CAP",
            "card_file_box_Anim1",
            "card_file_box_Anim2",
            "card_file_box_Anim3",
            "card_file_box_Anim4",
            "Toolbox_Door",
            "Medical_Door",
            "scontainer_ammo_grenade_Door",
            "boor_safe",
            "Ammo_crate_Cap",
            "weapon_box_cover",
            // Add more containers as needed
            "cap", // Example for unknown containers
            "cover_64",
            "lootable"
        };

        private static class ContainerMappings
        {
            public static readonly Dictionary<string, string> NameMap = new Dictionary<string, string>
            {
                {"scontainer_Blue_Barrel_Base_Cap", "Buried barrel cache"},
                {"scontainer_bag_sport_lootable", "Sports bag"},
                {"container_supplyBox_opened_Cap", "Supply crate"},
                {"scontainer_wood_CAP", "Ground cache"},
                {"card_file_box_Anim1", "Drawers 1"},
                {"card_file_box_Anim2", "Drawers 2"},
                {"card_file_box_Anim3", "Drawers 3"},
                {"card_file_box_Anim4", "Drawers 4"},
                {"Toolbox_Door", "Toolbox"},
                {"Medical_Door", "Medcase"},
                {"scontainer_ammo_grenade_Door", "Grenade box"},
                {"boor_safe", "Safe"},
                {"Ammo_crate_Cap", "Wooden ammo box"},
                {"weapon_box_cover", "Weapon box"},
                // Add more mappings as needed
                {"cap", "Unknown Container"}, // Example for unknown containers
                {"cover_64", "Unknown Container"},
                {"lootable", "Unknown Container"}
            };
        }

        /// <summary>
        /// Applies specified loot filter.
        /// </summary>
        /// <param name="filter">Filter by item 'name'. Will use values instead if left null.</param>
        public void ApplyFilter(string filter) {
            var loot = this.Loot; // cache ref
            if (loot is not null) {
                var filteredLoot = new List < LootItem > (loot.Count);
                if (filter is null ||
                    filter.Trim() == string.Empty) // Use loot values
                {
                    foreach(var item in loot) {
                        if (item.Label == null) return;
                        var value = Math.Max((byte) item.Item.avg24hPrice, item.Item.basePrice);
                        if (item.AlwaysShow || value >= _config.MinLootValue) {
                            if (!filteredLoot.Contains(item))
                                filteredLoot.Add(item);
                        }else {
                            //Console.WriteLine($"Filtered out {item.Item.name} with value {value}");
                        }
                    }
                } else // Use filtered name
                {
                    var alwaysShow = loot.Where(x => x.AlwaysShow); // Always show these
                    foreach(var item in alwaysShow) {
                        if (!filteredLoot.Contains(item))
                            filteredLoot.Add(item);
                    }
                    var names = filter.Split(','); // Get multiple items searched
                    foreach(var name in names) {
                        var search = loot.Where(x => x.Item.name.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase));
                        foreach(var item in search) {
                            if (!filteredLoot.Contains(item))
                                filteredLoot.Add(item);
                        }
                    }
                }
                this.Filter = new(filteredLoot); // update ref
            }
        }
        ///This method recursively searches grids. Grids work as follows:
        ///Take a Groundcache which holds a Blackrock which holds a pistol.
        ///The Groundcache will have 1 grid array, this method searches for whats inside that grid.
        ///Then it finds a Blackrock. This method then invokes itself recursively for the Blackrock.
        ///The Blackrock has 11 grid arrays (not to be confused with slots!! - a grid array contains slots. Look at the blackrock and you'll see it has 20 slots but 11 grids).
        ///In one of those grid arrays is a pistol. This method would recursively search through each item it finds
        ///To Do: add slot logic, so we can recursively search through the pistols slots...maybe it has a high value scope or something.
        private void GetItemsInGrid(ulong gridsArrayPtr, string id, Vector3 pos, List < LootItem > loot, bool isContainer = false, string containerName = "") {
            var gridsArray = new MemArray(gridsArrayPtr);

            if (TarkovMarketManager.AllItems.TryGetValue(id, out
                    var entry)) {
                loot.Add(new LootItem {
                    Label = entry.Label,
                        AlwaysShow = entry.AlwaysShow,
                        Important = entry.Important,
                        Position = pos,
                        Item = entry.Item,
                        Container = isContainer,
                        ContainerName = containerName,
                });
            }
            // Check all sections of the container
            foreach(var grid in gridsArray.Data) {
                var gridEnumerableClass = Memory.ReadPtr(grid + Offsets.Grids.GridsEnumerableClass); // -.GClass178A->gClass1797_0x40 // Offset: 0x0040 (Type: -.GClass1797)
                var itemListPtr = Memory.ReadPtr(gridEnumerableClass + 0x18); // -.GClass1797->list_0x18 // Offset: 0x0018 (Type: System.Collections.Generic.List<Item>)
                var itemList = new MemList(itemListPtr);

                foreach(var childItem in itemList.Data) {
                    try {
                        var childItemTemplate = Memory.ReadPtr(childItem + Offsets.LootItemBase.ItemTemplate); // EFT.InventoryLogic.Item->_template // Offset: 0x0038 (Type: EFT.InventoryLogic.ItemTemplate)
                        var childItemIdPtr = Memory.ReadPtr(childItemTemplate + Offsets.ItemTemplate.BsgId);
                        var childItemIdStr = Memory.ReadUnityString(childItemIdPtr).Replace("\\0", "");
                        //Set important and always show if quest item using ID
                        // Check to see if the child item has children
                        var childGridsArrayPtr = Memory.ReadPtr(childItem + Offsets.LootItemBase.Grids); // -.GClassXXXX->Grids // Offset: 0x0068 (Type: -.GClass1497[])
                        GetItemsInGrid(childGridsArrayPtr, childItemIdStr, pos, loot, true, containerName); // Recursively add children to the entity
                    } catch (Exception ee) {
                    }
                }
            }
        }
        #endregion
    }

    #region Classes
    //Helper class or struct
    public class MemArray {
        public ulong Address {
            get;
        }
        public int Count {
            get;
        }
        public ulong[] Data {
            get;
        }

        public MemArray(ulong address) {
            var type = typeof (ulong);

            Address = address;
            Count = Memory.ReadValue < int > (address + Offsets.UnityList.Count);
            var arrayBase = address + Offsets.UnityListBase.Start;
            var tSize = (uint) Marshal.SizeOf(type);

            // Rudimentary sanity check
            if (Count > 4096 || Count < 0)
                Count = 0;

            var retArray = new ulong[Count];
            var buf = Memory.ReadBuffer(arrayBase, Count * (int) tSize);

            for (uint i = 0; i < Count; i++) {
                var index = i * tSize;
                var t = MemoryMarshal.Read < ulong > (buf.Slice((int) index, (int) tSize));
                if (t == 0x0) throw new NullPtrException();
                retArray[i] = t;
            }

            Data = retArray;
        }
    }

    //Helper class or struct
    public class MemList {
        public ulong Address {
            get;
        }

        public int Count {
            get;
        }

        public List < ulong > Data {
            get;
        }

        public MemList(ulong address) {
            var type = typeof (ulong);

            Address = address;
            Count = Memory.ReadValue < int > (address + Offsets.UnityList.Count);

            if (Count > 4096 || Count < 0)
                Count = 0;

            var arrayBase = Memory.ReadPtr(address + Offsets.UnityList.Base) + Offsets.UnityListBase.Start;
            var tSize = (uint) Marshal.SizeOf(type);
            var retList = new List < ulong > (Count);
            var buf = Memory.ReadBuffer(arrayBase, Count * (int) tSize);

            for (uint i = 0; i < Count; i++) {
                var index = i * tSize;
                var t = MemoryMarshal.Read < ulong > (buf.Slice((int) index, (int) tSize));
                if (t == 0x0) throw new NullPtrException();
                retList.Add(t);
            }

            Data = retList;
        }
    }
    public class LootItem {
        public string Label {
            get;
            init;
        }
        public bool Important {
            get;
            init;
        } = false;
        public Vector3 Position {
            get;
            init;
        }
        //public TarkovMarketItem Item { get; init; } = new();
        public TrakovDevMarketItem Item {
            get;
            init;
        } = new();
        public bool AlwaysShow {
            get;
            init;
        } = false;
        public string BsgId {
            get;
            init;
        }
        public bool Container {
            get;
            init;
        } = false;
        public string ContainerName {
            get;
            init;
        }

    }

    public class Corpse {
        public string Label {
            get;
            init;
        }
        public bool Important {
            get;
            init;
        } = false;
        public Vector3 Position {
            get;
            init;
        }
        public bool AlwaysShow {
            get;
            init;
        } = false;
        public string BsgId {
            get;
            init;
        }
    }
    #endregion
}