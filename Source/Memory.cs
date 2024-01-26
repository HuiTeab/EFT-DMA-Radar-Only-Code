using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using vmmsharp;

namespace eft_dma_radar
{
    internal static class Memory
    {
        /// <summary>
        /// Adjust this to achieve desired mem/sec performance. Higher = slower, Lower = faster.
        /// </summary>
        private const int LOOP_DELAY = 150;

        private static volatile bool _running = false;
        private static volatile bool _restart = false;
        private static volatile bool _ready = false;
        private static readonly Thread _worker;
        private static uint _pid;
        private static ulong _unityBase;
        private static Game _game;
        private static int _ticksCounter = 0;
        private static volatile int _ticks = 0;
        private static readonly Stopwatch _tickSw = new();
        #region Getters
        public static int Ticks
        {
            get => _ticks;
        }
        public static bool InGame
        {
            get => _game?.InGame ?? false;
        }
        public static bool Ready
        {
            get => _ready;
        }
        public static bool InHideout
        {
            get => _game?.InHideout ?? false;
        }
        public static bool IsScav
        {
            get => _game?.IsScav ?? false;
        }
        public static string MapName
        {
            get => _game?.MapName;
        }
        public static ReadOnlyDictionary<string, Player> Players
        {
            get => _game?.Players;
        }
        public static LootManager Loot
        {
            get => _game?.Loot;
        }
        
        public static ReadOnlyCollection<Grenade> Grenades
        {
            get => _game?.Grenades;
        }
        public static bool LoadingLoot
        {
            get => _game?.LoadingLoot ?? false;
        }
        public static ReadOnlyCollection<Exfil> Exfils
        {
            get => _game?.Exfils;
        }
        #endregion

        #region Startup
        /// <summary>
        /// Constructor
        /// </summary>
        static Memory()
        {
            try
            {
                Program.Log("Loading memory module...");
                if (!File.Exists("mmap.txt"))
                {
                    Program.Log("No MemMap, attempting to generate...");
                    if (!vmm.Initialize("-printf", "-v", "-device", "FPGA"))
                        throw new DMAException("Unable to initialize DMA Device while attempting to generate MemMap!");
                    GetMemMap();
                    vmm.Close(); // Close back down, re-init w/ map
                }
                if (!vmm.Initialize("-printf", "-v", "-device", "FPGA", "-memmap", "mmap.txt")) // Initialize DMA device
                    throw new DMAException("ERROR initializing DMA Device! If you do not have a memory map (mmap.txt) edit the constructor in Memory.cs");
                Program.Log("Starting Memory worker thread...");
                _worker = new Thread(() => Worker()) 
                { 
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _running = true;
                _worker.Start(); // Start new background thread to do memory operations on
                Program.HideConsole();
                _tickSw.Start(); // Start stopwatch for Mem Ticks/sec
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "DMA Init", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(-1);
            }
        }
        /// <summary>
        /// Generates a Physical Memory Map (mmap.txt) to enhance performance/safety.
        /// </summary>
        private static void GetMemMap()
        {
            try
            {
                var map = vmm.Map_GetPhysMem();
                if (map.Length == 0) throw new Exception("Map_GetPhysMem() returned no entries!");
                var sb = new StringBuilder();
                for (int i = 0; i < map.Length; i++)
                {
                    sb.AppendLine($"{i.ToString("D4")}  {map[i].pa.ToString("x")}  -  {(map[i].pa + map[i].cb - 1).ToString("x")}  ->  {map[i].pa.ToString("x")}");
                }
                File.WriteAllText("mmap.txt", sb.ToString());
            }
            catch (Exception ex)
            {
                throw new DMAException("Unable to generate MemMap!", ex);
            }
        }

        /// <summary>
        /// Gets EFT Process ID.
        /// </summary>
        private static bool GetPid()
        {
            try
            {
                ThrowIfDMAShutdown();
                if (!vmm.PidGetFromName("EscapeFromTarkov.exe", out _pid))
                    throw new DMAException("Unable to obtain PID. Game is not running.");
                else
                {
                    Program.Log($"EscapeFromTarkov.exe is running at PID {_pid}");
                    return true;
                }
            }
            catch (DMAShutdown) { throw; }
            catch (Exception ex)
            {
                Program.Log($"ERROR getting PID: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Gets module base entry address for UnityPlayer.dll
        /// </summary>
        private static bool GetModuleBase()
        {
            try
            {
                ThrowIfDMAShutdown();
                _unityBase = vmm.ProcessGetModuleBase(_pid, "UnityPlayer.dll");
                if (_unityBase == 0) throw new DMAException("Unable to obtain Base Module Address. Game may not be running");
                else
                {
                    Program.Log($"Found UnityPlayer.dll at 0x{_unityBase.ToString("x")}");
                    return true;
                }
            }
            catch (DMAShutdown) { throw; }
            catch (Exception ex)
            {
                Program.Log($"ERROR getting module base: {ex}");
                return false;
            }
        }
        #endregion

        #region MemoryThread
        /// <summary>
        /// Main worker thread to perform DMA Reads on.
        /// </summary>
        private static void Worker()
        {
            try
            {
                while (true)
                {
                    Program.Log("Attempting to find EFT Process...");
                    while (true) // Startup loop
                    {
                        if (GetPid()
                        && GetModuleBase()
                        )
                        {
                            Program.Log($"EFT process located! Startup successful.");
                            break;
                        }
                        else
                        {
                            Program.Log("EFT startup failed, trying again in 15 seconds...");
                            Thread.Sleep(15000);
                        }
                    }
                    while (true) // Game is running
                    {
                        _game = new Game(_unityBase);
                        Player.Reset(); // Reset static assets for a new raid/game.
                        try
                        {
                            Program.Log("Ready -- Waiting for raid...");
                            _ready = true;
                            Task.Run(async () => await _game.WaitForGameAsync()).Wait();
                            while (_game.InGame || _game.InHideout)
                            {
                                if (_tickSw.ElapsedMilliseconds >= 1000)
                                {
                                    _ticks = _ticksCounter; // push count to public property
                                    _ticksCounter = 0;
                                    _tickSw.Restart();
                                }
                                else _ticksCounter++;
                                if (_restart)
                                {
                                    Program.Log("Restarting game... getting fresh GameWorld instance");
                                    _restart = false;
                                    break;
                                }
                                _game.GameLoop();
                                Thread.SpinWait(LOOP_DELAY * 1000); // rate-limit, high performance
                            }
                        }
                        catch (GameNotRunningException) { break; }
                        catch (ThreadInterruptedException) { throw; }
                        catch (DMAShutdown) { throw; }
                        catch (Exception ex)
                        {
                            Program.Log($"CRITICAL ERROR in Game Loop: {ex}");
                        }
                        finally
                        {
                            _ready = false;
                            Thread.Sleep(100);
                        }
                    }
                    Program.Log("Game is no longer running! Attempting to restart...");
                }
            }
            catch (ThreadInterruptedException) { } // Do nothing
            catch (DMAShutdown) { } // Do nothing
            catch (Exception ex)
            {
                Environment.FailFast($"FATAL ERROR on Memory Thread: {ex}"); // Force shutdown asap
            }
            finally
            {
                Program.Log("Uninitializing DMA Device...");
                vmm.Close(); // Un-init DMA
                Program.Log("Memory Thread closing down gracefully...");
            }
        }
        #endregion

        #region ScatterRead
        /// <summary>
        /// Performs multiple reads in one sequence, significantly faster than single reads.
        /// Designed to run without throwing unhandled exceptions, which will ensure the maximum amount of
        /// reads are completed OK even if a couple fail.
        /// </summary>
        // Credit to asmfreak https://www.unknowncheats.me/forum/3345474-post27.html
        // Major edits/re-working done by Frost
        public static void ReadScatter(ScatterReadEntry[] entries, Dictionary<int, Dictionary<int, ScatterReadEntry>> results)
        {
            var pagesToRead = new HashSet<ulong>(); // Will contain each unique page only once to prevent reading the same page multiple times
            var entriesToSkip = new HashSet<int>();
            for (int i = 0; i < entries.Length; i++) // First loop through all entries - GET INFO
            {
                // Parse Addr
                if (entries[i].Addr is not null) // Ensure address field is set
                {
                    var addrType = entries[i].Addr.GetType();
                    if (addrType == typeof(ScatterReadEntry)) // Check if the address references another ScatterRead Result
                    {
                        var item = (ScatterReadEntry)entries[i].Addr; // Cast addr to ScatterReadEntry
                        if (item.Result is not null)
                        {
                            entries[i].Addr = (ulong)item.Result; // Use the referenced ScatterReadEntry's 'result' as the address
                        }
                        else entries[i].Addr = (ulong)0x0; // Failed, set to 0x0 (will be skipped)
                    }
                }
                else entries[i].Addr = (ulong)0x0; // object is null, set to 0x0 (will be skipped)

                // Parse Size
                if (entries[i].Size is not null) // Check if size field is set
                {
                    var sizeType = entries[i].Size.GetType();
                    if (sizeType == typeof(ScatterReadEntry)) // Check if the size references another ScatterRead Result
                    {
                        var item = (ScatterReadEntry)entries[i].Size; // Cast size to ScatterReadEntry
                        if (item.Result is not null)
                        {
                            entries[i].Size = (int)item.Result; // Use the referenced ScatterReadEntry's 'result' as the size
                        }
                        else entries[i].Size = (int)0; // Failed, set to 0 (may be a value type)
                    }
                }
                else entries[i].Size = (int)0; // object is null, set to 0 (may be a value type)
                uint size; // total size of object
                if ((uint)(int)entries[i].Size > 0) // Ref type, size value is set
                    size = (uint)(int)entries[i].Size * (uint)entries[i].SizeMult;
                else if (entries[i].Type.IsValueType) // Ensure value type before attempting to marshal size
                    size = (uint)Marshal.SizeOf(entries[i].Type); // Value type, use marshaler to determine size of type
                else size = (uint)0; // will be skipped

                // INTEGRITY CHECK - Make sure the read is valid and within range
                if ((ulong)entries[i].Addr == 0x0 || size == 0 || size > PAGE_SIZE)
                {
                    entriesToSkip.Add(i);
                    entries[i].Result = null;
                    results[entries[i].Index].Add(entries[i].Id, entries[i]);
                    continue;
                }
                // location of object
                ulong readAddress = (ulong)entries[i].Addr + entries[i].Offset;
                // get the number of pages
                uint numPages = ADDRESS_AND_SIZE_TO_SPAN_PAGES(readAddress, size);

                //loop all the pages we would need
                for (int p = 0; p < numPages; p++)
                {
                    ulong page = PAGE_ALIGN(readAddress) + PAGE_SIZE * (uint)p;
                    pagesToRead.Add(page);
                }
            }
            ThrowIfDMAShutdown();
            var scatters = vmm.MemReadScatter(_pid, vmm.FLAG_NOCACHE, pagesToRead.ToArray()); // execute scatter read

            for (int i = 0; i < entries.Length; i++) // Second loop through all entries - PARSE RESULTS
            {
                if (entriesToSkip.Contains(i)) // Skip this entry, leaves result as null
                {
                    continue;
                }
                bool isFailed = false;

                ulong readAddress = (ulong)entries[i].Addr + entries[i].Offset; // location of object
                uint pageOffset = BYTE_OFFSET(readAddress); // Get object offset from the page start address

                uint size; // total size of object
                if ((uint)(int)entries[i].Size > 0) // Ref type, size value is set
                    size = (uint)(int)entries[i].Size * (uint)entries[i].SizeMult;
                else size = (uint)Marshal.SizeOf(entries[i].Type); // Value type, use marshaler to determine size of type
                Memory<byte> buffer = new byte[size]; // temporary buffer to store result
                int bytesCopied = 0; // track number of bytes copied to ensure nothing is missed
                uint cb = Math.Min(size, (uint)PAGE_SIZE - pageOffset); // bytes to read this page

                uint numPages = ADDRESS_AND_SIZE_TO_SPAN_PAGES(readAddress, size); // number of pages to read from (in case result spans multiple pages)

                for (int p = 0; p < numPages; p++)
                {
                    ulong page = PAGE_ALIGN(readAddress) + PAGE_SIZE * (uint)p; // get current page addr
                    var entry = scatters.First(x => x.qwA == page); // retrieve page of mem needed
                    if (entry.f) // read succeeded -> copy to buffer
                    {
                        entry.pb
                            .Slice((int)pageOffset, (int)cb)
                            .CopyTo(buffer.Slice(bytesCopied, (int)cb)); // Copy bytes to buffer
                        bytesCopied += (int)cb;
                    }
                    else // read failed -> set failed flag
                    {
                        isFailed = true;
                        break;
                    }

                    cb = (uint)PAGE_SIZE; // set bytes to read next page
                    if (((pageOffset + size) & 0xfff) != 0)
                        cb = ((pageOffset + size) & 0xfff);

                    pageOffset = 0; // Next page (if any) should start at 0
                }
                try // Parse buffer and set result
                {
                    if (isFailed) throw new DMAException("Scatter read failed!");
                    else if (bytesCopied != size) throw new DMAException("Incomplete buffer copy!");
                    else if (entries[i].Type == typeof(ulong)) // assumed pointer
                    {
                        var addr = MemoryMarshal.Read<ulong>(buffer.Span);
                        if (addr == 0x0) throw new NullPtrException();
                        entries[i].Result = addr;
                    }
                    else if (entries[i].Type == typeof(float))
                    {
                        entries[i].Result = MemoryMarshal.Read<float>(buffer.Span);
                    }
                    else if (entries[i].Type == typeof(System.Numerics.Vector2))
                    {
                        entries[i].Result = MemoryMarshal.Read<System.Numerics.Vector2>(buffer.Span);
                    }
                    else if (entries[i].Type == typeof(int))
                    {
                        entries[i].Result = MemoryMarshal.Read<int>(buffer.Span);
                    }
                    else if (entries[i].Type == typeof(bool))
                    {
                        entries[i].Result = MemoryMarshal.Read<bool>(buffer.Span);
                    }
                    else if (entries[i].Type == typeof(UnityString))
                    {
                        entries[i].Result = Encoding.Unicode.GetString(buffer.Span);
                    }
                    else if (entries[i].Type == typeof(string))
                    {
                        entries[i].Result = Encoding.Default.GetString(buffer.Span).Split('\0')[0];
                    }
                    else if (entries[i].Type == typeof(Memory<byte>))
                    {
                        entries[i].Result = buffer; // Store ref to mem buffer
                    }
                    else if (entries[i].Type == typeof(List<int>)) // indices
                    {
                        var spanBuf = buffer.Span;
                        var list = new List<int>();
                        if ((int)entries[i].Size > 0)
                        {
                            for (var index = 0; index < spanBuf.Length; index += 4)
                            {
                                list.Add(MemoryMarshal.Read<int>(spanBuf.Slice(index, 4)));
                            }
                        }
                        else throw new ArgumentOutOfRangeException();
                        entries[i].Result = list;
                    }
                    else if (entries[i].Type == typeof(List<Vector128<float>>)) // vertices
                    {
                        var spanBuf = buffer.Span;
                        var list = new List<Vector128<float>>();
                        int count = (int)entries[i].Size;
                        for (var z = 0; z < count * 16; z += 16)
                        {
                            var result = Vector128.Create(
                                spanBuf[z], spanBuf[z + 1], spanBuf[z + 2], spanBuf[z + 3], spanBuf[z + 4], spanBuf[z + 5],
                                spanBuf[z + 6], spanBuf[z + 7], spanBuf[z + 8], spanBuf[z + 9], spanBuf[z + 10], spanBuf[z + 11],
                                spanBuf[z + 12], spanBuf[z + 13], spanBuf[z + 14], spanBuf[z + 15])
                                .AsSingle();

                            list.Add(result);
                        }
                        entries[i].Result = list;
                    }
                }
                catch (Exception ex)
                {
                    entries[i].Result = null;
                    Program.Log($"ERROR parsing result from Scatter Read at index {i}: {ex}");
                }
                finally
                {
                    results[entries[i].Index].Add(entries[i].Id, entries[i]);
                }
            }
        }
        #endregion

        #region ReadMethods
        /// <summary>
        /// Read memory into a Span.
        /// </summary>
        public static Span<byte> ReadBuffer(ulong addr, int size)
        {
            if ((uint)size > PAGE_SIZE * 1500) throw new DMAException("Buffer length outside expected bounds!");
            ThrowIfDMAShutdown();
            var buf = vmm.MemRead(_pid, addr, (uint)size, vmm.FLAG_NOCACHE);
            if (buf.Length != size) throw new DMAException("Incomplete memory read!");
            return buf;
        }

        /// <summary>
        /// Read a chain of pointers and get the final result.
        /// </summary>
        public static ulong ReadPtrChain(ulong ptr, uint[] offsets)
        {
            ulong addr = 0;
            try { addr = ReadPtr(ptr + offsets[0]); }
            catch (Exception ex) { throw new DMAException($"ERROR reading pointer chain at index 0, addr 0x{ptr.ToString("X")} + 0x{offsets[0].ToString("X")}", ex); }
            for (int i = 1; i < offsets.Length; i++)
            {
                try { addr = ReadPtr(addr + offsets[i]); }
                catch (Exception ex) { throw new DMAException($"ERROR reading pointer chain at index {i}, addr 0x{addr.ToString("X")} + 0x{offsets[i].ToString("X")}", ex); }
            }
            return addr;
        }
        /// <summary>
        /// Resolves a pointer and returns the memory address it points to.
        /// </summary>
        public static ulong ReadPtr(ulong ptr)
        {
            var addr = ReadValue<ulong>(ptr);
            if (addr == 0x0) throw new NullPtrException();
            else return addr;
        }

        /// <summary>
        /// Read value type/struct from specified address.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to read from.</param>
        public static T ReadValue<T>(ulong addr)
            where T : struct
        {
            try
            {
                int size = Marshal.SizeOf(typeof(T));
                ThrowIfDMAShutdown();
                var buf = vmm.MemRead(_pid, addr, (uint)size, vmm.FLAG_NOCACHE);
                return MemoryMarshal.Read<T>(buf);
            }
            catch (Exception ex)
            {
                throw new DMAException($"ERROR reading {typeof(T)} value at 0x{addr.ToString("X")}", ex);
            }
        }

        /// <summary>
        /// Read null terminated string.
        /// </summary>
        /// <param name="length">Number of bytes to read.</param>
        /// <exception cref="DMAException"></exception>
        public static string ReadString(ulong addr, uint length) // read n bytes (string)
        {
            try
            {
                if (length > PAGE_SIZE) throw new DMAException("String length outside expected bounds!");
                ThrowIfDMAShutdown();
                var buf = vmm.MemRead(_pid, addr, length, vmm.FLAG_NOCACHE);
                return Encoding.Default.GetString(buf).Split('\0')[0];
            }
            catch (Exception ex)
            {
                throw new DMAException($"ERROR reading string at 0x{addr.ToString("X")}", ex);
            }
        }

        /// <summary>
        /// Read UnityEngineString structure
        /// </summary>
        public static string ReadUnityString(ulong addr)
        {
            try
            {
                var length = (uint)ReadValue<int>(addr + Offsets.UnityString.Length);
                if (length > PAGE_SIZE) throw new DMAException("String length outside expected bounds!");
                ThrowIfDMAShutdown();
                var buf = vmm.MemRead(_pid, addr + Offsets.UnityString.Value, length * 2, vmm.FLAG_NOCACHE);
                return Encoding.Unicode.GetString(buf).TrimEnd('\0');;
            }
            catch (Exception ex)
            {
                throw new DMAException($"ERROR reading UnityString at 0x{addr.ToString("X")}", ex);
            }
        }
        #endregion

        #region Methods
        /// <summary>
        /// Sets restart flag to re-initialize the game/pointers from the bottom up.
        /// </summary>
        public static void Restart()
        {
            if (InGame)
            {
                _restart = true;
            }
        }

        /// <summary>
        /// Refresh loot only.
        /// </summary>
        public static void RefreshLoot()
        {
            _game?.RefreshLoot();
        }
        /// <summary>
        /// Close down DMA Device Connection.
        /// </summary>
        public static void Shutdown()
        {
            if (_running)
            {
                Program.Log("Closing down Memory Thread...");
                _running = false;
                _worker.Interrupt(); // Interrupt thread if sleeping
                while (_worker.IsAlive) Thread.SpinWait(100);
            }
        }

        private static void ThrowIfDMAShutdown()
        {
            if (!_running) throw new DMAShutdown("Memory Thread/DMA is shutting down!");
        }


        /// Mem Align Functions Ported from Win32 (C Macros)
        private const ulong PAGE_SIZE = 0x1000;
        private const int PAGE_SHIFT = 12;

        /// <summary>
        /// The PAGE_ALIGN macro takes a virtual address and returns a page-aligned
        /// virtual address for that page.
        /// </summary>
        private static ulong PAGE_ALIGN(ulong va)
        {
            return (va & ~(PAGE_SIZE - 1));
        }
        /// <summary>
        /// The ADDRESS_AND_SIZE_TO_SPAN_PAGES macro takes a virtual address and size and returns the number of pages spanned by the size.
        /// </summary>
        private static uint ADDRESS_AND_SIZE_TO_SPAN_PAGES(ulong va, uint size)
        {
            return (uint)((BYTE_OFFSET(va) + (size) + (PAGE_SIZE - 1)) >> PAGE_SHIFT);
        }

        /// <summary>
        /// The BYTE_OFFSET macro takes a virtual address and returns the byte offset
        /// of that address within the page.
        /// </summary>
        private static uint BYTE_OFFSET(ulong va)
        {
            return (uint)(va & (PAGE_SIZE - 1));
        }
        #endregion
    }

    #region Exceptions
    public class DMAException : Exception
    {
        public DMAException()
        {
        }

        public DMAException(string message)
            : base(message)
        {
        }

        public DMAException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    public class NullPtrException : Exception
    {
        public NullPtrException()
        {
        }

        public NullPtrException(string message)
            : base(message)
        {
        }

        public NullPtrException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    public class DMAShutdown : Exception
    {
        public DMAShutdown()
        {
        }

        public DMAShutdown(string message)
            : base(message)
        {
        }

        public DMAShutdown(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
    #endregion
}
