using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;

namespace eft_dma_radar
{
    public class ExfilManager
    {

        private bool IsAtHideout
        {
            get => Memory.InHideout;
        }

        private bool IsScav
        {
            get => Memory.IsScav;
        }
        private readonly Stopwatch _sw = new();
        /// <summary>
        /// List of PMC Exfils in Local Game World and their position/status.
        /// </summary>
        public ReadOnlyCollection<Exfil> Exfils { get; }

        public ExfilManager(ulong localGameWorld)
        {
            //If we are in hideout, we don't need to do anything.
            if (IsAtHideout)
            {
                Debug.WriteLine("In Hideout, not loading exfils.");
                return;
            }
            var list = new List<Exfil>();

            var exfilController = Memory.ReadPtr(localGameWorld + Offsets.LocalGameWorld.ExfilController);

            ulong exfilPoints;
            if (IsScav) {
                exfilPoints = Memory.ReadPtr(exfilController + 0x28);

                var count = Memory.ReadValue<int>(exfilPoints + Offsets.ExfilController.ExfilCount);
                if (count < 1 || count > 24) throw new ArgumentOutOfRangeException();
                for (uint i = 0; i < count; i++)
                {
                    var exfilAddr = Memory.ReadPtr(exfilPoints + Offsets.UnityListBase.Start + (i * 0x08));
                    var exfil = new Exfil(exfilAddr);
                    list.Add(exfil);
                }
            }else {
                var localPlayer = Memory.ReadPtr(localGameWorld + Offsets.LocalGameWorld.MainPlayer);
                Console.WriteLine($"LocalPlayer: {localPlayer:X}");
                var classNamePtr = Memory.ReadPtrChain(localPlayer, Offsets.UnityClass.Name);
                var classNameString = Memory.ReadString(classNamePtr, 64).Replace("\0", string.Empty);
                Console.WriteLine($"LocalPlayerClass: {classNameString}");
                var localPlayerProfile = Memory.ReadPtr(localPlayer + Offsets.Player.Profile);
                Console.WriteLine($"LocalPlayerProfile: {localPlayerProfile:X}");
                var localPlayerInfo = Memory.ReadPtr(localPlayerProfile + Offsets.Profile.PlayerInfo);
                Console.WriteLine($"LocalPlayerInfo: {localPlayerInfo:X}");
                var localPlayerEntryPoint = Memory.ReadPtr(localPlayerInfo + 0x30);
                Console.WriteLine($"LocalPlayerEntryPoint: {localPlayerEntryPoint:X}");
                var localPlayerEntryPointString = Memory.ReadUnityString(localPlayerEntryPoint);
                Console.WriteLine($"LocalPlayerEntryPointString: {localPlayerEntryPointString}");


                exfilPoints = Memory.ReadPtr(exfilController + Offsets.ExfilController.ExfilList);
                var count = Memory.ReadValue<int>(exfilPoints + Offsets.ExfilController.ExfilCount);
                if (count < 1 || count > 24) throw new ArgumentOutOfRangeException();
                for (uint i = 0; i < count; i++)
                {
                    var exfilAddr = Memory.ReadPtr(exfilPoints + Offsets.UnityListBase.Start + (i * 0x8));
                    var eligibleEntryPoints = Memory.ReadPtr(exfilAddr + 0x80);
                    var eligibleEntryPointsCount = Memory.ReadValue<int>(eligibleEntryPoints + 0x18);
                    for (uint j = 0; j < eligibleEntryPointsCount; j++)
                    {
                        var entryPoint = Memory.ReadPtr(eligibleEntryPoints + 0x20 + (j * 0x8));
                        var entryPointString = Memory.ReadUnityString(entryPoint);
                        if (entryPointString.ToLower() == localPlayerEntryPointString.ToLower())
                        {
                            var exfil = new Exfil(exfilAddr);
                            list.Add(exfil);
                            break;
                        }
                    }
                }
            }
            Exfils = new(list); // update readonly ref
            UpdateExfils(); // Get initial statuses
            _sw.Start();
        }

        /// <summary>
        /// Checks if Exfils are due for a refresh, and then refreshes them.
        /// </summary>
        public void Refresh()
        {
            if (_sw.ElapsedMilliseconds < 5000) return;
            UpdateExfils();
            _sw.Restart();
        }

        /// <summary>
        /// Updates exfil statuses.
        /// </summary>
        private void UpdateExfils()
        {
            try
            {
                var map = new ScatterReadMap();
                var round1 = map.AddRound();
                for (int i = 0; i < Exfils.Count; i++)
                {
                    round1.AddEntry(i, 0, Exfils[i].BaseAddr + Offsets.Exfil.Status, typeof(int));
                }
                map.Execute(Exfils.Count);
                for (int i = 0; i < Exfils.Count; i++)
                {
                    try
                    {
                        var status = (int)map.Results[i][0].Result;
                        Exfils[i].UpdateStatus(status);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    #region Classes_Enums
    public class Exfil
    {
        public ulong BaseAddr { get; }
        public Vector3 Position { get; }
        public ExfilStatus Status { get; private set; } = ExfilStatus.Closed;

        public Exfil(ulong baseAddr)
        {
            this.BaseAddr = baseAddr;
            var transform_internal = Memory.ReadPtrChain(baseAddr, Offsets.GameObject.To_TransformInternal);
            Position = new Transform(transform_internal).GetPosition();
        }

        /// <summary>
        /// Update status of exfil.
        /// </summary>
        public void UpdateStatus(int status)
        {
            switch (status)
            {
                case 1: // NotOpen
                    this.Status = ExfilStatus.Closed;
                    break;
                case 2: // IncompleteRequirement
                    this.Status = ExfilStatus.Pending;
                    break;
                case 3: // Countdown
                    this.Status = ExfilStatus.Open;
                    break;
                case 4: // Open
                    this.Status = ExfilStatus.Open;
                    break;
                case 5: // Pending
                    this.Status = ExfilStatus.Pending;
                    break;
                case 6: // AwaitActivation
                    this.Status = ExfilStatus.Pending;
                    break;
                default:
                    break;
            }
        }
    }

    public enum ExfilStatus
    {
        Open,
        Pending,
        Closed
    }
    #endregion
}
