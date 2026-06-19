using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics; // IDE hover

namespace Households
{
    public static class Constants
    {
        public static int nThreads = 1;
    }

    public enum LifecycleType
    {
        Birth,
        Death
    }

    class Program
    {
        static void Main(string[] args)
        {
            var sim = new Simulation();
            sim.InitializeBucket(size: 1, cap: 10);
            sim.InitializeBucket(size: 2, cap: 10);
            sim.InitializeBucket(size: 3, cap: 10);

            sim.CreateHousehold(101, new[] { 45, 21 }, new[] { "Prof bachelor", "Almen gym" });
            sim.CreateHousehold(102, new[] { 64, 62 }, new[] { "Kandidat", "Prof bachelor" });
            sim.CreateHousehold(103, new[] { 32, 5 }, new[] { "PhD", "Ingen" });

            sim.PrintState();

            sim.RunYearlySimulationCycle();

            sim.PrintState();
        }
    }


    [DebuggerDisplay("Household ID: {Id}, Size: {Size}")]
    [DebuggerTypeProxy(typeof(HouseholdDebugView))]
    public readonly struct Household
    {
        private readonly HouseholdsOfGivenSize _bucket;
        private readonly int _localHIdx;

        public int Id { get; }
        public int Size => _bucket.HouseholdSize;

        public Household(HouseholdsOfGivenSize bucket, int localHouseholdIndex, int id)
        {
            _bucket = bucket;
            _localHIdx = localHouseholdIndex;
            Id = id;
        }

        public PersonList Members => new PersonList(_bucket, _localHIdx, Size);

        /// <summary>
        /// Til brug for debugging
        /// </summary>
        internal class HouseholdDebugView
        {
            private readonly Household _household;
            public HouseholdDebugView(Household household) => _household = household;

            public int Id => _household.Id;
            public int Size => _household.Size;

            /// <summary>
            /// Husholdningens personer vises med hover-over som "Persons", som ligner en List&lt;Person>
            /// </summary>
            public Person[] Persons
            {
                get
                {
                    var items = new Person[_household.Size];
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
    public readonly struct Person
    {
        private readonly HouseholdsOfGivenSize _storage;
        public int AbsIdx { get; }

        public Person(HouseholdsOfGivenSize bucket, int absoluteIndex)
        {
            _storage = bucket;
            AbsIdx = absoluteIndex;
        }

        public int Age
        {
            get => _storage.AgesSpan[AbsIdx];
            set => _storage.AgesSpan[AbsIdx] = value;
        }

        public string Hf
        {
            get => _storage.HfsSpan[AbsIdx];
            set => _storage.HfsSpan[AbsIdx] = value;
        }

        /// <summary>
        /// Peger tilbage til personens husholdning
        /// </summary>
        public Household Household
        {
            get
            {
                int localHIdx = AbsIdx / _storage.HouseholdSize;
                int hhId = _storage.GetHouseholdId(localHIdx);
                return new Household(_storage, localHIdx, hhId);
            }
        }
    }

    [DebuggerDisplay("Count = {Count}")]
    public readonly struct PersonList
    {
        private readonly HouseholdsOfGivenSize _bucket;
        private readonly int _localHIdx;
        private readonly int _size;

        public PersonList(HouseholdsOfGivenSize bucket, int localHIdx, int size)
        {
            _bucket = bucket;
            _localHIdx = localHIdx;
            _size = size;
        }

        public int Count => _size;

        public Person this[int index]
        {
            get
            {
                if (index < 0 || index >= _size) throw new IndexOutOfRangeException();
                return new Person(_bucket, _localHIdx * _size + index);
            }
            set
            {
                int absIdx = _localHIdx * _size + index;
                _bucket.AgesSpan[absIdx] = value.Age;
                _bucket.HfsSpan[absIdx] = value.Hf;
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(_bucket, _localHIdx, _size);

        public struct Enumerator
        {
            private readonly HouseholdsOfGivenSize _bucket;
            private readonly int _localHIdx;
            private readonly int _size;
            private int _index;

            public Enumerator(HouseholdsOfGivenSize bucket, int localHIdx, int size)
            {
                _bucket = bucket; _localHIdx = localHIdx; _size = size; _index = -1;
            }
            public bool MoveNext() => ++_index < _size;
            public Person Current => new Person(_bucket, _localHIdx * _size + _index);
        }
    }

    public struct HouseholdPosition
    {
        public int HouseholdSize;
        public int LocalIndex;
        public HouseholdPosition(int size, int idx) { HouseholdSize = size; LocalIndex = idx; }
    }

    public struct PendingEvent
    {
        public int HouseholdId;
        public LifecycleType Type;
        public int TargetPersonAbsIdx;

        public PendingEvent(int id, LifecycleType type, int targetIdx = -1)
        {
            HouseholdId = id; Type = type; TargetPersonAbsIdx = targetIdx;
        }
    }

    /// <summary>
    /// Simulation af modellen
    /// </summary>
    public class Simulation
    {
        private Dictionary<int, HouseholdsOfGivenSize> _buckets = new Dictionary<int, HouseholdsOfGivenSize>();
        private Dictionary<int, HouseholdPosition> _registry = new Dictionary<int, HouseholdPosition>();
        private ConcurrentQueue<PendingEvent> _structuralEventQueue = new ConcurrentQueue<PendingEvent>();

        public void InitializeBucket(int size, int cap) => _buckets[size] = new HouseholdsOfGivenSize(size, cap);

        public void CreateHousehold(int id, int[] ages, string[] educations)
        {
            int size = ages.Length;
            if (!_buckets.ContainsKey(size)) InitializeBucket(size, 1000);
            int localIdx = _buckets[size].InsertHousehold(id, ages, educations);
            _registry[id] = new HouseholdPosition(size, localIdx);
        }

        public void RunYearlySimulationCycle()
        {
            foreach (var bucket in _buckets.Values)
            {
                bucket.ParallelYearlyUpdate(_structuralEventQueue);
            }

            Console.WriteLine($"\nProcessing {_structuralEventQueue.Count} structural events sequentially...");
            while (_structuralEventQueue.TryDequeue(out PendingEvent ev))
            {
                // Verify household still exists in the registry
                if (!_registry.TryGetValue(ev.HouseholdId, out var loc)) continue;

                var bucket = _buckets[loc.HouseholdSize];

                if (ev.Type == LifecycleType.Birth)
                {
                    int newSize = loc.HouseholdSize + 1;
                    if (!_buckets.ContainsKey(newSize)) InitializeBucket(newSize, 1000);

                    var oldAges = bucket.AgesSpan.Slice(loc.LocalIndex * loc.HouseholdSize, loc.HouseholdSize);
                    var oldEdus = bucket.HfsSpan.Slice(loc.LocalIndex * loc.HouseholdSize, loc.HouseholdSize);

                    int[] newAges = new int[newSize];
                    string[] newEdus = new string[newSize];
                    oldAges.CopyTo(newAges.AsSpan());
                    oldEdus.CopyTo(newEdus.AsSpan());

                    newAges[newSize - 1] = 0;
                    newEdus[newSize - 1] = "None";

                    bucket.RemoveHouseholdAndSwapLast(loc.LocalIndex, (shiftedId, newLocalIdx) =>
                    {
                        _registry[shiftedId] = new HouseholdPosition(loc.HouseholdSize, newLocalIdx);
                    });

                    int newLocalIdx = _buckets[newSize].InsertHousehold(ev.HouseholdId, newAges, newEdus);
                    _registry[ev.HouseholdId] = new HouseholdPosition(newSize, newLocalIdx);
                }
                else if (ev.Type == LifecycleType.Death)
                {
                    int newSize = loc.HouseholdSize - 1;
                    int startIdx = loc.LocalIndex * loc.HouseholdSize;

                    // FIX: Instead of relying on a stale absolute index from before previous mutations,
                    // calculate the local index relative to the household's CURRENT bucket position.
                    // If the absolute index is out of bounds for the current household position, safely default to the last person.
                    int targetLocalPersonIdx = ev.TargetPersonAbsIdx - startIdx;
                    if (targetLocalPersonIdx < 0 || targetLocalPersonIdx >= loc.HouseholdSize)
                    {
                        targetLocalPersonIdx = loc.HouseholdSize - 1;
                    }

                    if (newSize == 0)
                    {
                        // The last member died, entirely dissolve the household
                        bucket.RemoveHouseholdAndSwapLast(loc.LocalIndex, (shiftedId, newLocalIdx) =>
                        {
                            _registry[shiftedId] = new HouseholdPosition(loc.HouseholdSize, newLocalIdx);
                        });
                        _registry.Remove(ev.HouseholdId);
                    }
                    else
                    {
                        if (!_buckets.ContainsKey(newSize)) InitializeBucket(newSize, 1000);

                        int[] newAges = new int[newSize];
                        string[] newEdus = new string[newSize];
                        int destIdx = 0;

                        // Transfer everyone except the deceased person
                        for (int i = 0; i < loc.HouseholdSize; i++)
                        {
                            if (i == targetLocalPersonIdx) continue;
                            newAges[destIdx] = bucket.AgesSpan[startIdx + i];
                            newEdus[destIdx] = bucket.HfsSpan[startIdx + i];
                            destIdx++;
                        }

                        bucket.RemoveHouseholdAndSwapLast(loc.LocalIndex, (shiftedId, newLocalIdx) =>
                        {
                            _registry[shiftedId] = new HouseholdPosition(loc.HouseholdSize, newLocalIdx);
                        });

                        int newLocalIdx = _buckets[newSize].InsertHousehold(ev.HouseholdId, newAges, newEdus);
                        _registry[ev.HouseholdId] = new HouseholdPosition(newSize, newLocalIdx);
                    }
                }
            }
        }

        public void PrintState() { foreach (var b in _buckets.Values) b.PrintBucketState(); }
    }

    /// <summary>
    /// Her opbevares data for familier med given HouseholdSize. Hvis HouseholdSize er 3, vil elementer 0, 1, 2 høre til første familie,
    /// elementer 3, 4, 5 til anden familie osv.
    /// </summary>
    public class HouseholdsOfGivenSize
    {
        public int HouseholdSize { get; }
        public int HouseholdCount { get; private set; }

        private int[] _ages;
        private string[] _hfs;
        private int[] _householdIds;

        public HouseholdsOfGivenSize(int householdSize, int initialCapacity)
        {
            HouseholdSize = householdSize;
            _ages = new int[initialCapacity * householdSize];
            _hfs = new string[initialCapacity * householdSize];
            _householdIds = new int[initialCapacity];
        }

        public Span<int> AgesSpan => _ages.AsSpan();
        public Span<string> HfsSpan => _hfs.AsSpan();
        public int GetHouseholdId(int localHIdx) => _householdIds[localHIdx];

        public int InsertHousehold(int householdId, int[] ages, string[] educations)
        {
            int localHIdx = HouseholdCount;
            _householdIds[localHIdx] = householdId;
            int startPersonIdx = localHIdx * HouseholdSize;

            for (int i = 0; i < HouseholdSize; i++)
            {
                _ages[startPersonIdx + i] = ages[i];
                _hfs[startPersonIdx + i] = educations[i];
            }
            HouseholdCount++;
            return localHIdx;
        }

        public void RemoveHouseholdAndSwapLast(int targetLocalHIdx, Action<int, int> onLastHouseholdMoved)
        {
            int lastLocalHIdx = HouseholdCount - 1;
            if (targetLocalHIdx != lastLocalHIdx)
            {
                int targetStart = targetLocalHIdx * HouseholdSize;
                int lastStart = lastLocalHIdx * HouseholdSize;
                _householdIds[targetLocalHIdx] = _householdIds[lastLocalHIdx];

                for (int i = 0; i < HouseholdSize; i++)
                {
                    _ages[targetStart + i] = _ages[lastStart + i];
                    _hfs[targetStart + i] = _hfs[lastStart + i];
                }
                onLastHouseholdMoved(_householdIds[targetLocalHIdx], targetLocalHIdx);
            }
            HouseholdCount--;
        }

        public void ParallelYearlyUpdate(ConcurrentQueue<PendingEvent> eventQueue)
        {
            Parallel.For(0, HouseholdCount, new ParallelOptions { MaxDegreeOfParallelism = Constants.nThreads }, h =>
            {
                int id = _householdIds[h];
                Household household = new Household(this, h, id);

                // Age the household members
                for (int p = 0; p < household.Members.Count; p++)
                {
                    Person person = household.Members[p];
                    person.Age += 1;
                }

                if (id == 101)
                {
                    eventQueue.Enqueue(new PendingEvent(id, LifecycleType.Birth));
                }
                else if (id == 102)
                {
                    int deadAbsIdx = -1;
                    for (int p = 0; p < household.Members.Count; p++)
                        if (household.Members[p].Age == 65) deadAbsIdx = household.Members[p].AbsIdx;

                    eventQueue.Enqueue(new PendingEvent(id, LifecycleType.Death, deadAbsIdx));
                    eventQueue.Enqueue(new PendingEvent(id, LifecycleType.Birth));
                }
                else if (id == 103)
                {
                    int deadAbsIdx = -1;
                    for (int p = 0; p < household.Members.Count; p++)
                        if (household.Members[p].Age == 33) deadAbsIdx = household.Members[p].AbsIdx;

                    eventQueue.Enqueue(new PendingEvent(id, LifecycleType.Death, deadAbsIdx));
                }
            });
        }

        public void PrintBucketState()
        {
            if (HouseholdCount == 0) return;
            Console.WriteLine($"\n--- Household size {HouseholdSize} (Count: {HouseholdCount}) ---");
            for (int h = 0; h < HouseholdCount; h++)
            {
                Console.Write($"  Household ID {_householdIds[h]}: ");
                int start = h * HouseholdSize;
                for (int p = 0; p < HouseholdSize; p++)
                    Console.Write($"[Age: {_ages[start + p]}, Edu: {_hfs[start + p]}] ");
                Console.WriteLine();
            }
        }
    }
}