﻿using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace eft_dma_radar
{

    /// <summary>
    /// Class containing Game (Raid) instance.
    /// </summary>
    public class Game
    {
        private readonly ulong _unityBase;
        private GameObjectManager _gom;
        private ulong _localGameWorld;
        private LootManager _lootManager;
        private RegisteredPlayers _rgtPlayers;
        private bool _inHideout = false;
        private GrenadeManager _grenadeManager;
        private ExfilManager _exfilManager;
        private volatile bool _inGame = false;
        private volatile bool _loadingLoot = false;
        private volatile bool _refreshLoot = false;
        private volatile string _mapName = string.Empty;
        private volatile bool _isScav = false;
        
        #region Getters
        public bool InGame
        {
            get => _inGame;
        }
        // in InHideout means local game world not false and registered players is 1
        public bool InHideout
        {
            get => _inHideout;
        }
        public bool IsScav
        {
            get => _isScav;
        }
        public string MapName
        {
            get => _mapName;
        }
        public int PlayerSide
        {
            get => 0;
        }
        public bool LoadingLoot
        {
            get => _loadingLoot;
        }
        public ReadOnlyDictionary<string, Player> Players
        {
            get => _rgtPlayers?.Players;
        }
        public LootManager Loot
        {
            get => _lootManager;
        }
        public ReadOnlyCollection<Grenade> Grenades
        {
            get => _grenadeManager?.Grenades;
        }
        public ReadOnlyCollection<Exfil> Exfils
        {
            get => _exfilManager?.Exfils;
        }
        #endregion

        /// <summary>
        /// Game Constructor.
        /// </summary>
        public Game(ulong unityBase)
        {
            _unityBase = unityBase;
        }

        #region GameLoop
        /// <summary>
        /// Main Game Loop executed by Memory Worker Thread.
        /// It manages the updating of player list and game environment elements like loot, grenades, and exfils.
        /// </summary>
        public async void GameLoop()
        {
            try
            {
                // Update the list of players and their states
                await UpdatePlayersAsync();

                // Update game environment elements such as loot and exfils
                if (_inGame && !InHideout)
                {
                    UpdateGameEnvironment();
                }
                //UpdateGameEnvironment();
            }
            catch (DMAShutdown)
            {
                // Handle DMA shutdown scenarios
                HandleDMAShutdown();
            }
            catch (RaidEnded e)
            {
                // Handle scenarios where the raid has ended
                HandleRaidEnded(e);
            }
            catch (Exception ex)
            {
                // Handle any unexpected exceptions that occur during the game loop
                HandleUnexpectedException(ex);
            }
        }
        #endregion

        #region Methods
        /// <summary>
        /// Updates the list of players and their current states.
        /// </summary>
        private async Task UpdatePlayersAsync()
        {
            // Update the list of registered players
            //_rgtPlayers.UpdateList();

            await _rgtPlayers.UpdateListAsync();
            // Update the state of each player (e.g., location, health)
            _rgtPlayers.UpdateAllPlayers();
        }
        /// <summary>
        /// Method to get map name using local game world
        /// </summary>
        /// <returns></returns>
        private void GetMapName()
        {
            //If in hideout, map name is empty
            if (_inHideout)
            {
                _mapName = string.Empty;
                return;
            }
            var mapNamePrt = Memory.ReadPtr(_localGameWorld + 0x40);
            var mapName = Memory.ReadUnityString(mapNamePrt);
            _mapName = mapName;
        }

        /// <summary>
        /// Updates miscellaneous game environment elements like loot, grenades, and exfils.
        /// </summary>
        private void UpdateGameEnvironment()
        {
            // This method encapsulates the logic for updating various elements in the game
            // such as loot positions, grenade statuses, and exfil points
            UpdateMisc();
        }

        /// <summary>
        /// Handles the scenario when DMA shutdown occurs.
        /// </summary>
        private void HandleDMAShutdown()
        {
            _inGame = false;
            // Additional logic to handle DMA shutdown
        }

        /// <summary>
        /// Handles the scenario when the raid ends.
        /// </summary>
        /// <param name="e">The RaidEnded exception instance containing details about the raid end.</param>
        private void HandleRaidEnded(RaidEnded e)
        {
            Program.Log("Raid has ended!");
            _inGame = false;
            // Additional logic to handle the end of a raid
        }

        /// <summary>
        /// Handles unexpected exceptions that occur during the game loop.
        /// </summary>
        /// <param name="ex">The exception instance that was thrown.</param>
        private void HandleUnexpectedException(Exception ex)
        {
            Program.Log($"CRITICAL ERROR - Raid ended due to unhandled exception: {ex}");
            _inGame = false;
            // Additional logic to handle unexpected exceptions
        }

        /// <summary>
        /// Waits until Raid has started before returning to caller.
        /// </summary>
        /// 
        public async Task WaitForGameAsync()
        {
            while (!(GetGOM() && await GetLGWAsync()))
            {
                _inGame = false;
                await Task.Delay(500);
            }
            Program.Log("Raid has started!");
            _inGame = true;
        }


        /// <summary>
        /// Helper method to locate Game World object.
        /// </summary>
        private ulong GetObjectFromList(ulong activeObjectsPtr, ulong lastObjectPtr, string objectName)
        {
            var activeObject = Memory.ReadValue<BaseObject>(Memory.ReadPtr(activeObjectsPtr));
            var lastObject = Memory.ReadValue<BaseObject>(Memory.ReadPtr(lastObjectPtr));
            if (activeObject.obj != 0x0 && lastObject.obj == 0x0)
            {
                // Add wait for lastObject to be populated
                Program.Log("Waiting for lastObject to be populated...");
                while (lastObject.obj == 0x0)
                {
                    lastObject = Memory.ReadValue<BaseObject>(Memory.ReadPtr(lastObjectPtr));
                    Thread.Sleep(1000);
                }
            }

            if (activeObject.obj != 0x0)
            {
                while (activeObject.obj != 0x0 && activeObject.obj != lastObject.obj)
                {
                    var objectNamePtr = Memory.ReadPtr(activeObject.obj + Offsets.GameObject.ObjectName);
                    var objectNameStr = Memory.ReadString(objectNamePtr, 64);
                    if (objectNameStr.Contains(objectName, StringComparison.OrdinalIgnoreCase))
                    {
                        Program.Log($"Found object {objectNameStr}");
                        return activeObject.obj;
                    }

                    activeObject = Memory.ReadValue<BaseObject>(activeObject.nextObjectLink); // Read next object
                }
            }
            Program.Log($"Couldn't find object {objectName}");
            return 0;
        }

        /// <summary>
        /// Gets Game Object Manager structure.
        /// </summary>
        private bool GetGOM()
        {
            try
            {
                var addr = Memory.ReadPtr(_unityBase + Offsets.ModuleBase.GameObjectManager);
                _gom = Memory.ReadValue<GameObjectManager>(addr);
                Program.Log($"Found Game Object Manager at 0x{addr.ToString("X")}");
                return true;
            }
            catch (DMAShutdown) { throw; }
            catch (Exception ex)
            {
                throw new GameNotRunningException($"ERROR getting Game Object Manager, game may not be running: {ex}");
            }
        }

        /// <summary>
        /// Gets Local Game World address.
        /// </summary>
        private async Task<bool> GetLGWAsync()
        {
            int retryInterval = 500; // Time in milliseconds to wait before retrying
            int maxRetries = 50; // Maximum number of retries
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                try
                {
                    ulong activeNodes = Memory.ReadPtr(_gom.ActiveNodes);
                    ulong lastActiveNode = Memory.ReadPtr(_gom.LastActiveNode);
                    var gameWorld = GetObjectFromList(activeNodes, lastActiveNode, "GameWorld");

                    if (gameWorld == 0)
                    {
                        Program.Log("Unable to find GameWorld Object, likely not in raid. Retrying...");
                        await Task.Delay(retryInterval);
                        retryCount++;
                        continue;
                    }

                    _localGameWorld = Memory.ReadPtrChain(gameWorld, Offsets.GameWorld.To_LocalGameWorld);
                    if (_localGameWorld == 0)
                    {
                        Program.Log("ERROR - Local Game World is null. Retrying...");
                        await Task.Delay(retryInterval);
                        retryCount++;
                        continue;
                    }
                    var localPlayer = Memory.ReadPtr(_localGameWorld + 0x148);
                    var localPlayerInfo = Memory.ReadPtrChain(localPlayer, new uint[] {0x588, 0x28});
                    var localPlayeSide = Memory.ReadValue<int>(localPlayerInfo + 0x70);
                    if (localPlayeSide == 4)
                    {
                        _isScav = true;
                    }
                    else
                    {
                        _isScav = false;
                    }
                    var localPlayerClassnamePtr = Memory.ReadPtrChain(localPlayer, Offsets.UnityClass.Name);
                    var classNameString = Memory.ReadString(localPlayerClassnamePtr, 64).Replace("\0", string.Empty);
                    //Hideout handling
                    if (classNameString == "HideoutPlayer")
                    {
                        var rgtPlayers = localPlayer;
                        _rgtPlayers = new RegisteredPlayers(rgtPlayers);
                        _inHideout = true;
                        //Should skip loot, grenades, exfils, etc. But should still keep gearmanager
                        return true;
                    }
                    //Online and offline handling
                    else if (classNameString == "ClientPlayer" || classNameString == "LocalPlayer")
                    {
                        _inHideout = false;
                        // Check side
                        //var side = Memory.ReadValue<int>(localPlayer + Offsets.LocalPlayer.Side);
                        var rgtPlayers = new RegisteredPlayers(Memory.ReadPtr(_localGameWorld + Offsets.LocalGameWorld.RegisteredPlayers));
                        // retry if player count is 0
                        if (rgtPlayers.PlayerCount == 0)
                        {
                            Program.Log("ERROR - Registered Players is null. Retrying...");
                            await Task.Delay(retryInterval);
                            retryCount++;
                            continue;
                        }

                        _rgtPlayers = rgtPlayers;
                        return true; // Successful exit
                    }
                    else {
                        await Task.Delay(retryInterval);
                        retryCount++;
                        continue;
                    }
                }
                catch (DMAShutdown)
                {
                    throw; // Propagate the DMAShutdown exception upwards
                }
                catch (Exception ex)
                {
                    Program.Log($"ERROR getting Local Game World: {ex}. Retrying...");
                    await Task.Delay(retryInterval);
                    retryCount++;
                    continue;
                }
            }

            Program.Log("Maximum retry attempts reached. Unable to retrieve Local Game World.");
            return false; // Indicate failure after maximum retries
        }



        /// <summary>
        /// Loot, grenades, exfils,etc.
        /// </summary>
        private void UpdateMisc()
        {
            if (_lootManager is null || _refreshLoot)
            {
                _loadingLoot = true;
                try
                {
                    var loot = new LootManager(_localGameWorld);
                    _lootManager = loot; // update ref
                    _refreshLoot = false;
                }
                catch (Exception ex)
                {
                    Program.Log($"ERROR loading LootEngine: {ex}");
                }
                _loadingLoot = false;
            }
            if (_mapName == string.Empty)
            {
                try
                {
                    GetMapName();
                }
                catch (Exception ex)
                {
                    Program.Log($"ERROR getting map name: {ex}");
                }
            }
            if (_grenadeManager is null)
            {
                try
                {
                    var grenadeManager = new GrenadeManager(_localGameWorld);
                    _grenadeManager = grenadeManager; // update ref
                }
                catch (Exception ex)
                {
                    Program.Log($"ERROR loading GrenadeManager: {ex}");
                }
            }
            else _grenadeManager.Refresh(); // refresh via internal stopwatch
            if (_exfilManager is null)
            {
                try
                {
                    var exfils = new ExfilManager(_localGameWorld);
                    _exfilManager = exfils; // update ref
                }
                catch (Exception ex)
                {
                    Program.Log($"ERROR loading ExfilController: {ex}");
                }
            }
            else _exfilManager.Refresh(); // periodically refreshes (internal stopwatch)
        }
        public void RefreshLoot()
        {
            if (_inGame)
            {
                _refreshLoot = true;
            }
        }
        #endregion
    }

    #region Exceptions
    public class GameNotRunningException : Exception
    {
        public GameNotRunningException()
        {
        }

        public GameNotRunningException(string message)
            : base(message)
        {
        }

        public GameNotRunningException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    public class RaidEnded : Exception
    {
        public RaidEnded()
        {
        }

        public RaidEnded(string message)
            : base(message)
        {
        }

        public RaidEnded(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
    #endregion
}
