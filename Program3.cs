using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Households
{
    class Program3
    {
        public static void Main3(string[] args)
        {
            for (int r = 0; r < Globals.reps; r++)
            {
                DateTime t0 = DateTime.Now;
                var sim = new Simulation3();

                for (int j = 0; j < Globals.nPop3; j++)
                {
                    sim.CreateHousehold(10 * j + 0, new[] { 18, 18 }, new[] { EHf.AlmenGym, EHf.AlmenGym });
                    sim.CreateHousehold(10 * j + 1, new[] { 64, 62 }, new[] { EHf.Kandidat, EHf.ProfBachelor });
                    sim.CreateHousehold(10 * j + 2, new[] { 32, 5 }, new[] { EHf.PhD, EHf.Ingen });
                    sim.CreateHousehold(10 * j + 3, new[] { 25, 25 }, new[] { EHf.AlmenGym, EHf.AlmenGym });
                    sim.CreateHousehold(10 * j + 4, new[] { 25, 25 }, new[] { EHf.AlmenGym, EHf.AlmenGym });
                    sim.CreateHousehold(10 * j + 5, new[] { 25, 25 }, new[] { EHf.AlmenGym, EHf.AlmenGym });
                    sim.CreateHousehold(10 * j + 6, new[] { 25, 25 }, new[] { EHf.AlmenGym, EHf.AlmenGym });
                    sim.CreateHousehold(10 * j + 7, new[] { 25, 25 }, new[] { EHf.AlmenGym, EHf.AlmenGym });
                    sim.CreateHousehold(10 * j + 8, new[] { 25, 25 }, new[] { EHf.AlmenGym, EHf.AlmenGym });
                    sim.CreateHousehold(10 * j + 9, new[] { 25, 25 }, new[] { EHf.AlmenGym, EHf.AlmenGym });
                }

                DateTime t1 = DateTime.Now;
                for (int i = 0; i < Globals.simulations; i++)
                {
                    sim.RunYearlySimulationCycle();
                }
                DateTime t2 = DateTime.Now;

                Console.WriteLine("Load per size: " + Program.Pretty((double)Globals.nPop3 / (t1 - t0).TotalMilliseconds * 1000d));
                Console.WriteLine("Sim per size: " + Program.Pretty((double)Globals.nPop3 / (t2 - t1).TotalMilliseconds * 1000d));

                Console.WriteLine();
            }
        }
    }

    [DebuggerDisplay("Household ID: {Id}, People Count: {Size}, Blocks: {BlocksUsed}")]
    [DebuggerTypeProxy(typeof(HouseholdDebugView))]
    public readonly struct Household3
    {
        private readonly Simulation3 _sim;
        public int StartBlock { get; }
        public int Id => _sim.HouseholdIds[StartBlock];
        public int Size => _sim.HouseholdSizes[StartBlock];
        public int BlocksUsed => _sim.BlocksUsed[StartBlock];

        public Household3(Simulation3 sim, int startBlock)
        {
            _sim = sim;
            StartBlock = startBlock;
        }

        public PersonList3 Members => new PersonList3(_sim, StartBlock, Size);

        internal class HouseholdDebugView
        {
            private readonly Household3 _household;
            public HouseholdDebugView(Household3 household) => _household = household;

            public int Id => _household.Id;
            public int Size => _household.Size;
            public int BlocksUsed => _household.BlocksUsed;

            public Person3[] Persons
            {
                get
                {
                    var items = new Person3[_household.Size];
                    for (int i = 0; i < _household.Size; i++)
                    {
                        items[i] = _household.Members[i];
                    }
                    return items;
                }
            }
        }
    }

    [DebuggerDisplay("Age = {Age}, Hf = {Hf}")]
    public readonly struct Person3
    {
        private readonly Simulation3 _sim;
        public int AbsIdx { get; }

        public Person3(Simulation3 sim, int absoluteIndex)
        {
            _sim = sim;
            AbsIdx = absoluteIndex;
        }

        public int Age
        {
            get => _sim.Ages[AbsIdx];
            set => _sim.Ages[AbsIdx] = value;
        }

        public EHf Hf
        {
            get => _sim.Hfs[AbsIdx];
            set => _sim.Hfs[AbsIdx] = value;
        }
    }

    [DebuggerDisplay("Count = {Count}")]
    public readonly struct PersonList3
    {
        private readonly Simulation3 _sim;
        private readonly int _startBlock;
        private readonly int _size;

        public PersonList3(Simulation3 sim, int startBlock, int size)
        {
            _sim = sim;
            _startBlock = startBlock;
            _size = size;
        }

        public int Count => _size;

        public Person3 this[int index]
        {
            get
            {
                if (index < 0 || index >= _size) throw new IndexOutOfRangeException();
                return new Person3(_sim, _startBlock * Globals.slotSize + index);
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(_sim, _startBlock, _size);

        public struct Enumerator
        {
            private readonly Simulation3 _sim;
            private readonly int _startPersonIdx;
            private readonly int _size;
            private int _index;

            public Enumerator(Simulation3 sim, int startBlock, int size)
            {
                _sim = sim;
                _startPersonIdx = startBlock * Globals.slotSize;
                _size = size;
                _index = -1;
            }

            public bool MoveNext() => ++_index < _size;
            public Person3 Current => new Person3(_sim, _startPersonIdx + _index);
        }
    }

    public struct ExpansionEvent
    {
        public int HouseholdId;
        public int CurrentStartBlock;
        public ExpansionEvent(int id, int currentBlock) { HouseholdId = id; CurrentStartBlock = currentBlock; }
    }

    public class Simulation3
    {
        // Flat Array Layout
        public int[] Ages;
        public EHf[] Hfs;

        // Block-Level Metadata
        public int[] HouseholdIds;
        public int[] HouseholdSizes;
        public int[] BlocksUsed;

        // Registry tracking where a household ID begins in the blocks
        private Dictionary<int, int> _registry = new Dictionary<int, int>();
        private List<int> _aliveBlockIndices = new List<int>();
        private ConcurrentQueue<ExpansionEvent> _expansionQueue = new ConcurrentQueue<ExpansionEvent>();

        private int _nextFreeBlock = 0;

        public Simulation3()
        {
            int initialBlockCapacity = Globals.nPop3 * 12; // Extra buffer for expansions
            Ages = new int[initialBlockCapacity * Globals.slotSize];
            Hfs = new EHf[initialBlockCapacity * Globals.slotSize];

            HouseholdIds = new int[initialBlockCapacity];
            HouseholdSizes = new int[initialBlockCapacity];
            BlocksUsed = new int[initialBlockCapacity];
        }

        public void CreateHousehold(int id, int[] ages, EHf[] educations)
        {
            int size = ages.Length;
            int blocksNeeded = (size + Globals.slotSize - 1) / Globals.slotSize;
            if (blocksNeeded == 0) blocksNeeded = 1;

            int startBlock = _nextFreeBlock;
            _nextFreeBlock += blocksNeeded;

            HouseholdIds[startBlock] = id;
            HouseholdSizes[startBlock] = size;
            BlocksUsed[startBlock] = blocksNeeded;

            int startPersonIdx = startBlock * Globals.slotSize;
            for (int i = 0; i < size; i++)
            {
                Ages[startPersonIdx + i] = ages[i];
                Hfs[startPersonIdx + i] = educations[i];
            }

            _registry[id] = startBlock;
            _aliveBlockIndices.Add(startBlock);
        }

        public void RunYearlySimulationCycle()
        {            
            Parallel.ForEach(_aliveBlockIndices, new ParallelOptions { MaxDegreeOfParallelism = Globals.nThreads }, startBlock =>
            {
                Household3 household = new Household3(this, startBlock);
                int id = HouseholdIds[startBlock];
                int currentSize = HouseholdSizes[startBlock];
                int maxAllowedCapacity = BlocksUsed[startBlock] * Globals.slotSize;
                int startPersonIdx = startBlock * Globals.slotSize;

                // Age all current members
                if (true)
                {
                    foreach (Person3 person in household.Members)
                    {
                        person.Age++;
                    }
                }
                else
                {
                    for (int i = 0; i < currentSize; i++)
                    {
                        if (true)
                        {
                            Person3 person = new Person3(this, startPersonIdx + i);
                            person.Age++;
                        }
                        else
                        {
                            Ages[startPersonIdx + i]++; //fastest, and then Household3 household = ... can be omitted
                        }
                    }
                }

                // Rule % 10 == 0: One Birth
                if (id % 10 == 0)
                {
                    ExecuteBirthInPlace(startBlock, startPersonIdx, ref currentSize, maxAllowedCapacity, id);
                }
                // Rule % 10 == 1: One Potential Death and One Birth
                else if (id % 10 == 1)
                {
                    ExecuteDeathInPlace(startBlock, startPersonIdx, ref currentSize, 65);
                    if (currentSize > 0)
                    {
                        ExecuteBirthInPlace(startBlock, startPersonIdx, ref currentSize, maxAllowedCapacity, id);
                    }
                }
                // Rule % 10 == 2: One Potential Death
                else if (id % 10 == 2)
                {
                    ExecuteDeathInPlace(startBlock, startPersonIdx, ref currentSize, 33);
                }

                HouseholdSizes[startBlock] = currentSize;
            });
                        
            // Process removals (size == 0) and track elements we need to drop from active list
            for (int i = _aliveBlockIndices.Count - 1; i >= 0; i--)
            {
                int blockIdx = _aliveBlockIndices[i];
                if (HouseholdSizes[blockIdx] <= 0)
                {
                    _registry.Remove(HouseholdIds[blockIdx]);

                    // Fast swap-to-last inside the active looping index list
                    int lastListIdx = _aliveBlockIndices.Count - 1;
                    _aliveBlockIndices[i] = _aliveBlockIndices[lastListIdx];
                    _aliveBlockIndices.RemoveAt(lastListIdx);
                }
            }

            // 3. Large households...
            while (_expansionQueue.TryDequeue(out var ev))
            {
                // Verify it hasn't expanded or died already from secondary events
                if (!_registry.TryGetValue(ev.HouseholdId, out int currentBlock) || currentBlock != ev.CurrentStartBlock)
                    continue;

                int size = HouseholdSizes[currentBlock];
                int blocksNeeded = (size + Globals.slotSize - 1) / Globals.slotSize;

                int newStartBlock = _nextFreeBlock;
                _nextFreeBlock += blocksNeeded;

                // Migrate metadata
                HouseholdIds[newStartBlock] = ev.HouseholdId;
                HouseholdSizes[newStartBlock] = size;
                BlocksUsed[newStartBlock] = blocksNeeded;

                // Shift person data blocks to end of flat matrix
                Array.Copy(Ages, currentBlock * Globals.slotSize, Ages, newStartBlock * Globals.slotSize, size);
                Array.Copy(Hfs, currentBlock * Globals.slotSize, Hfs, newStartBlock * Globals.slotSize, size);

                // Update structural registry trackers
                _registry[ev.HouseholdId] = newStartBlock;

                // Swap index trace pointer inside alive lists
                int listIdx = _aliveBlockIndices.IndexOf(currentBlock);
                if (listIdx != -1) _aliveBlockIndices[listIdx] = newStartBlock;
            }
        }

        private void ExecuteBirthInPlace(int startBlock, int startPersonIdx, ref int currentSize, int maxAllowedCapacity, int id)
        {
            if (currentSize + 1 > maxAllowedCapacity)
            {
                // Temporarily store baby data inside a safe index increment overflow register 
                // until the sequential cleanup worker expands the structural layout.
                if (currentSize + 1 > Globals.maxSizeHusholdning)
                    throw new InvalidOperationException("Household size exceeded maximum limit.");

                _expansionQueue.Enqueue(new ExpansionEvent(id, startBlock));
            }

            Ages[startPersonIdx + currentSize] = 0;
            Hfs[startPersonIdx + currentSize] = EHf.Ingen;
            currentSize++;
        }

        private void ExecuteDeathInPlace(int startBlock, int startPersonIdx, ref int currentSize, int targetAge)
        {
            int deadLocalIdx = -1;
            for (int p = 0; p < currentSize; p++)
            {
                if (Ages[startPersonIdx + p] == targetAge)
                {
                    deadLocalIdx = p;
                    break;
                }
            }

            if (deadLocalIdx != -1)
            {
                // Local swap-back: overwrite the dead person slot with the family's last person
                if (deadLocalIdx != currentSize - 1)
                {
                    Ages[startPersonIdx + deadLocalIdx] = Ages[startPersonIdx + (currentSize - 1)];
                    Hfs[startPersonIdx + deadLocalIdx] = Hfs[startPersonIdx + (currentSize - 1)];
                }
                currentSize--;
            }
        }
    }
}