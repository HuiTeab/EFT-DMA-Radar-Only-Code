using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.Intrinsics;

namespace eft_dma_radar
{
    public class RegisteredPlayers
    {
        private readonly ulong _base;
        private readonly ulong _listBase;
        private readonly Stopwatch _regSw = new();
        private readonly Stopwatch _healthSw = new();
        private readonly Stopwatch _posSw = new();
        private readonly ConcurrentDictionary<string, Player> _players =
            new(StringComparer.OrdinalIgnoreCase);

        private int _localPlayerGroup = -100;

        private bool IsAtHideout
        {
            get => Memory.InHideout;
        }

        #region Getters
        public ReadOnlyDictionary<string, Player> Players { get; }
        public int PlayerCount
        {
            get
            {
                int maxAttempts = 5;
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    try
                    {
                        var count = Memory.ReadValue<int>(_base + Offsets.UnityList.Count);
                        if (count < 1 || count > 1024)
                        {
                            throw new ArgumentOutOfRangeException(nameof(count), 
                                $"Count value out of range: {count}");
                        }
                        return count;
                    }
                    catch (DMAShutdown)
                    {
                        throw; // Specific handling for DMAShutdown
                    }
                    catch (Exception ex) when (attempt < maxAttempts - 1)
                    {
                        Thread.Sleep(1000); // Consider using Task.Delay in an async method
                    }
                    catch
                    {
                        // Log exception or handle it as needed
                        return -1; // Indicate an error condition
                    }
                }

                return -1; // Default error return
            }
        }

        public async Task<int> GetPlayerCountAsync()
        {
            int maxAttempts = 5;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    var count = Memory.ReadValue<int>(_base + Offsets.UnityList.Count);
                    if (count <= 1 || count > 1024)
                    {
                        throw new ArgumentOutOfRangeException(nameof(count), 
                            $"Count value out of range: {count}");
                    }
                    return count;
                }
                catch (DMAShutdown)
                {
                    throw; // Specific handling for DMAShutdown
                }
                catch (Exception ex) when (attempt < maxAttempts - 1)
                {
                    await Task.Delay(1000); // Asynchronous delay
                }
                catch
                {
                    // Log exception or handle it as needed
                    return -1; // Indicate an error condition
                }
            }
            return -1; // Default error return
        }



        #endregion

        /// <summary>
        /// RegisteredPlayers List Constructor.
        /// </summary>
        public RegisteredPlayers(ulong baseAddr)
        {
            _base = baseAddr;
            Players = new(_players); // update readonly ref
            _listBase = Memory.ReadPtr(_base + 0x0010);
            _regSw.Start();
            _healthSw.Start();
            _posSw.Start();
        }

        // Example of a helper method
        private (string, string) GetPlayerIdFromBase(ulong playerBase)
        {
            // Logic to extract player ID from player base
            // Return the player ID
            //Debug.WriteLine($"PlayerBase: {playerBase:X}");
            var classNamePtr = Memory.ReadPtrChain(playerBase, Offsets.UnityClass.Name);
            var classNameString = Memory.ReadString(classNamePtr, 64).Replace("\0", string.Empty);
            //classNameString = classNameString.Replace("\0", string.Empty);
            //Debug.WriteLine($"ClassName: {classNameString}");

            string id;
            string className = classNameString;
            //ulong localPlayerProfile = 0;
            // If classname = ClientPlayer use [Class] EFT.Player
            // If classname = NextObservedPlayer use [Class] EFT.NextObservedPlayer.ObservedPlayerView

            if (classNameString == "ClientPlayer" || classNameString == "LocalPlayer") // [Class] EFT.Player : MonoBehaviour, IPlayer, GInterface58CF, GInterface58CA, GInterface58D4, GInterface590B, GInterfaceB734, IDissonancePlayer
            {
                //Local player
                var localPlayerProfile = Memory.ReadPtr(playerBase + Offsets.Player.Profile);
                var localPlayerID = Memory.ReadPtr(localPlayerProfile + Offsets.Profile.Id);

                var localPlayerIDStr = Memory.ReadUnityString(localPlayerID);
                id = localPlayerIDStr;
                
            }
            else if (classNameString == "HideoutPlayer"){
                //Local player
                var localPlayerProfile = Memory.ReadPtr(playerBase + Offsets.Player.Profile);
                var localPlayerID = Memory.ReadPtr(localPlayerProfile + Offsets.Profile.Id);

                var localPlayerIDStr = Memory.ReadUnityString(localPlayerID);
                id = localPlayerIDStr;
            }
            else if (classNameString == "ObservedPlayerView") //[Class] EFT.NextObservedPlayer.ObservedPlayerView : MonoBehaviour, IPlayer
            {
                //All other players
                var ObservedPlayerView = playerBase;
                var profileIDprt = Memory.ReadPtr(ObservedPlayerView + Offsets.ObservedPlayerView.ID);
                var profileID = Memory.ReadUnityString(profileIDprt);
                id = profileID;
            }
            else
            {
                id = null;
            }

            return (id, className);
        }



        private void ProcessPlayer(int index, ScatterReadMap scatterMap, HashSet<string> registered)
        {
            //Console.WriteLine($"UpdateList: Processing player {index + 1}.");

            try
            {
                var playerBase = (ulong)scatterMap.Results[index][0].Result;
                var playerProfile = 0ul;
                (string playerId, string className) = GetPlayerIdFromBase(playerBase);
                if (playerId.Length != 24 && playerId.Length != 36) throw new ArgumentOutOfRangeException("id"); // Ensure valid ID length
                                if (_players.TryGetValue(playerId, out var player))
                {
                    if (className == "ClientPlayer" || className == "LocalPlayer" || className == "HideoutPlayer")
                    {
                        playerProfile = Memory.ReadPtr(playerBase + Offsets.Player.Profile);

                    }
                    else if (className == "ObservedPlayerView")
                    {
                        //Console.WriteLine($"Player '{player.Name}' is observed player.");
                        playerProfile = Memory.ReadPtr(playerBase);
                        //var healthController = Memory.ReadPtr(playerProfile + 0x80 +0xE8);
                    }
                    else
                    {
                        //Console.WriteLine($"Player '{player.Name}' is unknown player.");
                        //set playerProfile to 0
                        playerProfile = 0;
                        return;
                    }
                    
                    if (player.ErrorCount > 100) // Erroring out a lot? Re-Alloc
                    {
                        Program.Log(
                            $"WARNING - Existing player '{player.Name}' being re-allocated due to excessive errors..."
                        );
                        ReallocPlayer(playerId, playerBase, playerProfile);
                    }
                    else if (player.Base != playerBase) // Base address changed? Re-Alloc
                    {
                        Program.Log(
                            $"WARNING - Existing player '{player.Name}' being re-allocated due to new base address..."
                        );
                        ReallocPlayer(playerId, playerBase, playerProfile);
                    }
                    else // Mark active & alive
                    {
                        player.IsActive = true;
                        player.IsAlive = true;
                    }
                }
                else
                {
                    if (className == "ClientPlayer" || className == "LocalPlayer" || className == "HideoutPlayer") {
                        playerProfile = Memory.ReadPtr(playerBase + Offsets.Player.Profile);
                    }
                    else if (className == "ObservedPlayerView")
                    {
                        playerProfile = Memory.ReadPtr(playerBase);
                    }
                    else
                    {
                        playerProfile = 0;
                        return;
                    }
                    var newplayer = new Player(playerBase, playerProfile, null, className); // allocate new player object
                    if (
                        newplayer.Type is PlayerType.LocalPlayer
                        && _players.Any(x => x.Value.Type is PlayerType.LocalPlayer)
                    )
                    {
                        // Don't allocate more than one LocalPlayer on accident
                    }
                    else
                    {
                        if (_players.TryAdd(playerId, newplayer))
                            Program.Log($"Player '{newplayer.Name}' allocated.");
                    }
                }

                registered.Add(playerId); // Mark player as registered

            }
            catch (Exception ex)
            {
                Program.Log($"ERROR processing player at index {index}: {ex}");
            }

            void ReallocPlayer(string id, ulong newPlayerBase, ulong newPlayerProfile)
            {
                try
                {
                    var player = new Player(newPlayerBase, newPlayerProfile, _players[id].Position); // alloc
                    _players[id] = player; // update ref to new object
                    Program.Log($"Player '{player.Name}' Re-Allocated successfully.");
                }
                catch (Exception ex)
                {
                    throw new Exception($"ERROR re-allocating player '{_players[id].Name}': ", ex);
                }
            }
        }

        private bool ShouldSkipUpdate()
        {
            if (_regSw.ElapsedMilliseconds < 750)
            {
                //Debug.WriteLine("UpdateList: Update skipped, less than 750ms elapsed.");
                return true;
            }
            return false;
        }

        private void MarkInactivePlayers(HashSet<string> registered)
        {
            foreach (var player in _players)
            {
                if (!registered.Contains(player.Key))
                {
                    player.Value.IsActive = false;
                }
            }
        }

        private ScatterReadMap InitializeScatterRead(int count)
        {
            var scatterMap = new ScatterReadMap();
            var round1 = scatterMap.AddRound();

            //Debug.WriteLine("UpdateList: Initializing scatter read rounds.");
            for (int i = 0; i < count; i++)
            {
                ulong playerBaseOffset = _listBase + Offsets.UnityListBase.Start + (uint)(i * 0x8);
                //Debug.WriteLine($"Player {i + 1}/{count} - PlayerBase Offset: {playerBaseOffset:X}");
                round1.AddEntry(i, 0, playerBaseOffset, typeof(ulong));
            }

            //Debug.WriteLine("UpdateList: Scatter read rounds added, executing scatter read.");
            scatterMap.Execute(count); // Execute scatter read
            //Debug.WriteLine("UpdateList: Scatter read executed.");

            return scatterMap;
        }

            // Method to check if the raid has ended
        private bool IsRaidEnded(ulong playerBase)
        {
            // Implement logic to determine if the raid has ended
            // This could be based on a state, an event, or a condition
            // If localplayer corpsePtr is found, raid has ended
            // If localplayer corpsePtr is null, raid has not ended
            var corpsePtr =  Memory.ReadPtr(playerBase + Offsets.Player.Corpse);
            if (corpsePtr != 0x0)
            {
                return true;
            }
            return false; // Placeholder return value
        }

        #region UpdateList
        /// <summary>
        /// Updates the ConcurrentDictionary of 'Players'
        /// </summary>
        /// 
        public async Task UpdateListAsync()
        {
            if (ShouldSkipUpdate())
                return;

            try
            {
                int count = await GetPlayerCountAsync();
                var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var scatterMap = InitializeScatterRead(count);

                for (int i = 0; i < count; i++)
                {
                    ProcessPlayer(i, scatterMap, registered);
                }

                MarkInactivePlayers(registered);
                _regSw.Restart();
            }
            catch (DMAShutdown)
            {
                throw;
            }
            catch (RaidEnded)
            {
                // Clear variables
                _localPlayerGroup = -100;
                _players.Clear();
                
                throw;
            }
            catch (Exception ex)
            {
                Program.Log($"CRITICAL ERROR - RegisteredPlayers Loop FAILED: {ex}");
            }
        }
        #endregion

        #region UpdateAllPlayers
        /// <summary>
        /// Updates all 'Player' values (Position,health,direction,etc.)
        /// </summary>
        ///
        public void UpdateAllPlayers()
        {
            //atm skip if at hideout
            if (IsAtHideout)
            {
                Debug.WriteLine("In Hideout, not updating players.");
                return;
            }
            try
            {
                var players = _players
                    .Select(x => x.Value)
                    .Where(x => x.IsActive && x.IsAlive)
                    .ToArray();
                if (players.Length == 0)
                    return; // No players
                if (_localPlayerGroup == -100) // Check if current player group is set
                {
                    var localPlayer = _players
                        .FirstOrDefault(x => x.Value.Type is PlayerType.LocalPlayer)
                        .Value;
                    if (localPlayer is not null)
                    {
                        _localPlayerGroup = localPlayer.GroupID;
                    }
                }
                //bool checkHealth = _healthSw.ElapsedMilliseconds > 250; // every 250 ms
                bool checkPos =
                    _posSw.ElapsedMilliseconds > 10000 && players.Any(x => x.IsHumanActive); // every 10 sec & at least 1 active human player
                var scatterMap = new ScatterReadMap();
                var round1 = scatterMap.AddRound();
                ScatterReadRound round2 = null;
                if (checkPos) // allocate and add extra rounds to map
                {
                    round2 = scatterMap.AddRound();
                }
                for (int i = 0; i < players.Length; i++)
                {
                    var player = players[i];
                    if (player.LastUpdate) // player may be dead/exfil'd
                    {
                        if (player.Type is PlayerType.LocalPlayer)
                        {
                            if (IsRaidEnded(player.Base))
                            {
                                throw new RaidEnded();
                            }
                            //var corpse = round1.AddEntry(i, 10, player.CorpsePtr, typeof(ulong));
                        }
                    }
                    else
                    {
                        if (player.Type is PlayerType.LocalPlayer)
                        {
                            var rotation = round1.AddEntry(i,7,player.MovementContext + Offsets.MovementContext.Rotation,typeof(System.Numerics.Vector2),null);
                            var posAddr = player.TransformScatterReadParameters;
                            var indices = round1.AddEntry(i,8,posAddr.Item1,typeof(List<int>),posAddr.Item2);
                            indices.SizeMult = 4;
                            var vertices = round1.AddEntry(i,9,posAddr.Item3,typeof(List<Vector128<float>>),posAddr.Item4);
                            vertices.SizeMult = 16;
                            if (checkPos && player.IsHumanActive)
                            {
                                var hierarchy = round1.AddEntry(i,11,player.TransformInternal,typeof(ulong),null,Offsets.TransformInternal.Hierarchy);
                                var indicesAddr = round2?.AddEntry(i,12,hierarchy,typeof(ulong),null,Offsets.TransformHierarchy.Indices);
                                var verticesAddr = round2?.AddEntry(i,13,hierarchy,typeof(ulong),null,Offsets.TransformHierarchy.Vertices);
                            }
                            //var corpse = round1.AddEntry(i, 10, player.CorpsePtr, typeof(ulong));
                        }
                        else if (player.Type is PlayerType.AIScav) {
                            var rotation = round1.AddEntry(i,7,player.MovementContext + Offsets.MovementContext.Rotation,typeof(System.Numerics.Vector2),null);
                            var posAddr = player.TransformScatterReadParameters;
                            var indices = round1.AddEntry(i,8,posAddr.Item1,typeof(List<int>),posAddr.Item2);
                            indices.SizeMult = 4;
                            var vertices = round1.AddEntry(i,9,posAddr.Item3,typeof(List<Vector128<float>>),posAddr.Item4);
                            vertices.SizeMult = 16;
                            if (checkPos && player.IsHumanActive)
                            {
                                var hierarchy = round1.AddEntry(i,11,player.TransformInternal,typeof(ulong),null,Offsets.TransformInternal.Hierarchy);
                                var indicesAddr = round2?.AddEntry(i,12,hierarchy,typeof(ulong),null,Offsets.TransformHierarchy.Indices);
                                var verticesAddr = round2?.AddEntry(i,13,hierarchy,typeof(ulong),null,Offsets.TransformHierarchy.Vertices);
                            }
                        }
                        else //if (player.Type is PlayerType.PMC or PlayerType.USEC or PlayerType.BEAR or PlayerType.AIScav)
                        {
                            var rotation = round1.AddEntry(i,7,player.MovementContext + Offsets.ObserverdPlayerMovementContext.Rotation,typeof(System.Numerics.Vector2), null);
                            var posAddr = player.TransformScatterReadParameters;
                            var indices = round1.AddEntry(i,8,posAddr.Item1,typeof(List<int>),posAddr.Item2);
                            indices.SizeMult = 4;
                            var vertices = round1.AddEntry(i,9,posAddr.Item3,typeof(List<Vector128<float>>),posAddr.Item4);
                            vertices.SizeMult = 16;
                            if (checkPos && player.IsHumanActive)
                            {
                                var hierarchy = round1.AddEntry(i,11,player.TransformInternal,typeof(ulong),null,Offsets.TransformInternal.Hierarchy);
                                var indicesAddr = round2?.AddEntry(i,12,hierarchy,typeof(ulong),null,Offsets.TransformHierarchy.Indices);
                                var verticesAddr = round2?.AddEntry(i,13,hierarchy,typeof(ulong),null,Offsets.TransformHierarchy.Vertices);
                            }
                        }
                    }
                }
                scatterMap.Execute(players.Length); // Execute scatter read

                for (int i = 0; i < players.Length; i++)
                {
                    var player = players[i];
                    if (_localPlayerGroup != -100 && player.GroupID != -1 && player.IsHumanHostile)
                    { // Teammate check
                        if (player.GroupID == _localPlayerGroup)
                            player.Type = PlayerType.Teammate;
                    }
                    if (player.LastUpdate) // player may be dead/exfil'd
                    {
                        //var corpse = (ulong?)scatterMap.Results[i][10].Result;
                        //Debug.WriteLine($"Corpse: {corpse}");
                        //if (corpse is not null && corpse != 0x0) // dead
                        //{
                        //    player.IsAlive = false;
                        //}
                        player.IsActive = false; // mark inactive
                        player.LastUpdate = false; // Last update processed, clear flag
                    }
                    else
                    {
                        bool posOK = true;
                        if (checkPos && player.IsHumanActive) // Position integrity check for active human players
                        {
                            if (
                                scatterMap.Results[i].TryGetValue(12, out var i12)
                                && i12?.Result is not null
                                && scatterMap.Results[i].TryGetValue(13, out var i13)
                                && i13?.Result is not null
                            )
                            {
                                var indicesAddr = (ulong)i12.Result;
                                var verticesAddr = (ulong)i13.Result;
                                if (
                                    player.IndicesAddr != indicesAddr
                                    || player.VerticesAddr != verticesAddr
                                ) // check if any addr changed
                                {
                                    Program.Log(
                                        $"WARNING - Transform has changed for Player '{player.Name}'"
                                    );
                                    player.SetPosition(null); // alloc new transform
                                    posOK = false; // Don't try update pos with old vertices/indices
                                }
                            }
                        }
                        bool p1 = true;
                        if (player.Type is PlayerType.Default)
                        {
                            Debug.WriteLine($"Player type: {player.Type}");
                            Debug.WriteLine("Continuing...");
                            continue;
                        }
                        if (player.IsLocalPlayer)
                        {

                            var rotation = scatterMap.Results[i][7].Result;
                            //Debug.WriteLine($"Player Rotation: {rotation}");
                            bool p2 = player.SetRotation(rotation);
                            var posBufs = new object[2]
                            {
                                scatterMap.Results[i][8].Result,
                                scatterMap.Results[i][9].Result
                            };
                            bool p3 = true;
                            if (posOK)
                                p3 = player.SetPosition(posBufs);
                            //player.SetKD(); // set KD if not already set
                            if (p1 && p2 && p3)
                                player.ErrorCount = 0;
                            else
                                player.ErrorCount++;
                        }
                        else //if (player.Type is PlayerType.PMC or PlayerType.USEC or PlayerType.BEAR or PlayerType.AIScav)
                        {
                            var rotation = scatterMap.Results[i][7].Result;
                            //Debug.WriteLine($"Player Rotation: {rotation}");
                            bool p2 = player.SetRotation(rotation);
                            var posBufs = new object[2]
                            {
                                scatterMap.Results[i][8].Result,
                                scatterMap.Results[i][9].Result
                            };
                            bool p3 = true;
                            if (posOK)
                                p3 = player.SetPosition(posBufs);
                            //player.SetKD(); // set KD if not already set
                            if (p1 && p2 && p3)
                                player.ErrorCount = 0;
                            else
                                player.ErrorCount++;
                        }
                    }
                }
                //if (checkHealth) _healthSw.Restart();
                if (checkPos)
                    _posSw.Restart();
            }
            catch (DMAShutdown)
            {
                throw;
            }
            catch (Exception ex)
            {
                Program.Log($"CRITICAL ERROR - UpdatePlayers Loop FAILED: {ex}");
            }
        }

        #endregion
    }
}
